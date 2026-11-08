"""Waking the agents: builds each prompt, runs `claude -p` with the agent's role as its system prompt,
and reads the reply. Agents use the designer's Claude login; they get no tools, only text in and out."""
import json
import os
import pathlib
import re
import shutil
import subprocess
import tempfile

from .model import AGENTS, CHECK_RE

ROLES = pathlib.Path(__file__).resolve().parent / "roles"
MAX_FILE_CHARS = 60000

PLAN_FORMAT = """Reply with one JSON object only, no other text. Either a question for the designer:
{"type": "question", "text": "<what you need to know, and why>"}
or a note when nothing new needs planning (e.g. the designer only commented on work already planned):
{"type": "note", "text": "<your answer>"}
or a plan:
{"type": "plan", "summary": "<one or two sentences for the designer>",
 "epics": [{"title": "<feature name>", "goal": "<what the player gets, one sentence>",
   "tasks": [{"key": "t1", "title": "<what the agent does, e.g. Write mechanic lines>", "agent": "<agent key>",
     "reviewer": "<agent key, not the same agent>", "what": "<instructions, a few sentences>",
     "acceptance": ["<a criterion the reviewer can check>"], "files": ["<repo path the agent writes>"],
     "reads": ["<repo path the agent needs to read>"], "checks": ["python agents/checks/<name>.py <args>"],
     "depends_on": ["<key of a task that must be Done first>"]}]}]}
Agent keys: """ + ", ".join(AGENTS) + """.
Epics already on the board for this request are planned: plan only what is new.
Rules: each task belongs to one agent and writes only its listed files (text files only). The reviewer judges the
result; a check script (optional) must already exist or be written by an earlier task it depends on. Never list
the GDD (docs/) or anything in agents/board/ or .github/ as a file to write. Keep the plan as small as the request
allows: every task costs tokens."""

WORK_FORMAT = """Reply in exactly this format, nothing before it:
=== FILE: <path from your task's Files> ===
<the complete new content of the file>
=== END FILE ===
(one block per file; every file in full, never a diff)
SUMMARY: <what you changed and why, two or three sentences, for the reviewer>

If you cannot do the task without a decision from the designer, write no files and reply only:
QUESTION: <the question, and why the GDD does not answer it>

You cannot run anything yourself, and you don't need to: after you reply, the runner runs the task's checks on
your files. If they fail, you get their output and can fix your files before the reviewer sees them. Results of
earlier attempts are in the task's thread. Never write that you could not run a check."""

REVIEW_FORMAT = """Reply with one JSON object only, no other text:
{"verdict": "approve" or "reject", "summary": "<one sentence>",
 "issues": [{"where": "<file and place>", "problem": "<what is wrong, and the GDD rule or criterion it breaks>",
             "fix": "<concrete fix>"}]}
Reject only for real problems: a broken acceptance criterion, a GDD contradiction, a failed check, wrong format.
Taste alone is not a reason to reject; mention it as an issue on an approval. At most 6 issues.
The check results above come from the runner, which ran the checks on exactly the files shown: take them as given.
Never reject because the agent did not run or report a check itself."""


class AgentError(RuntimeError):
    pass


def total_tokens(usage):
    return sum(int(usage.get(k) or 0) for k in
               ("input_tokens", "cache_creation_input_tokens", "cache_read_input_tokens", "output_tokens"))


def sum_usage(usages):
    keys = ("input_tokens", "cache_creation_input_tokens", "cache_read_input_tokens", "output_tokens")
    return {k: sum(int(u.get(k) or 0) for u in usages) for k in keys}


def describe_tokens(usage):
    cached = int(usage.get("cache_creation_input_tokens") or 0) + int(usage.get("cache_read_input_tokens") or 0)
    return (f"{total_tokens(usage):,} tokens (in {int(usage.get('input_tokens') or 0):,}, cached {cached:,}, "
            f"out {int(usage.get('output_tokens') or 0):,})")


def gdd_sections(text, names):
    """The GDD split by '## ' headings; ['*'] = everything before the appendix."""
    parts = re.split(r"(?m)^(?=## )", text)
    keep = []
    for s in parts:
        title = s.splitlines()[0][3:].strip() if s.startswith("## ") else ""
        if names == ["*"]:
            if title.startswith("Appendix"):
                break
            keep.append(s)
        elif title in names:
            keep.append(s)
    return "\n".join(keep).strip()


def extract_json(text):
    m = re.search(r"\{.*\}", text, re.S)
    if not m:
        raise AgentError("no JSON object in the reply")
    return json.loads(m.group(0))


def parse_work(text):
    """-> (files {path: content}, summary, question)"""
    q = re.search(r"^QUESTION:\s*(.+)", text, re.M | re.S)
    files = {}
    for path, body in re.findall(r"=== FILE: (.+?) ===\n(.*?)\n=== END FILE ===", text, re.S):
        lines = body.split("\n")
        if len(lines) >= 2 and lines[0].startswith("```") and lines[-1].strip() == "```":
            lines = lines[1:-1]  # the model wrapped the file in a code fence
        files[path.strip()] = "\n".join(lines).rstrip("\n") + "\n"
    s = re.search(r"^SUMMARY:\s*(.+?)(?=^QUESTION:|\Z)", text, re.M | re.S)
    summary = s.group(1).strip() if s else ""
    if q and not files:
        return {}, summary, q.group(1).strip()
    if not files:
        raise AgentError("the reply has no file blocks")
    return files, summary, None


def validate_plan(plan, repo_files):
    """Rules a plan must follow. Returns a list of problems (empty = ok)."""
    p = []
    epics = plan.get("epics")
    if not isinstance(epics, list) or not epics:
        return ["a plan needs at least one epic"]
    keys = {}
    for e in epics:
        if not e.get("title") or not isinstance(e.get("tasks"), list) or not e["tasks"]:
            p.append(f"epic {e.get('title')!r}: needs a title and at least one task")
            continue
        for t in e["tasks"]:
            k = t.get("key")
            if not k or k in keys:
                p.append(f"task {t.get('title')!r}: missing or duplicate key")
                continue
            keys[k] = t
            if t.get("agent") not in AGENTS:
                p.append(f"{k}: unknown agent {t.get('agent')!r}")
            if t.get("reviewer") not in AGENTS or t.get("reviewer") == t.get("agent"):
                p.append(f"{k}: reviewer must be one of the seven agents and not the task's own agent")
            if not t.get("title") or len(t["title"]) > 80:
                p.append(f"{k}: title missing or longer than 80 characters")
            if not t.get("acceptance"):
                p.append(f"{k}: needs acceptance criteria")
            files = t.get("files") or []
            if not files:
                p.append(f"{k}: needs at least one file to write")
            for f in files:
                if f.startswith(("/", "docs/", "agents/board/", ".github/")) or ".." in f or "\\" in f:
                    p.append(f"{k}: may not write {f!r}")
            for c in t.get("checks") or []:
                if not CHECK_RE.match(c):
                    p.append(f"{k}: check {c!r} must look like: python agents/checks/<name>.py <args>")
    for k, t in keys.items():
        for d in t.get("depends_on") or []:
            if d not in keys:
                p.append(f"{k}: depends on unknown task {d!r}")
    seen, stack = set(), set()

    def cyclic(k):
        if k in stack:
            return True
        if k in seen or k not in keys:
            return False
        seen.add(k)
        stack.add(k)
        bad = any(cyclic(d) for d in keys[k].get("depends_on") or [])
        stack.discard(k)
        return bad
    if any(cyclic(k) for k in keys):
        p.append("the dependencies form a loop")
    return p


class ClaudeAgents:
    def __init__(self, settings, gdd_text):
        self.s = settings
        self.gdd = gdd_text

    def _call(self, agent, task_kind, prompt, attempt=1):
        exe = shutil.which("claude")
        if not exe:
            raise AgentError("Claude Code CLI not found. Install it and log in.")
        model = self.s["models"][agent]
        if attempt > 1 and agent in self.s.get("retry_models", {}):
            model = self.s["retry_models"][agent]  # GDD: large model if it makes too many mistakes
        system = "\n\n".join([(ROLES / "_rules.md").read_text(encoding="utf-8"),
                              (ROLES / f"{agent}.md").read_text(encoding="utf-8"), task_kind])
        with tempfile.TemporaryDirectory() as tmp:  # outside the repo, so no project settings are loaded
            sp = pathlib.Path(tmp) / "system.md"
            sp.write_text(system, encoding="utf-8")
            base = [exe, "-p", "--model", model, "--output-format", "json", "--no-session-persistence",
                    "--system-prompt-file", str(sp)]
            res = None
            for extra in (["--tools", ""], []):  # no tools; retry without the flag if this CLI rejects it
                res = subprocess.run(base + extra, input=prompt, capture_output=True, text=True, encoding="utf-8",
                                     cwd=tmp, timeout=900, env={**os.environ})
                if res.returncode == 0:
                    break
        if res.returncode != 0:
            raise AgentError(f"{AGENTS[agent]} call failed: {(res.stderr or res.stdout).strip()[:500]}")
        data = json.loads(res.stdout)
        if data.get("is_error"):
            raise AgentError(f"{AGENTS[agent]}: {data.get('result', '')[:500]}")
        return data.get("result", ""), data.get("usage", {}), model

    # ------------------------------------------------------------ the three kinds of work

    def plan(self, request_thread, board_summary, repo_files, problems=None):
        prompt = (f"GDD:\n\n{gdd_sections(self.gdd, ['*'])}\n\n---\n\nThe designer's request thread:\n\n{request_thread}"
                  f"\n\n---\n\nThe board now:\n{board_summary or 'empty'}\n\nFiles in the repo:\n{repo_files}")
        if problems:
            prompt += "\n\nYour last plan broke these rules; send a corrected one:\n" + "\n".join(f"- {x}" for x in problems)
        text, usage, model = self._call("director", "Task: plan the request.\n\n" + PLAN_FORMAT, prompt)
        return extract_json(text), usage, model

    def work(self, agent, card_md, thread, files, reads, attempt, check_feedback=None):
        prompt = (f"GDD sections:\n\n{gdd_sections(self.gdd, self.s['reads'][agent])}\n\n---\n\n"
                  f"Your task card:\n\n{card_md}\n\n---\n\n"
                  f"Current content of your files:\n\n{_files_block(files)}\n\n---\n\n"
                  f"Files you need to read:\n\n{_files_block(reads)}\n\n---\n\n"
                  f"Earlier in this task (attempts, reviews, the designer's input):\n\n{thread or 'nothing yet'}")
        if check_feedback:
            prompt += ("\n\n---\n\nYour files above are from this attempt, and they failed the checks. Check output:\n"
                       f"{check_feedback}\n\nFix the files and send every file again in full.")
        text, usage, model = self._call(agent, f"Task: attempt {attempt} of your task.\n\n" + WORK_FORMAT, prompt,
                                        attempt)
        return text, usage, model

    def review(self, reviewer, card_md, summary, files, checks, thread):
        prompt = (f"GDD:\n\n{gdd_sections(self.gdd, self.s['reads'][reviewer])}\n\n---\n\n"
                  f"Task card:\n\n{card_md}\n\n---\n\nThe agent's summary: {summary}\n\n"
                  f"Files it wrote:\n\n{_files_block(files)}\n\n---\n\nCheck results:\n{checks}\n\n---\n\n"
                  f"Earlier in this task:\n\n{thread or 'nothing yet'}")
        text, usage, model = self._call(reviewer, "Task: review this task's result.\n\n" + REVIEW_FORMAT, prompt)
        return extract_json(text), usage, model


def _files_block(files):
    if not files:
        return "none"
    out = []
    for path, content in files.items():
        if content is None:
            out.append(f"--- {path} (does not exist yet)")
        else:
            c = content if len(content) <= MAX_FILE_CHARS else content[:MAX_FILE_CHARS] + "\n... (cut)"
            out.append(f"--- {path}\n{c}")
    return "\n\n".join(out)
