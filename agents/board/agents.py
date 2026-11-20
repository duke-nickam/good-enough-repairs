"""Waking the agents: builds each prompt, runs `claude -p` with the agent's role as its system prompt,
and reads the reply. Agents use the designer's Claude login; they get no tools, only text in and out."""
import base64
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
result; a check script (optional) must already exist or be written by an earlier task it depends on.
Every check names the files it checks as arguments, and a check script checks only the files it is given.
A task depends on every task that writes a file its checks or Reads use; a folder counts as every file in it. If a
check should not need another task's file, give the check only this task's own files. Never list a file
in docs/ (the designer's) as a file to write, except in docs/crew/ (the crew's own documents, which the designer
approves), and nothing in agents/board/ or .github/. To move or delete a file, list its old path in
files too: the agent deletes it. Every task that writes C# lists the check "python agents/checks/editmode.py ."
(Unity compiles the project and runs its EditMode tests), once that script exists.
Who reviews whom copies a studio: every task that writes code (C#, or a script in agents/checks/) is reviewed by
lead_programmer; the technical_artist's tasks are reviewed by the ux_designer from screenshots. The programmer
writes the unit tests (EditMode) with its code, and the editor tools, such as the builder tool; the
qa_automation_engineer writes bots, the simulation and check scripts; the qa_playtester tests as a player and never
reads code. The technical_artist does not program: it writes specs (JSON) that the programmer's builder tool turns
into scenes and prefabs, and a check runs the tool in Unity; list the scene or prefab in no task's files.
Everything the player sees (panels, screens, ships, parts, bars) is a prefab from a spec, never built in code at
runtime: the programmer writes the view script and extends the builder tool if the spec needs something new, then a
technical_artist task writes the spec, depending on it. Only the programmer and the qa_automation_engineer write code
(C#, or a script in agents/checks/); the ux_designer designs and reviews, it never writes code. Such a
check prints "MADE: <path>" for each file Unity made (the runner commits them) and "SCREENSHOT: <absolute path>"
for each screenshot the reviewer should see.
You see the GDD and the repo's file names, not the files' contents: never state what a file holds unless the GDD
says so. If a task needs a fact the GDD leaves open (a list, a name, a value two tasks must agree on), ask the
designer instead of choosing it. Formats that the plan's own tasks create are theirs to define. Keep the plan as small as the request
allows: every task costs tokens."""

TRIAGE_FORMAT = """A task's agent stopped with a question instead of finishing. Decide it like a producer.
Reply with one JSON object only, no other text. One of:
{"verdict": "answer", "summary": "<one sentence>", "answer": "<the answer, naming where the GDD, docs/crew/ or the
  card gives it>"}
{"verdict": "plan", "summary": "<one sentence: what is missing and who builds it>",
 "task": {"title": "<what the agent does>", "agent": "<agent key>", "reviewer": "<agent key>", "what": "<instructions>",
   "acceptance": ["<criterion>"], "files": ["<repo path>"], "reads": ["<repo path>"],
   "checks": ["python agents/checks/<name>.py <args>"]}}
{"verdict": "tool", "summary": "<one sentence>", "title": "<the tool, e.g. Builder can save objects hidden>",
 "goal": "<what the tool lets the crew do, one sentence>", "done_when": ["<a fact the designer can check>"]}
{"verdict": "ask", "summary": "<one sentence>", "questions": ["<a question only the designer can decide>"]}
answer: the GDD, docs/crew/ or the card already answer the question.
plan or tool: something outside the agent's files is missing. Choose plan for a small piece only this epic needs,
one task in size. Choose tool for a general tool other epics will use too, or anything bigger than one task: it
becomes its own request with its own epic, done first; the blocked task waits until that request is closed.
plan: the agent cannot finish because something outside its own files is missing (a tool feature, a view field,
code), and another agent can build it within this epic's goal. Plan exactly that one task; the blocked task then
waits for it and starts again. Never widen the epic's goal, and never plan work that belongs to another epic on the
board. Parts the blocked agent can still do itself stay with it. The new task follows these plan rules:
""" + PLAN_FORMAT[PLAN_FORMAT.index("Rules:"):] + """
ask: only the designer can decide it (taste, scope, a gap in the GDD), or the missing piece belongs to another epic
on the board."""

WORK_FORMAT = """Reply in exactly this format, nothing before it:
=== FILE: <path from your task's Files> ===
<the complete new content of the file>
=== END FILE ===
(one block per file; every file in full, never a diff)
To delete a file from your task's Files (for example the old path of a moved file), write instead:
=== DELETE: <path> ===
SUMMARY: <what you changed and why, two or three sentences, for the reviewer>

If you cannot do the task without a decision from the designer, write no files and reply only:
QUESTION: <the question, and why the GDD does not answer it>

You cannot run anything yourself, and you don't need to: after you reply, the runner runs the task's checks on
your files. If they fail, you get their output and can fix your files before the reviewer sees them. Results of
earlier attempts are in the task's thread. Never write that you could not run a check.
All tasks this one depends on are Done; their files are final."""

GOAL_FORMAT = """Reply with one JSON object only, no other text. One of:
{"type": "question", "text": "<what you need the designer to decide, and why>"}
{"type": "note", "text": "<your answer, when nothing needs planning>"}
{"type": "goal", "goal": "<the state the game should reach>", "done_when": "<how anyone can tell it is met>",
 "text": "<a short message asking the designer to confirm both>"}
{"type": "done", "text": "<why the goal is met, by its done-when>"}
{"type": "gaps", "goal": "<the goal>", "done_when": "<its done-when>", "summary": "<one or two sentences>",
 "gaps": [{"key": "g1", "title": "<feature name, for the epic>", "goal": "<what the player gets, one sentence>",
   "epic": <number of an open epic on the board that already is this gap, or null>, "swap_loop": true or false,
   "cut_order": <0 to 4>, "size": "S" or "M" or "L", "needs": ["<keys of gaps that must be done before this one>"]}]}

How goals work:
- A goal is a state the game should reach; it is met when its done-when is true. Before your first gaps reply the
  designer must have confirmed the goal and its done-when in the thread: if not, reply "goal". A request that
  states both clearly counts as confirmed. The standing goal is confirmed already.
- You check the board, not the code: a feature is done when its epic is Done (built, tested and merged). An open
  epic that has tasks is in progress: it is not a gap.
- Gaps are everything still missing for the goal, in GDD order. When the board already has an open epic without
  tasks for a gap, give its number in "epic" instead of making a new one; such an epic that is no longer a gap is
  dropped.
- For each gap judge: swap_loop - is it part of the swap loop (the production plan's "Never cut")? cut_order - its
  number in the production plan's cut order, 0 if it is not there. size - S, M or L for the work. needs - the
  other gaps in your list that must be done before it can start.
- A taste goal (e.g. "funnier") is met only when the designer says so in the thread; a measurable one when the
  board, a check or the simulation shows it.
- When the designer's newest comment says the goal is not done or lists what is missing, those items are gaps (or
  ask a question): a Done epic never overrules the designer, so never reply "done" while such a comment stands
  unanswered.
- When the designer asks to plan a certain epic next, the goal is that epic Done, and that epic is its only gap.
- Do not plan tasks now: the runner ranks the gaps with your priority function and then asks you for the tasks
  of the top one."""

DESIGN_FORMAT = """This attempt is your design note, not the code: before writing code, explain your plan to the
Director the way you would to a project manager, in game terms, not code. Reply in exactly this format, and write
no files:
DESIGN NOTE:
What I will build: <what the player or the other agents can do afterwards, and which GDD rules it covers>
What I will not do: <what is left for other tasks>
Decisions: <choices that change the game, e.g. an edge case the GDD does not mention; code choices are yours to
make by docs/crew/ and are not listed>
Files: <the files you will write; if the task cannot be done without a file the card does not list, name it
and say why in one line, e.g. "also StationView.cs: it creates the shared inventory">
Questions: <numbered, only about the game, or "none">
Keep it short: half a page at most."""

NOTE_REVIEW_FORMAT = """Reply with one JSON object only, no other text:
{"verdict": "approve" or "revise" or "ask", "summary": "<one sentence>",
 "answers": ["<an answer to one of the note's questions that the GDD, docs/crew/ or the task already gives,
   naming where>"],
 "issues": ["<only for revise: what in the note breaks the task, the GDD, docs/crew/ or the scope, and the fix>"],
 "questions": ["<only for ask: a question for the designer that the GDD, docs/crew/ and the task leave open>"],
 "add_files": ["<only with approve: a file the note must also write that the card does not list>"]}
approve: the note builds what the task asks, within the GDD and its scope, and you answered all its questions.
add_files: approve a file the card lacks only when the task cannot be done without it and it stays in the task's
scope; the runner adds it to the card. A file outside the task's scope is a reason to revise, not to add.
revise: the note is wrong or out of scope; the Programmer writes it again with your issues.
ask: a question only the designer can decide is still open. Never send the designer a question the GDD,
docs/crew/ or the task already answers, and never a question about code."""

REVIEW_FORMAT = """Reply with one JSON object only, no other text:
{"verdict": "approve" or "reject", "summary": "<one sentence>",
 "issues": [{"where": "<file and place>", "problem": "<what is wrong, and the GDD rule or criterion it breaks>",
             "fix": "<concrete fix>"}],
 "repeat": true or false, "outside": true or false}
repeat: true when you reject for the same main problem as the last rejection in "Earlier in this task".
outside: true when the main problem can only be fixed in files outside this task's Files (another agent's code, a
tool, a view script): the agent cannot fix it, so the Director plans the missing piece instead of a new attempt.
Reject only for real problems: a broken acceptance criterion, a GDD contradiction, a failed check, wrong format.
Taste alone is not a reason to reject; mention it as an issue on an approval. At most 6 issues.
The check results above come from the runner, which ran the checks on exactly the files shown: take them as given.
Never reject because the agent did not run or report a check itself."""


UNITY_MADE = (".unity", ".prefab", ".asset", ".meta")  # files only Unity writes


class AgentError(RuntimeError):
    pass


def total_tokens(usage):
    return sum(int(usage.get(k) or 0) for k in
               ("input_tokens", "cache_creation_input_tokens", "cache_read_input_tokens", "output_tokens"))


def sum_usage(usages):
    keys = ("input_tokens", "cache_creation_input_tokens", "cache_read_input_tokens", "output_tokens", "turns")
    return {k: sum(int(u.get(k) or 0) for u in usages) for k in keys}


def describe_tokens(usage):
    turns = int(usage.get("turns") or 0)
    return (f"{total_tokens(usage):,} tokens (in {int(usage.get('input_tokens') or 0):,}, "
            f"cache write {int(usage.get('cache_creation_input_tokens') or 0):,}, "
            f"cache read {int(usage.get('cache_read_input_tokens') or 0):,}, "
            f"out {int(usage.get('output_tokens') or 0):,}" + (f", {turns} turns" if turns else "") + ")")


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


def parse_design_note(text):
    m = re.search(r"DESIGN NOTE:\s*(.+)", text, re.S)
    return (m.group(1) if m else text).strip()


def parse_work(text):
    """-> (files {path: content}, summary, question)"""
    q = re.search(r"^QUESTION:\s*(.+)", text, re.M | re.S)
    files = {path.strip(): None for path in re.findall(r"^=== DELETE: (.+?) ===\s*$", text, re.M)}
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
                p.append(f"{k}: reviewer must be one of the agents and not the task's own agent")
            if t.get("agent") == "technical_artist" and any(not f.endswith(".json") for f in t.get("files") or []):
                p.append(f"{k}: the technical_artist does not program: it writes only specs (.json); code such as "
                         "the builder tool is a programmer task")
            if t.get("agent") not in CODE_WRITERS and any(is_code(f) for f in t.get("files") or []):
                p.append(f"{k}: only the programmer or the qa_automation_engineer writes code (C#, or a script in "
                         "agents/checks/), as in a studio; the ux_designer designs and reviews")
            want = expected_reviewer(t)
            if want and t.get("reviewer") != want:
                p.append(f"{k}: reviewer must be {want}, as in a studio: the Lead Programmer reviews code, the UX "
                         "Designer reviews the Technical Artist's work")
            if not t.get("title") or len(t["title"]) > 80:
                p.append(f"{k}: title missing or longer than 80 characters")
            if not t.get("acceptance"):
                p.append(f"{k}: needs acceptance criteria")
            files = t.get("files") or []
            if not files:
                p.append(f"{k}: needs at least one file to write")
            for f in files:
                p += [f"{k}: {x}" for x in file_rule_problems(f)]
                if f.startswith("Assets/") and f not in (repo_files or ()) and \
                        any(x.endswith(".asmdef") for x in repo_files or ()):  # new file: one new folder level at most
                    parent = f.rsplit("/", 1)[0] if "/" in f else ""
                    grand = parent.rsplit("/", 1)[0] if "/" in parent else ""
                    known = {x.rsplit("/", 1)[0] for x in repo_files if "/" in x}
                    while True:  # every folder above a file is a folder too
                        more = {d.rsplit("/", 1)[0] for d in known if "/" in d} - known
                        if not more:
                            break
                        known |= more
                    if parent and parent not in known and grand not in known:
                        p.append(f"{k}: {f} is a new file in a folder that does not exist ({parent}); check the "
                                 "folder against the repo's file list")
            for f in files:  # a spec with its own measure check must run it, so a change cannot shift the look
                m = re.search(r"([^/]+)\.spec\.json$", f)
                script = f"agents/checks/measure_{m.group(1)}.py" if m else None
                if script and script in (repo_files or ()) and \
                        not any(c.split()[1:2] == [script] for c in t.get("checks") or []):
                    p.append(f"{k}: it writes {f}, so it must also run `python {script} {f}`")
            for c in t.get("checks") or []:
                if not CHECK_RE.match(c):
                    p.append(f"{k}: check {c!r} must look like: python agents/checks/<name>.py <args>")
    for k, t in keys.items():
        for d in t.get("depends_on") or []:
            if d not in keys:
                p.append(f"{k}: depends on unknown task {d!r}")
    p += file_dependency_problems(keys)
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


CODE_WRITERS = ("programmer", "qa_automation_engineer")


def file_rule_problems(f):
    """Files no agent may write, and paths that are not plain paths."""
    if re.search(r"[()\[\]:;,]", f) or f != f.strip():
        return [f"{f!r} is not a plain file path: write only the path, and put any explanation in 'what'"]
    if f.endswith(UNITY_MADE):
        return [f"may not write {f!r}: Unity scene, prefab, asset and .meta files come from Unity (the editor, an "
                "editor script or MCP), never from an agent writing them by hand"]
    if f.startswith(("/", "agents/board/", ".github/")) or ".." in f or "\\" in f or \
            (f.startswith("docs/") and not f.startswith("docs/crew/")):
        return [f"may not write {f!r}"]
    return []


def added_file_problems(agent, reviewer, path, other_open_files):
    """Why a file from an approved design note may not join the task's card (empty = it may)."""
    p = file_rule_problems(path)
    if is_code(path) and agent not in CODE_WRITERS:
        p.append(f"{path}: only the programmer or the qa_automation_engineer writes code")
    if is_code(path) and reviewer != "lead_programmer":
        p.append(f"{path}: code needs the Lead Programmer as reviewer")
    if path in other_open_files:
        p.append(f"{path}: another open task ({other_open_files[path]}) writes it")
    return p


def is_code(path):
    return path.endswith(".cs") or (path.startswith("agents/checks/") and path.endswith(".py"))


def expected_reviewer(task):
    """Fixed reviewers that copy a studio; None when the Director chooses."""
    if task.get("agent") == "technical_artist":
        return "ux_designer"
    if any(is_code(f) for f in task.get("files") or []):
        return "lead_programmer"
    return None


def validate_gaps(reply, open_epics):
    """Rules a gaps reply must follow. open_epics: numbers of open epics on the board without tasks."""
    p, gaps = [], reply.get("gaps")
    if not reply.get("goal") or not reply.get("done_when"):
        p.append("a gaps reply needs the goal and its done_when")
    if not isinstance(gaps, list) or not gaps:
        return p + ["a gaps reply needs at least one gap; if the goal is met, reply done"]
    keys = [g.get("key") for g in gaps]
    if len(set(keys)) != len(keys) or not all(keys):
        p.append("every gap needs its own key")
    for g in gaps:
        k = g.get("key")
        if not g.get("title") or len(g["title"]) > 80:
            p.append(f"{k}: title missing or longer than 80 characters")
        if g.get("epic") is not None and g["epic"] not in open_epics:
            p.append(f"{k}: epic {g['epic']} is not an open epic without tasks")
        if not isinstance(g.get("swap_loop"), bool):
            p.append(f"{k}: swap_loop must be true or false")
        if g.get("cut_order") not in (0, 1, 2, 3, 4):
            p.append(f"{k}: cut_order must be 0 to 4")
        if g.get("size") not in ("S", "M", "L"):
            p.append(f"{k}: size must be S, M or L")
        for n in g.get("needs") or []:
            if n not in keys or n == k:
                p.append(f"{k}: needs unknown gap {n!r}")
    epics = [g["epic"] for g in gaps if g.get("epic") is not None]
    if len(set(epics)) != len(epics):
        p.append("two gaps name the same epic")
    return p


def touched_paths(task):
    """Files and folders a task's checks and Reads use: the check script and every path argument."""
    paths = list(task.get("reads") or [])
    for c in task.get("checks") or []:
        paths += [a for a in c.split()[1:] if "/" in a or "." in a]
    return paths


def file_dependency_problems(keys):
    """A task that uses a file another task writes must depend on that task (directly or through others)."""
    def deps(k, seen=None):
        seen = set() if seen is None else seen
        for d in keys.get(k, {}).get("depends_on") or []:
            if d in keys and d not in seen:
                seen.add(d)
                deps(d, seen)
        return seen
    problems = []
    for k, t in keys.items():
        for c in t.get("checks") or []:
            if not [a for a in c.split()[2:] if "/" in a or "." in a]:
                problems.append(f"{k}: the check '{c}' names no files. Give every check the files it checks as "
                                f"arguments; a check script checks only the files it is given")
        mine, before, reads = set(t.get("files") or []), deps(k), set(t.get("reads") or [])
        for path in touched_paths(t):
            folder = path.rstrip("/") + "/"
            for w, other in keys.items():
                if w == k or w in before:
                    continue
                if path in reads and k in deps(w):
                    continue  # a Read of the file as it is now, before the later task w changes it
                hit = [f for f in other.get("files") or [] if f not in mine and (f == path or f.startswith(folder))]
                if hit:
                    problems.append(f"{k}: its checks or Reads use {', '.join(hit)}, written by {w}, but {k} does not "
                                    f"depend on {w}. Add {w} to its depends_on, or narrow the check to {k}'s own files")
    return problems


class ClaudeAgents:
    def __init__(self, settings, gdd_text):
        self.s = settings
        self.gdd = gdd_text
        self.production_plan = ""
        self.project_info = ""  # Unity and package versions, set by the runner each pass
        self.crew_docs = ""  # docs/crew/*.md, set by the runner each pass
        self.check_usage = ""  # how each check script in agents/checks/ is called, set by the runner each pass

    def _call(self, agent, task_kind, prompt, attempt=1, images=None):
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
            tools = ["--tools", ""]  # no tools
            if images:  # screenshots go into the first message, so the review takes one turn
                try:
                    return (*self._call_with_images(base, prompt, images, tmp, agent), model)
                except AgentError as e:
                    print(f"  ({AGENTS[agent]}: images in the message failed, opening them with Read instead: {e})")
            if images:  # fallback: screenshots copied next to the call, opened with the Read tool only
                names = []
                for i, path in enumerate(images, 1):
                    name = f"screenshot{i}{pathlib.Path(path).suffix or '.png'}"
                    shutil.copyfile(path, pathlib.Path(tmp) / name)
                    names.append(str(pathlib.Path(tmp) / name))
                tools = ["--tools", "Read", "--allowedTools", "Read"]
                prompt += ("\n\n---\n\nScreenshots of the result: " + ", ".join(names) +
                           ". Open each with the Read tool and judge what you see.")
            res = None
            for extra in (tools, []):  # retry without the flags if this CLI rejects them
                res = subprocess.run(base + extra, input=prompt, capture_output=True, text=True, encoding="utf-8",
                                     cwd=tmp, timeout=900, env={**os.environ})
                if res.returncode == 0:
                    break
        if res.returncode != 0:
            raise AgentError(f"{AGENTS[agent]} call failed: {(res.stderr or res.stdout).strip()[:500]}")
        data = json.loads(res.stdout)
        if data.get("is_error"):
            raise AgentError(f"{AGENTS[agent]}: {data.get('result', '')[:500]}")
        usage = dict(data.get("usage") or {})
        usage["turns"] = int(data.get("num_turns") or 0)  # a screenshot review needs extra turns to open the image
        return data.get("result", ""), usage, model

    def _call_with_images(self, base, prompt, images, tmp, agent):
        """One user message with the text and every screenshot (stream-json input), no tools: one turn."""
        types = {".png": "image/png", ".jpg": "image/jpeg", ".jpeg": "image/jpeg"}
        content = [{"type": "text", "text": prompt + "\n\n---\n\nThe screenshots of the result follow. Judge what "
                                                  "you see in them."}]
        for k, path in enumerate(images, 1):  # each image named, so the reviewer can tell the screenshots apart
            content.append({"type": "text", "text": f"Screenshot {k} of {len(images)}: {pathlib.Path(path).name}"})
            data = base64.b64encode(pathlib.Path(path).read_bytes()).decode("ascii")
            content.append({"type": "image", "source": {"type": "base64",
                            "media_type": types.get(pathlib.Path(path).suffix.lower(), "image/png"), "data": data}})
        line = json.dumps({"type": "user", "message": {"role": "user", "content": content}}) + "\n"
        cmd = [a for a in base if a not in ("--output-format", "json")]
        cmd += ["--input-format", "stream-json", "--output-format", "stream-json", "--verbose", "--tools", ""]
        res = subprocess.run(cmd, input=line, capture_output=True, text=True, encoding="utf-8", cwd=tmp, timeout=900,
                             env={**os.environ})
        data = None
        for out in (res.stdout or "").splitlines():
            try:
                msg = json.loads(out)
            except ValueError:
                continue
            if msg.get("type") == "result":
                data = msg
        if res.returncode != 0 or not data:
            raise AgentError(f"call failed: {(res.stderr or res.stdout).strip()[-500:]}")
        if data.get("is_error"):
            raise AgentError(f"{AGENTS[agent]}: {str(data.get('result', ''))[:500]}")
        usage = dict(data.get("usage") or {})
        usage["turns"] = int(data.get("num_turns") or 0)
        return data.get("result", ""), usage

    # ------------------------------------------------------------ the three kinds of work

    def goal(self, request_thread, board_summary, problems=None):
        """The goal step: confirm the goal, find its gaps, or say it is met."""
        prompt = (f"GDD:\n\n{gdd_sections(self.gdd, ['*'])}\n\n---\n\nProduction plan:\n\n{self.production_plan}"
                  f"\n\n---\n\nThe goal's request thread:\n\n{request_thread}\n\n---\n\n"
                  f"The board now:\n{board_summary or 'empty'}")
        if problems:
            prompt += "\n\nYour last reply broke these rules; send a corrected one:\n" + "\n".join(f"- {x}" for x in problems)
        text, usage, model = self._call("director", "Task: work toward the goal.\n\n" + GOAL_FORMAT, prompt)
        return extract_json(text), usage, model

    def plan(self, request_thread, board_summary, repo_files, problems=None, epic=None):
        """The tasks of one epic: the gap the priority function put first."""
        prompt = (f"GDD:\n\n{gdd_sections(self.gdd, ['*'])}\n\n---\n\nThe designer's request thread:\n\n{request_thread}"
                  f"\n\n---\n\nThe board now:\n{board_summary or 'empty'}\n\nFiles in the repo:\n{repo_files}")
        if self.crew_docs:
            prompt += ("\n\n---\n\nThe crew's documents (docs/crew/), approved by the designer. Plan by them: "
                       f"paths, folders and assemblies come from here, not from guesses.\n\n{self.crew_docs}")
        if self.check_usage:
            prompt += ("\n\n---\n\nThe check scripts and how to call them. Write every check command in exactly "
                       f"this form, with real arguments:\n\n{self.check_usage}")
        if epic:
            prompt += (f"\n\n---\n\nPlan the tasks of this one epic only: #{epic['number']} {epic['title']}: "
                       f"{epic['goal']}\nReply with a plan whose epics list has exactly this one epic, with this title.")
        if problems:
            prompt += "\n\nYour last plan broke these rules; send a corrected one:\n" + "\n".join(f"- {x}" for x in problems)
        text, usage, model = self._call("director", "Task: plan the tasks.\n\n" + PLAN_FORMAT, prompt)
        return extract_json(text), usage, model

    def triage(self, card_md, question, thread, board_summary, repo_files, problems=None):
        """The Director reads a task's question first: answers it, plans the one missing task, or asks the designer."""
        prompt = (f"GDD:\n\n{gdd_sections(self.gdd, ['*'])}\n\n---\n\nThe blocked task's card:\n\n{card_md}\n\n---\n\n"
                  f"Its agent's question:\n\n{question}\n\n---\n\nEarlier in this task:\n\n{thread or 'nothing yet'}"
                  f"\n\n---\n\nThe board now:\n{board_summary or 'empty'}\n\nFiles in the repo:\n{repo_files}")
        if self.crew_docs:
            prompt += f"\n\n---\n\nThe crew's documents (docs/crew/), approved by the designer:\n\n{self.crew_docs}"
        if self.check_usage:
            prompt += f"\n\n---\n\nThe check scripts and how to call them:\n\n{self.check_usage}"
        if problems:
            prompt += "\n\nYour last task broke these rules; send a corrected one:\n" + "\n".join(f"- {x}" for x in problems)
        text, usage, model = self._call("director", "Task: decide this question.\n\n" + TRIAGE_FORMAT, prompt)
        return extract_json(text), usage, model

    def work(self, agent, card_md, thread, files, reads, attempt, check_feedback=None, design_note=False,
             images=None, look=False):
        prompt = (f"GDD sections:\n\n{gdd_sections(self.gdd, self.s['reads'][agent])}\n\n---\n\n"
                  f"The project uses: {self.project_info or 'unknown versions'}\n\n---\n\n"
                  f"Your task card:\n\n{card_md}\n\n---\n\n"
                  f"Current content of your files:\n\n{_files_block(files, cut=False)}\n\n---\n\n"
                  f"Files you need to read:\n\n{_files_block(reads)}\n\n---\n\n"
                  f"Earlier in this task (attempts, reviews, the designer's input):\n\n{thread or 'nothing yet'}")
        if check_feedback:
            prompt += ("\n\n---\n\nYour files above are from this attempt, and they failed the checks. Check output:\n"
                       f"{check_feedback}\n\nFix the files and send every file again in full.")
        if look:
            prompt += ("\n\n---\n\nYour files above are from this attempt and passed the checks. The screenshots of "
                       "your build follow. Look at them against the card's acceptance criteria, as the reviewer will. "
                       "If they show what the card asks, reply with exactly LOOKS RIGHT and nothing else. Otherwise "
                       "send the corrected files in full, in the usual format.")
        elif images:
            prompt += ("\n\n---\n\nThe screenshots the reviewer judged in the last attempt follow. Look at them "
                       "before you change anything.")
        kind = DESIGN_FORMAT if design_note else WORK_FORMAT
        text, usage, model = self._call(agent, f"Task: attempt {attempt} of your task.\n\n" + kind, prompt, attempt,
                                        images=images)
        return text, usage, model

    def review(self, reviewer, card_md, summary, files, checks, thread, reads=None, images=None):
        prompt = (f"GDD:\n\n{gdd_sections(self.gdd, self.s['reads'][reviewer])}\n\n---\n\n"
                  f"The project uses: {self.project_info or 'unknown versions'}\n\n---\n\n"
                  f"Task card:\n\n{card_md}\n\n---\n\nThe agent's summary: {summary}\n\n"
                  f"Files it wrote:\n\n{_files_block(files, cut=False)}\n\n---\n\n"
                  f"Files the task read (context, not under review):\n\n{_files_block(reads)}\n\n---\n\n"
                  f"Check results:\n{checks}\n\n---\n\nEarlier in this task:\n\n{thread or 'nothing yet'}")
        if reviewer == "lead_programmer" and self.crew_docs:
            prompt += f"\n\n---\n\nThe crew's documents (docs/crew/), approved by the designer:\n\n{self.crew_docs}"
        text, usage, model = self._call(reviewer, "Task: review this task's result.\n\n" + REVIEW_FORMAT, prompt,
                                        images=images)
        return extract_json(text), usage, model

    def review_note(self, card_md, note, thread):
        """The Director reviews the Programmer's design note, like a producer, before any code is written."""
        prompt = (f"GDD:\n\n{gdd_sections(self.gdd, ['*'])}\n\n---\n\nTask card:\n\n{card_md}\n\n---\n\n"
                  f"The Programmer's design note:\n\n{note}\n\n---\n\nEarlier in this task:\n\n{thread or 'nothing yet'}")
        if self.crew_docs:
            prompt += f"\n\n---\n\nThe crew's documents (docs/crew/), approved by the designer:\n\n{self.crew_docs}"
        text, usage, model = self._call("director", "Task: review this design note.\n\n" + NOTE_REVIEW_FORMAT, prompt)
        return extract_json(text), usage, model


def _files_block(files, cut=True):
    """Files for a prompt. Files the agent only reads may be cut; a file it rewrites (always in full) or a reviewer
    judges is never cut, since half a file cannot be rewritten or judged."""
    if not files:
        return "none"
    out = []
    for path, content in files.items():
        if content is None:
            out.append(f"--- {path} (does not exist, or deleted)")
        else:
            c = content if not cut or len(content) <= MAX_FILE_CHARS else content[:MAX_FILE_CHARS] + "\n... (cut)"
            out.append(f"--- {path}\n{c}")
    return "\n\n".join(out)
