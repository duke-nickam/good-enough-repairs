"""Good Enough Repairs - agent board runner.

The Director plans work from the designer's requests on a GitHub board; this runner wakes the agent each task
names, runs the checks, has the reviewer judge the result, and moves the cards. All state lives on the board.

Usage (from the project root):
  python agents/crew.py setup     one time: labels, board, columns, fields, repo setting
  python agents/crew.py check     is everything ready? (gh login, board, Claude CLI, clean repo)
  python agents/crew.py           one pass: works until nothing is ready, then stops
  python agents/crew.py --watch   keeps running, checks the board every few minutes (Ctrl+C stops)
  python agents/crew.py --mock    a scripted demo on a local stand-in board: no GitHub, no Claude
  python agents/crew.py todo 65 66   the designer's move: tasks from Backlog or Needs designer to Todo
  python agents/crew.py done 65      the designer's move: accept a task waiting in Needs designer
  python agents/crew.py triage 115  send a task in Needs designer to the Director: its last review is
                                    blocked outside its files, so the Director plans the missing piece
"""
import argparse
import json
import pathlib
import shutil
import sys
import tempfile
import time

HERE = pathlib.Path(__file__).resolve().parent
ROOT = HERE.parent
sys.path.insert(0, str(HERE))

from board.agents import ClaudeAgents  # noqa: E402
from board.model import STATUSES  # noqa: E402
from board.runner import Runner, alert, log  # noqa: E402
from board.workspace import GitWorkspace, MockWorkspace, WorkspaceError  # noqa: E402

if hasattr(sys.stdout, "reconfigure"):
    sys.stdout.reconfigure(encoding="utf-8", errors="replace")

SETTINGS = HERE / "board" / "settings.json"


def load_settings():
    return json.loads(SETTINGS.read_text(encoding="utf-8"))


def check_usage(root):
    """How each check script is called: the usage part of its docstring, for the Director's plans."""
    import ast
    out = []
    for p in sorted((root / "agents" / "checks").glob("*.py")):
        doc = ast.get_docstring(ast.parse(p.read_text(encoding="utf-8"))) or ""
        lines = doc.splitlines()
        start = next((i for i, l in enumerate(lines) if l.strip().lower().startswith("usage")), None)
        if start is not None:
            block = [lines[start]]
            for l in lines[start + 1:start + 12]:  # the indented lines that explain the arguments
                if l.strip() and not l.startswith(" "):
                    break
                block.append(l)
            out.append(f"--- agents/checks/{p.name}\n" + "\n".join(block).rstrip())
    return "\n\n".join(out)


def project_info(root):
    """The project's Unity and package versions, read fresh each pass, so agents write for the versions in use."""
    parts = []
    pv = root / "ProjectSettings" / "ProjectVersion.txt"
    if pv.is_file():
        for line in pv.read_text(encoding="utf-8").splitlines():
            if line.startswith("m_EditorVersion:"):
                parts.append("Unity " + line.split(":", 1)[1].strip())
    manifest = root / "Packages" / "manifest.json"
    if manifest.is_file():
        deps = json.loads(manifest.read_text(encoding="utf-8")).get("dependencies", {})
        parts += [f"{k} {v}" for k, v in sorted(deps.items()) if not k.startswith("com.unity.modules.")]
    return ", ".join(parts)


def print_board(board):
    rows = sorted(board.items(), key=lambda i: i.number)
    print(f"\n{'#':>4}  {'Kind':<5} {'Status':<19} {'Attempt':<8} {'Priority':>8} {'Used tokens':>12}  Title")
    for i in rows:
        att = f"{i.attempt}/{board.max_attempts}" if i.kind == "task" else ""
        prio = "" if i.priority is None else str(i.priority)
        state = " (closed)" if i.state == "CLOSED" else ""
        print(f"{i.number:>4}  {i.kind:<5} {i.status:<19} {att:<8} {prio:>8} {i.tokens:>12,}  {i.title}{state}")
    print()


def run_mock(settings):
    from board.local import LocalBoard
    from board.mock import MOCK_BUILD, MockAgents, designer_step
    with tempfile.TemporaryDirectory() as tmp:
        board = LocalBoard(pathlib.Path(tmp) / "board.json", settings["max_attempts"])
        ws = MockWorkspace(ROOT, pathlib.Path(tmp) / "repo")
        ws.write("agents/checks/mockbuild.py", MOCK_BUILD)  # the stand-in builder check
        runner = Runner(board, ws, MockAgents(), {**settings, "director_autoplan": True, "sound": False})
        log("Designer (mock)", "director_autoplan is on: the Director opens the standing goal")
        step = 0
        while True:
            log("Runner", f"--- pass {step + 1} ---")
            runner.run_once()
            print_board(board)
            if not designer_step(board, step, log):
                break
            step += 1
        log("Runner", "mock run finished.")


def check(settings):
    from board.github import GitHubBoard, GhError, gh
    ok = True

    def report(name, fn):
        nonlocal ok
        try:
            print(f"  ok    {name}: {fn() or ''}")
        except Exception as e:  # noqa: BLE001 - report every kind of failure the same way
            ok = False
            print(f"  FAIL  {name}: {e}")
    print("Checking:")
    def need(value, problem):
        if not value:
            raise RuntimeError(problem)
        return value
    report("GitHub login", lambda: (gh("auth", "status") or "logged in").splitlines()[0])
    board = GitHubBoard(settings)

    def fields():
        f = board.fields()
        names = [o["name"] for o in f.get("Status", {}).get("options", [])]
        missing = [x for x in ("Attempt", "Used tokens", "Priority") if x not in f]
        if names != STATUSES or missing:
            raise GhError(f"columns {names}, missing fields {missing}: run setup")
        return "columns and fields in place"
    report("Board", lambda: f"'{board.title}' #{board.number}")
    report("Board fields", fields)
    report("Claude Code CLI", lambda: need(shutil.which("claude"), "not found: install it and log in"))
    ws = GitWorkspace(ROOT, settings["repo"])

    def repo():
        ws.start()
        ws.finish()
        return f"clean, base branch {ws.base}"
    report("Repo", repo)
    report("GDD", lambda: need((ROOT / settings["gdd"]).is_file() and settings["gdd"], f"not found: {settings['gdd']}"))
    report("Production plan", lambda: need((ROOT / settings["production_plan"]).is_file() and settings["production_plan"],
                                           f"not found: {settings['production_plan']}"))
    unity = settings.get("unity_path") or ""
    if not unity:
        print("  note  Unity: unity_path is empty in settings.json; the EditMode test check needs Unity.exe's path")
    else:
        report("Unity", lambda: need(pathlib.Path(unity).is_file() and unity, f"not found: {unity}"))
    print("Ready." if ok else "Fix the FAIL lines, then run check again.")
    return ok


MOVES = {"todo": ("Todo", ("Backlog", "Needs designer")), "done": ("Done", ("Needs designer",))}


def send_to_director(board, numbers):
    """The designer hands a task in Needs designer to the Director: its last review is blocked outside the task's
    files. The next run lets the Director plan the missing piece (no AI here, only a marker)."""
    from board.model import crew_comment
    items = {i.number: i for i in board.items()}
    ok = True
    for n in numbers or []:
        i = items.get(n)
        if not i or i.kind != "task" or i.status != "Needs designer":
            print(f"  #{n}: not a task in Needs designer, skipped")
            ok = False
            continue
        board.comment(n, crew_comment("Runner", "The designer sent this task to the Director: its last review is "
                                                "blocked by work outside the task's files.", "needs_designer",
                                      outside=True))
        print(f"  #{n} {i.title}: sent to the Director (next run)")
    if not numbers:
        print("Name the tasks, e.g.: python agents/crew.py triage 115")
    return ok and bool(numbers)


def move(board, command, numbers):
    """The designer's card moves from the command line: no AI, only the GitHub CLI."""
    to, allowed = MOVES[command]
    if not numbers:
        print(f"Name the tasks, e.g.: python agents/crew.py {command} 65 66")
        return False
    items = {i.number: i for i in board.items()}
    ok = True
    for n in numbers:
        i = items.get(n)
        if not i or i.kind != "task" or i.state != "OPEN":
            print(f"  #{n}: not an open task on the board, skipped")
            ok = False
        elif i.status not in allowed:
            print(f"  #{n}: is in {i.status}; '{command}' moves only from {' or '.join(allowed)}, skipped")
            ok = False
        else:
            board.set_status(n, to)
            print(f"  #{n} {i.title}: {i.status} -> {to}")
    return ok


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("command", nargs="?", choices=["run", "setup", "check", "todo", "done", "triage"], default="run")
    ap.add_argument("numbers", nargs="*", type=int, help="task numbers, for todo, done and triage")
    ap.add_argument("--watch", action="store_true", help="keep running; check the board every few minutes")
    ap.add_argument("--mock", action="store_true", help="scripted demo, no GitHub and no Claude")
    args = ap.parse_args()
    settings = load_settings()

    if args.mock:
        return run_mock(settings)
    from board.github import GitHubBoard, GhError
    board = GitHubBoard(settings)
    if args.command == "setup":
        board.setup()
        print("\nOne step on github.com: open the board, switch the view's layout to Board, grouped by Status.")
        return
    if args.command == "check":
        sys.exit(0 if check(settings) else 1)
    if args.command in ("todo", "done"):
        sys.exit(0 if move(board, args.command, args.numbers) else 1)
    if args.command == "triage":
        sys.exit(0 if send_to_director(board, args.numbers) else 1)

    ws = GitWorkspace(ROOT, settings["repo"])
    agents = ClaudeAgents(settings, "")
    runner = Runner(board, ws, agents, settings)
    try:
        while True:
            ws.start()
            agents.gdd = (ROOT / settings["gdd"]).read_text(encoding="utf-8")  # always the current GDD
            agents.production_plan = (ROOT / settings["production_plan"]).read_text(encoding="utf-8")
            agents.project_info = project_info(ROOT)
            agents.crew_docs = "\n\n".join(f"--- {p.relative_to(ROOT).as_posix()}\n{p.read_text(encoding='utf-8')}"
                                            for p in sorted((ROOT / "docs" / "crew").glob("*.md")))
            agents.check_usage = check_usage(ROOT)
            try:
                runner.run_once()
            finally:
                ws.finish()
            if not args.watch:
                log("Runner", "nothing ready to work on. Stopped.")
                alert(settings, "stopped")
                break
            log("Runner", f"waiting {settings['poll_minutes']} min (Ctrl+C stops)")
            time.sleep(settings["poll_minutes"] * 60)
    except KeyboardInterrupt:
        log("Runner", "stopped by the designer. Start it again to continue.")
    except (GhError, WorkspaceError) as e:
        sys.exit(f"Stopped: {e}")


if __name__ == "__main__":
    main()
