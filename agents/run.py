"""Good Enough Repairs - economy agent crew (raw orchestration).

Three agents work together to produce economy.json, the game's economy values,
which Unity loads at start:

  Director          reads the GDD economy rules, writes the task card,
                    and makes the final call (approve or escalate to the designer).
  Economy Balancer  proposes the numbers in economy.json.
  QA Playtester     runs the economy simulation (buyer, swapper and releaser bots),
                    checks the GDD targets and writes the report with feedback.

Balancer and QA loop until every target passes, at most 3 rounds (GDD: after 3
failed tuning rounds the designer decides).

Agents run through the Claude Code CLI (`claude -p`), so they use your Claude
login - no API key. `--mock` runs scripted stand-ins with no Claude calls.

Usage (from the project root):
  python agents/run.py            real agents
  python agents/run.py --mock     no Claude calls, for a quick test
"""
import argparse
import datetime
import json
import math
import pathlib
import re
import shutil
import subprocess
import sys

HERE = pathlib.Path(__file__).resolve().parent
ROOT = HERE.parent
sys.path.insert(0, str(HERE / "sim"))
import economy_sim as sim  # noqa: E402

ROLES = HERE / "roles"
CONTEXT = HERE / "context" / "gdd_economy.md"
START = HERE / "data" / "economy_start.json"
REPORTS = HERE / "reports"
OUTPUT = ROOT / "Assets" / "Data" / "economy.json"

MODELS = {"director": "sonnet", "economy_balancer": "sonnet", "qa_playtester": "haiku"}
MAX_ROUNDS = 3


# ------------------------------------------------------------------ plumbing

def log(who, msg):
    print(f"[{datetime.datetime.now():%H:%M:%S}] {who:<17} {msg}", flush=True)


def call_claude(role, prompt):
    """One agent turn: role prompt + task through `claude -p`, plain text back."""
    exe = shutil.which("claude")
    if not exe:
        sys.exit("Claude Code CLI not found. Install it and log in, or run with --mock.")
    system = (ROLES / f"{role}.md").read_text(encoding="utf-8")
    full = f"{system}\n\n---\n\n{prompt}"
    res = subprocess.run([exe, "-p", "--model", MODELS[role]], input=full, capture_output=True,
                         text=True, encoding="utf-8", timeout=600)
    if res.returncode != 0:
        raise RuntimeError(f"{role} failed: {res.stderr.strip()[:500]}")
    return res.stdout.strip()


def extract_json(text):
    """Take the first JSON object from an agent reply (agents may add ``` fences)."""
    m = re.search(r"\{.*\}", text, re.S)
    if not m:
        raise ValueError("no JSON object in reply")
    return json.loads(m.group(0))


def validate(eco):
    """Rules the Balancer may not break. Returns a list of problems (empty = ok)."""
    problems = []
    need = ["startingCredits", "stationUpkeep", "scrapPrice", "partPrice", "penaltyPercent",
            "fighterPayment", "freighterPayment", "junkyardPartPrice", "junkyardParts",
            "junkyardJunk", "days"]
    for k in need:
        if k not in eco:
            problems.append(f"missing key {k}")
    if problems:
        return problems
    for k in ["partPrice", "fighterPayment", "freighterPayment", "junkyardJunk"]:
        r = eco[k]
        if not (isinstance(r.get("min"), int) and isinstance(r.get("max"), int) and 0 <= r["min"] <= r["max"]):
            problems.append(f"{k} must be whole numbers with 0 <= min <= max")
    if len(eco["days"]) != 7:
        problems.append("there must be exactly 7 days")
    for d in eco["days"]:
        if not (1 <= d.get("weakMin", 0) <= d.get("weakMax", 0) <= 3):
            problems.append(f"day {d.get('day')}: ships arrive with 1-3 weak parts (1 <= weakMin <= weakMax <= 3)")
        if d.get("type") not in ("quiet", "busy"):
            problems.append(f"day {d.get('day')}: type must be quiet or busy")
        if d.get("type") == "quiet" and not 5 <= d.get("ships", 0) <= 10:
            problems.append(f"day {d.get('day')}: a quiet day has 5-10 ships")
    if not eco["junkyardJunk"]["max"] <= eco["junkyardParts"]:
        problems.append("junkyardJunk.max cannot exceed junkyardParts")
    if not eco["scrapPrice"] < eco["junkyardPartPrice"] < eco["partPrice"]["min"]:
        problems.append("junkyard part price must be above the scrap price and below the cheapest new part")
    return problems


# ------------------------------------------------------------------ agents

def director_task(mock):
    gdd = CONTEXT.read_text(encoding="utf-8")
    if mock:
        return ("# Task card: tune economy.json\n"
                "Goal: economy values where swapping beats buying.\n"
                "Done when every QA target passes. Change numbers only, never the rules.")
    targets = "\n".join(f"- {desc}" for _, desc, _ in sim.TARGETS)
    return call_claude("director", f"Write the task card for the Economy Balancer.\n\nGDD economy section:\n\n{gdd}\n\n"
                                   f"The QA Playtester checks exactly these targets (list them as they are):\n{targets}")


def balancer_propose(task, current, qa_report, mock):
    if mock:
        return mock_balancer(current, qa_report)
    prompt = (f"Task card:\n{task}\n\nCurrent economy.json:\n```json\n{json.dumps(current, indent=2)}\n```\n\n"
              f"QA report from the last round:\n{qa_report['text'] if qa_report else 'None yet - this is round 1.'}\n\n"
              "Reply with the complete new economy.json only.")
    return extract_json(call_claude("economy_balancer", prompt))


def qa_review(eco, results, checks, mock):
    if mock:
        failed = [c["target"] for c in checks if not c["pass"]]
        return {"summary": "All targets pass." if not failed else f"{len(failed)} target(s) fail.",
                "feedback": failed}
    prompt = (f"economy.json under test:\n```json\n{json.dumps(eco, indent=2)}\n```\n\n"
              f"Simulation results (300 runs per bot):\n```json\n{json.dumps(results, indent=2)}\n```\n\n"
              f"Target checks:\n```json\n{json.dumps(checks, indent=2)}\n```")
    return extract_json(call_claude("qa_playtester", prompt))


def director_decide(task, history, passed, mock):
    if mock:
        return ("Approved: every target passes. economy.json goes to the game."
                if passed else "Escalated to the designer: 3 tuning rounds failed. Best candidate saved in the report.")
    rounds = "\n\n".join(f"Round {h['round']}: checks {json.dumps(h['checks'])}\nQA: {json.dumps(h['qa'])}"
                         for h in history)
    return call_claude("director", f"Task card:\n{task}\n\nTuning history:\n{rounds}\n\n"
                                   f"All targets passed in the last round: {passed}. Write your decision.")


def mock_balancer(cur, qa_report):
    """Scripted stand-in for the Economy Balancer: tries a small grid of tweaks on a
    quick simulation and keeps the one that passes the most targets."""
    if qa_report is None:
        return json.loads(json.dumps(cur))  # round 1: test the GDD's starting values as they are
    best = None
    for pay in (0, 50, 100):
        for part in (0, 25):
            for late in (0, 1):
                for upkeep in (0, -20):
                    eco = json.loads(json.dumps(cur))
                    for k in ("fighterPayment", "freighterPayment"):
                        eco[k] = {"min": eco[k]["min"] + pay, "max": eco[k]["max"] + pay}
                    eco["partPrice"] = {"min": eco["partPrice"]["min"] + part, "max": eco["partPrice"]["max"] + part}
                    for d in eco["days"][5:]:
                        d["weakMin"] = max(1, d["weakMin"] - late)
                    eco["stationUpkeep"] += upkeep
                    lowest = min(eco["fighterPayment"]["min"], eco["freighterPayment"]["min"])
                    if eco["partPrice"]["min"] * (1 + eco["penaltyPercent"] / 100) <= lowest:
                        eco["penaltyPercent"] = math.ceil((lowest / eco["partPrice"]["min"] - 1) * 10) * 10 + 10
                    n = sum(c["pass"] for c in sim.check(eco, sim.simulate(eco, 100)))
                    size = abs(pay) + abs(part) + 50 * late + abs(upkeep)  # prefer small changes
                    if best is None or (n, -size) > (best[0], -best[1]):
                        best = (n, size, eco)
    eco = best[2]
    eco["version"] = f"mock round: grid search, {best[0]}/5 targets on a quick check"
    return eco


# ------------------------------------------------------------------ main loop

def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--mock", action="store_true", help="scripted agents, no Claude calls")
    ap.add_argument("--runs", type=int, default=300, help="simulated runs per bot")
    args = ap.parse_args()

    REPORTS.mkdir(exist_ok=True)
    stamp = datetime.datetime.now().strftime("%Y-%m-%d_%H%M")
    mode = "mock" if args.mock else "claude"
    log("Crew", f"Good Enough Repairs economy crew - mode: {mode}")

    log("Director", "reading the GDD economy section, writing the task card")
    task = director_task(args.mock)

    current = json.loads((OUTPUT if OUTPUT.exists() else START).read_text(encoding="utf-8"))
    qa_report, history, best, passed = None, [], None, False
    for rnd in range(1, MAX_ROUNDS + 1):
        log("Economy Balancer", f"round {rnd}: proposing economy.json")
        try:
            proposal = balancer_propose(task, current, qa_report, args.mock)
            problems = validate(proposal)
        except Exception as e:  # bad reply: count it as a failed round, keep going
            proposal, problems = current, [f"Balancer reply unusable: {e}"]
        if problems:
            log("QA Playtester", f"round {rnd}: rejected before simulation - {problems}")
            checks = [{"id": "valid", "target": "economy.json follows the GDD rules", "pass": False}]
            qa = {"summary": "Invalid economy.json", "feedback": problems}
            results = {}
        else:
            current = proposal
            log("QA Playtester", f"round {rnd}: simulating {args.runs} runs x 3 bots + worst-case day")
            results = sim.simulate(current, args.runs)
            checks = sim.check(current, results)
            try:
                qa = qa_review(current, results, checks, args.mock)
            except Exception as e:
                qa = {"summary": f"QA reply unusable ({e}); raw checks attached.",
                      "feedback": [c["target"] for c in checks if not c["pass"]]}
        n_pass = sum(c["pass"] for c in checks)
        log("QA Playtester", f"round {rnd}: {n_pass}/{len(checks)} targets pass - {qa.get('summary', '')}")
        qa_report = {"checks": checks, "results": results,
                     "text": json.dumps({"checks": checks, "results": results, "qa": qa}, indent=2)}
        history.append({"round": rnd, "economy": current, "checks": checks, "results": results, "qa": qa})
        if best is None or n_pass > best[0]:
            best = (n_pass, current)
        if results and all(c["pass"] for c in checks):
            passed = True
            break

    log("Director", "reviewing the rounds, making the call")
    decision = director_decide(task, history, passed, args.mock)

    if passed:
        OUTPUT.parent.mkdir(parents=True, exist_ok=True)
        current["version"] = f"crew-{stamp}"
        OUTPUT.write_text(json.dumps(current, indent=2) + "\n", encoding="utf-8")
        log("Director", f"APPROVED - wrote {OUTPUT.relative_to(ROOT)}")
    else:
        log("Director", "ESCALATED to the designer - economy.json in the game is unchanged")

    report = REPORTS / f"{stamp}_{mode}.md"
    lines = [f"# Economy crew report - {stamp} ({mode})", "",
             f"**Result:** {'approved' if passed else 'escalated to the designer'}", "",
             "## Director - task card", "", task, ""]
    for h in history:
        lines += [f"## Round {h['round']}", "",
                  "| Target | Pass |", "|---|---|"]
        lines += [f"| {c['target']} | {'yes' if c['pass'] else 'NO'} |" for c in h["checks"]]
        lines += ["", f"**QA Playtester:** {h['qa'].get('summary', '')}", ""]
        lines += [f"- {f}" for f in h["qa"].get("feedback", [])]
        lines += ["", "<details><summary>economy.json and simulation results</summary>", "",
                  "```json", json.dumps({"economy": h["economy"], "results": h["results"]}, indent=2), "```",
                  "", "</details>", ""]
    lines += ["## Director - decision", "", decision, ""]
    if not passed and best:
        lines += ["## Best candidate (for the designer)", "", "```json", json.dumps(best[1], indent=2), "```"]
    report.write_text("\n".join(lines), encoding="utf-8")
    log("Crew", f"report: {report.relative_to(ROOT)}")


if __name__ == "__main__":
    main()
