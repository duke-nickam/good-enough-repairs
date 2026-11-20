"""Good Enough Repairs - the style loop: Generator -> Evaluator -> Refiner for the mechanic's lines.

The style guide (docs/style-guide.md) is the single source: its rule table is read here, so the page and the loop
never disagree. The crew's own agents do the work, through the same call as the agent board:
  Generator  UX Designer (its normal fast model): writes the lines for one moment
  Evaluator  Director (large model) for the rules judged by reading; code for the rules code can measure.
             Every rule gets a score 0-10, a reason and the words that broke it; the total is out of 100.
  Refiner    UX Designer on its large model (the GDD's "large model if it makes too many mistakes"):
             rewrites only what scored low, and says what it changed and why.
The loop runs until the total reaches the bar (90, no rule below 6) or the round limit, with no human in it.
Every round is logged to docs/assignment-6/.

Usage (from the project root):
  python agents/style_loop.py <moment> [--count 3] [--seed <file.json>] [--threshold 90] [--rounds 5]
    moment   one of the moments in mechanic_lines.json, e.g. buy_warning (not tutorial_hint: its text is fixed)
    --seed   start from a given draft instead of generating one: a JSON file {"moment": ..., "lines": [...]}
"""
import argparse
import datetime
import difflib
import json
import pathlib
import re
import sys

HERE = pathlib.Path(__file__).resolve().parent
ROOT = HERE.parent
sys.path.insert(0, str(HERE))

from board.agents import AgentError, ClaudeAgents, extract_json, gdd_sections, total_tokens  # noqa: E402

GUIDE = ROOT / "docs" / "style-guide.md"
LINES = ROOT / "Assets" / "StreamingAssets" / "content" / "mechanic_lines.json"
OUT = ROOT / "docs" / "assignment-6"
SCIFI = ("warp", "hyperdrive", "hyperspace", "laser", "droid", "alien", "empire", "federation", "starfleet", "captain")
LONG_DASHES = ("—", "–")


# ------------------------------------------------------------ the style guide

def load_guide():
    """-> (guide text, rules from its table: id, rule, weight, judge, source; moments: id -> (when, job))."""
    text = GUIDE.read_text(encoding="utf-8")
    rules, moments = [], {}
    for line in text.splitlines():
        cells = [c.strip() for c in line.strip().strip("|").split("|")] if line.startswith("|") else []
        if len(cells) == 5 and re.fullmatch(r"[A-Z]\d", cells[0]):
            rules.append({"id": cells[0], "rule": cells[1], "weight": int(cells[2]), "judge": cells[3],
                          "source": cells[4]})
        elif len(cells) == 3 and re.fullmatch(r"[a-z_]+", cells[0]):
            moments[cells[0]] = (cells[1], cells[2])
    if sum(r["weight"] for r in rules) != 100:
        sys.exit(f"The style guide's weights add up to {sum(r['weight'] for r in rules)}, not 100.")
    return text, rules, moments


# ------------------------------------------------------------ the rules code measures

def sentences(line):
    return [s for s in re.split(r"(?<=[.!?])\s+", line.strip()) if s]


def code_scores(moment, lines, count):
    """The rules judged by code: F1-F4. Each -> (score 0-10, reason, evidence)."""
    out = {}
    bad = [l for l in lines if re.search(r"\d", l)]
    out["F1"] = (max(0, 10 - 5 * len(bad)), f"{len(bad)} line(s) with digits" if bad else "no digits",
                 " | ".join(bad))
    bad = [l for l in lines if any(d in l for d in LONG_DASHES)]
    out["F2"] = (max(0, 10 - 5 * len(bad)), f"{len(bad)} line(s) with a long dash" if bad else "no long dashes",
                 " | ".join(bad))
    bad = [l for l in lines if len(l) > 160 or len(sentences(l)) > 3]
    out["F3"] = (max(0, 10 - 3 * len(bad)),
                 f"{len(bad)} line(s) over 160 characters or 3 sentences" if bad else "every line short and plain",
                 " | ".join(f"{len(l)} chars, {len(sentences(l))} sentences: {l}" for l in bad))
    problems, evidence = [], []
    if len(lines) != count:
        problems.append(f"{len(lines)} lines instead of {count}")
    for i, a in enumerate(lines):
        for b in lines[i + 1:]:
            if difflib.SequenceMatcher(None, a.lower(), b.lower()).ratio() > 0.8:
                problems.append("two lines nearly the same")
                evidence.append(f"{a} / {b}")
    for l in lines:
        words = [w for w in SCIFI if re.search(rf"\b{w}", l, re.I)]
        if words:
            problems.append("generic sci-fi word: " + ", ".join(words))
            evidence.append(l)
        if moment == "fallback_hint" and re.search(r"\binventory\b", l, re.I):
            problems.append("a fallback hint says inventory, not on screen")
            evidence.append(l)
    out["F4"] = (max(0, 10 - 3 * len(problems)), "; ".join(problems) or "right count, all different, own world",
                 " | ".join(evidence))
    return out


# ------------------------------------------------------------ the three agents

GEN_FORMAT = """Task: write the mechanic's lines for one moment of the game.
Reply with one JSON object only, no other text: {"lines": ["<line>", ...]}"""

EVAL_FORMAT = """Task: score the mechanic's lines against the style guide, as a strict but fair editor.
Score only the rules listed for you, each from 0 to 10 for the lines as a set (10 = every line fully follows it).
A rule that limits what a line may say scores 10 when nothing in the lines breaks it: never mark a line down for
leaving out something the rule does not require, such as station imagery in a line that does not show the station.
For every rule give the reason, and as evidence quote the exact words that broke it (empty if none).
Reply with one JSON object only, no other text:
{"rules": [{"id": "<rule id>", "score": <0-10>, "reason": "<why>", "evidence": "<exact words>"}],
 "summary": "<one sentence: the biggest problem left>"}"""

REFINE_FORMAT = """Task: improve the mechanic's lines using the Evaluator's report.
Change only what the low-scoring rules point at. Keep every line that already scores well, word for word.
Reply with one JSON object only, no other text:
{"lines": ["<line>", ...], "changes": ["line <n>: <what changed> (<rule id>)"]}"""


class Loop:
    def __init__(self, moment, count, threshold, rounds):
        self.moment, self.count, self.threshold, self.rounds = moment, count, threshold, rounds
        self.guide, self.rules, self.moments = load_guide()
        if moment not in self.moments:
            sys.exit(f"Unknown moment {moment!r}. Moments: {', '.join(self.moments)}")
        settings = json.loads((HERE / "board" / "settings.json").read_text(encoding="utf-8"))
        gdd = (ROOT / settings["gdd"]).read_text(encoding="utf-8")
        self.agents = ClaudeAgents(settings, gdd)
        self.gdd_part = gdd_sections(gdd, settings["reads"]["ux_designer"])
        current = json.loads(LINES.read_text(encoding="utf-8"))["moments"]
        self.examples = "\n".join(f"- {m}: {ls[0]}" for m, ls in current.items()
                                  if m != moment and m != "tutorial_hint" and ls)
        self.tokens = 0

    def call(self, agent, fmt, prompt, attempt=1):
        for k in (1, 2):  # one retry if the reply is not usable JSON
            try:
                text, usage, model = self.agents._call(agent, fmt, prompt, attempt)
                self.tokens += total_tokens(usage)
                return extract_json(text), model, total_tokens(usage)
            except (AgentError, ValueError) as e:
                if k == 2:
                    raise
                print(f"  ({agent}: unusable reply, asking again: {e})")

    def moment_text(self):
        when, job = self.moments[self.moment]
        return f"Moment: {self.moment}\nWhen it shows: {when}\nWhat the line must do: {job}"

    def generate(self):
        prompt = (f"{self.moment_text()}\n\nWrite {self.count} different lines for this moment.\n\n---\n\n"
                  f"The style guide:\n\n{self.guide}\n\n---\n\nThe mechanic's voice in other moments (do not reuse "
                  f"these lines):\n{self.examples}\n\n---\n\nGDD sections:\n\n{self.gdd_part}")
        reply, model, used = self.call("ux_designer", GEN_FORMAT, prompt)
        return [str(l).strip() for l in reply.get("lines") or []], model, used

    def evaluate(self, lines):
        judged = [r for r in self.rules if r["judge"] == "Evaluator"]
        prompt = (f"{self.moment_text()}\n\nThe lines:\n" + "\n".join(f"{i}. {l}" for i, l in enumerate(lines, 1)) +
                  "\n\nScore these rules: " + ", ".join(r["id"] for r in judged) +
                  f"\n\n---\n\nThe style guide:\n\n{self.guide}\n\n---\n\nGDD:\n\n{self.gdd_part}")
        reply, model, used = self.call("director", EVAL_FORMAT, prompt)
        got = {r.get("id"): r for r in reply.get("rules") or []}
        scores = code_scores(self.moment, lines, self.count)
        report = []
        for r in self.rules:
            if r["judge"] == "Code":
                s, reason, evidence = scores[r["id"]]
            else:
                g = got.get(r["id"]) or {"score": 0, "reason": "the Evaluator gave no score", "evidence": ""}
                s, reason, evidence = int(max(0, min(10, int(g.get("score", 0))))), g.get("reason", ""), \
                    g.get("evidence", "")
            report.append({"id": r["id"], "weight": r["weight"], "judge": r["judge"], "score": s, "reason": reason,
                           "evidence": evidence})
        total = round(sum(x["weight"] * x["score"] / 10 for x in report), 1)
        return report, total, reply.get("summary", ""), model, used

    def passes(self, report, total):
        return total >= self.threshold and min(x["score"] for x in report) >= 6

    def refine(self, lines, report, total):
        low = [x for x in report if x["score"] < 9]
        prompt = (f"{self.moment_text()}\n\nThe lines (total score {total}/100):\n" +
                  "\n".join(f"{i}. {l}" for i, l in enumerate(lines, 1)) +
                  "\n\nThe Evaluator's report, rules that scored below 9:\n" +
                  "\n".join(f"- {x['id']} ({x['score']}/10): {x['reason']} Evidence: {x['evidence'] or '-'}"
                            for x in low) +
                  f"\n\nKeep exactly {self.count} lines.\n\n---\n\nThe style guide:\n\n{self.guide}\n\n---\n\n"
                  f"GDD sections:\n\n{self.gdd_part}")
        reply, model, used = self.call("ux_designer", REFINE_FORMAT, prompt, attempt=2)
        return [str(l).strip() for l in reply.get("lines") or []], reply.get("changes") or [], model, used

    def run(self, seed=None):
        log = {"moment": self.moment, "count": self.count, "threshold": self.threshold, "rounds": [],
               "started": datetime.datetime.now().astimezone().isoformat(timespec="seconds")}
        if seed:
            lines, origin = seed, "seed draft (given, not generated)"
        else:
            lines, model, used = self.generate()
            origin = f"Generator: UX Designer ({model}), {used:,} tokens"
        print(f"[style loop] {self.moment}: {origin}")
        best = None
        for n in range(1, self.rounds + 1):
            report, total, summary, model, used = self.evaluate(lines)
            ok = self.passes(report, total)
            entry = {"round": n, "lines": lines, "origin": origin, "report": report, "total": total,
                     "summary": summary, "evaluator": f"Director ({model}), {used:,} tokens", "passed": ok}
            log["rounds"].append(entry)
            print(f"[style loop] round {n}: {total}/100" + (" - meets the bar" if ok else f" - {summary}"))
            if not best or total > best["total"]:
                best = entry
            if ok or n == self.rounds:
                break
            lines, changes, model, used = self.refine(lines, report, total)
            origin = f"Refiner: UX Designer ({model}), {used:,} tokens"
            entry["changes"] = changes
        log.update(best_round=best["round"], best_total=best["total"], best_lines=best["lines"],
                   passed=best["passed"], tokens=self.tokens)
        return log


# ------------------------------------------------------------ the log

def write_log(log, name):
    OUT.mkdir(parents=True, exist_ok=True)
    (OUT / f"{name}.json").write_text(json.dumps(log, indent=2, ensure_ascii=False) + "\n", encoding="utf-8",
                                      newline="\n")
    md = [f"# Style loop: {log['moment']}", "",
          f"Started {log['started']}. Bar: {log['threshold']}/100 with no rule below 6. "
          f"Result: **{log['best_total']}/100** in round {log['best_round']}"
          f" ({'meets the bar' if log['passed'] else 'did not reach the bar in the round limit'}). "
          f"Tokens: {log['tokens']:,}.", ""]
    for r in log["rounds"]:
        md += [f"## Round {r['round']}: {r['total']}/100", "", f"Lines ({r['origin']}):", ""]
        md += [f"{i}. {l}" for i, l in enumerate(r["lines"], 1)]
        md += ["", f"Evaluator: {r['evaluator']} and code.", "", "| Rule | Weight | Score | Reason | Evidence |",
               "|---|---|---|---|---|"]
        for x in r["report"]:
            md.append(f"| {x['id']} ({x['judge']}) | {x['weight']} | {x['score']}/10 | "
                      f"{x['reason'].replace('|', '/')} | {(x['evidence'] or '-').replace('|', '/')} |")
        md += ["", f"Biggest problem left: {r['summary'] or '-'}", ""]
        if r.get("changes"):
            md += ["Refiner's changes for the next round:", ""] + [f"- {c}" for c in r["changes"]] + [""]
    md += ["## Final lines", ""] + [f"{i}. {l}" for i, l in enumerate(log["best_lines"], 1)] + [""]
    (OUT / f"{name}.md").write_text("\n".join(md), encoding="utf-8", newline="\n")
    return OUT / f"{name}.md"


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("moment")
    ap.add_argument("--count", type=int, default=3)
    ap.add_argument("--seed", help="a JSON file {\"moment\": ..., \"lines\": [...]} to start from")
    ap.add_argument("--threshold", type=float, default=90)
    ap.add_argument("--rounds", type=int, default=5)
    args = ap.parse_args()
    if args.moment == "tutorial_hint":
        sys.exit("The tutorial hint is fixed text in the GDD; it is never generated.")
    seed = None
    if args.seed:
        data = json.loads(pathlib.Path(args.seed).read_text(encoding="utf-8"))
        seed, args.count = [str(l) for l in data["lines"]], len(data["lines"])
    loop = Loop(args.moment, args.count, args.threshold, args.rounds)
    try:
        log = loop.run(seed)
    except AgentError as e:
        sys.exit(f"[style loop] stopped: an agent call failed, so nothing was written. {e}")
    name = base = args.moment + ("-seed" if seed else "")
    k = 1
    while (OUT / f"{name}.md").exists():  # every run keeps its own log
        k += 1
        name = f"{base}-run{k}"
    path = write_log(log, name)
    print(f"[style loop] {'met the bar' if log['passed'] else 'stopped at the round limit'}: "
          f"{log['best_total']}/100, {log['tokens']:,} tokens. Log: {path.relative_to(ROOT).as_posix()}")


if __name__ == "__main__":
    main()
