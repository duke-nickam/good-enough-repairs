"""Good Enough Repairs - agent board runner.

The Director plans work from the designer's requests on a GitHub board; this runner wakes the agent each task
names, runs the checks, has the reviewer judge the result, and moves the cards. All state lives on the board.

Usage (from the project root):
  python agents/crew.py setup     one time: labels, board, columns, fields, repo setting
  python agents/crew.py check     is everything ready? (gh login, board, Claude CLI, clean repo)
  python agents/crew.py           one pass: works until nothing is ready, then stops
  python agents/crew.py --watch   keeps running, checks the board every few minutes (Ctrl+C stops)
  python agents/crew.py --mock    a scripted demo on a local stand-in board: no GitHub, no Claude
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


def print_board(board):
    rows = sorted(board.items(), key=lambda i: i.number)
    print(f"\n{'#':>4}  {'Kind':<5} {'Status':<19} {'Attempt':<8} {'Used tokens':>12}  Title")
    for i in rows:
        att = f"{i.attempt}/{board.max_attempts}" if i.kind == "task" else ""
        print(f"{i.number:>4}  {i.kind:<5} {i.status:<19} {att:<8} {i.tokens:>12,}  {i.title}")
    print()


def run_mock(settings):
    from board.local import LocalBoard
    from board.mock import MockAgents, designer_step
    with tempfile.TemporaryDirectory() as tmp:
        board = LocalBoard(pathlib.Path(tmp) / "board.json", settings["max_attempts"])
        ws = MockWorkspace(ROOT, pathlib.Path(tmp) / "repo")
        runner = Runner(board, ws, MockAgents(), settings)
        n = board.create_issue("Demo: a greeting line for the day start",
                               "The mechanic should greet the player when a day starts.", ["request"])
        log("Designer (mock)", f"opened request #{n}")
        step = 0
        while True:
            log("Runner", f"--- pass {step + 1} ---")
            runner.run_once()
            print_board(board)
            if not designer_step(board, step, log):
                break
            step += 1
        req = board.issue(n)
        log("Runner", f"request #{n} is {req.state.lower()}. Mock run finished.")


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
        missing = [x for x in ("Attempt", "Used tokens") if x not in f]
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
    print("Ready." if ok else "Fix the FAIL lines, then run check again.")
    return ok


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("command", nargs="?", choices=["run", "setup", "check"], default="run")
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

    ws = GitWorkspace(ROOT, settings["repo"])
    agents = ClaudeAgents(settings, "")
    runner = Runner(board, ws, agents, settings)
    try:
        while True:
            ws.start()
            agents.gdd = (ROOT / settings["gdd"]).read_text(encoding="utf-8")  # always the current GDD
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
