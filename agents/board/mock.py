"""Scripted stand-ins for a --mock run: agents that need no Claude calls, and a designer who approves tasks,
accepts a crew document, answers the Director's question and merges the pull requests. The scenario shows the goal
loop: the standing goal's gaps ranked by priority_score(), the top epic planned, an unplanned epic sent back to
Backlog, a failed self-check, the docs/crew/ approval, the Director's design note review (revise, approve, and a
question for the designer), the Lead Programmer's rejection, a builder check that makes a prefab and a screenshot
for the UX Designer's review, and the next gap after the merge."""
import re

USAGE = {"input_tokens": 1200, "cache_read_input_tokens": 14000, "cache_creation_input_tokens": 0, "output_tokens": 300}

GAPS = [
    {"key": "g1", "title": "Ships and parts", "goal": "Ships dock with parts the player can compare to the required level.",
     "swap_loop": True, "cut_order": 0, "size": "M", "needs": []},
    {"key": "g2", "title": "Inventory", "goal": "Removed parts wait in a 10-slot inventory for a later ship.",
     "swap_loop": True, "cut_order": 0, "size": "S", "needs": ["g1"]},
    {"key": "g3", "title": "Guided tutorial", "goal": "A first-time player learns swapping on day 0.",
     "swap_loop": False, "cut_order": 1, "size": "L", "needs": ["g2"]},
]

TASKS = {
    "Ships and parts": [
        {"key": "t1", "title": "Write the technical design", "agent": "programmer", "reviewer": "director",
         "what": "Describe how the game runs as code alone: assemblies, folders, events.",
         "acceptance": ["Covers the rules assembly and the test assembly"],
         "files": ["docs/crew/technical-design.md"], "reads": [], "checks": [], "depends_on": []},
        {"key": "t2", "title": "Write ship and part rules", "agent": "programmer", "reviewer": "lead_programmer",
         "what": "Ships with five part slots and the repaired check, following t1's design, with unit tests.",
         "acceptance": ["A ship is repaired when every part reaches its type's level"],
         "files": ["Assets/Scripts/Rules/Ship.cs", "Assets/Tests/EditMode/ShipTests.cs"],
         "reads": ["docs/crew/technical-design.md"], "checks": [], "depends_on": ["t1"]},
        {"key": "t3", "title": "Build the ship-in-bay prefab", "agent": "technical_artist", "reviewer": "ux_designer",
         "what": "A builder script that makes the ship-in-bay prefab with placeholder sprites.",
         "acceptance": ["The screenshot shows the ship and its five condition bars"],
         "files": ["Assets/Scripts/Editor/ShipInBayBuilder.cs"], "reads": ["Assets/Scripts/Rules/Ship.cs"],
         "checks": ["python agents/checks/mockbuild.py Assets/Scripts/Editor/ShipInBayBuilder.cs"],
         "depends_on": ["t2"]},
    ],
    "Inventory": [
        {"key": "t1", "title": "Write the inventory rules", "agent": "programmer", "reviewer": "lead_programmer",
         "what": "A shared 10-slot inventory.", "acceptance": ["A full inventory refuses a part"],
         "files": ["Assets/Scripts/Rules/Inventory.cs"], "reads": [], "checks": [], "depends_on": []},
    ],
}

# A stand-in for a builder check: Unity "makes" a prefab and a screenshot (a 1x1 PNG), and says so.
MOCK_BUILD = """import base64, os, sys, tempfile
os.makedirs("Assets/Prefabs", exist_ok=True)
open("Assets/Prefabs/ShipInBay.prefab", "w").write("%YAML 1.1\\n")
png = os.path.join(tempfile.mkdtemp(prefix="ger_shots_"), "ShipInBay.png")
open(png, "wb").write(base64.b64decode("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg=="))
print("MADE: Assets/Prefabs/ShipInBay.prefab")
print("SCREENSHOT: " + png)
print("OK: built " + sys.argv[1])
"""


class MockAgents:
    def __init__(self):
        self.goal_calls = 0

    def goal(self, thread, board, problems=None):
        self.goal_calls += 1
        gaps = [dict(g) for g in GAPS]
        if self.goal_calls > 1:  # the first epic is Done: the rest are the gaps, on the epics the board already has
            gaps = gaps[1:]
            for g in gaps:
                m = re.search(rf"#(\d+) \[epic\] {g['title']}( \(goal: [^)]*\))? - open, no tasks yet", board)
                g["epic"] = int(m.group(1)) if m else None
            gaps[0]["needs"] = []
        return {"type": "gaps", "goal": "Every GDD feature in the current scope is done.",
                "done_when": "Every such feature has a Done epic.", "summary": "Mock gaps.", "gaps": gaps}, USAGE, "mock"

    def plan(self, thread, board, files, problems=None, epic=None):
        return {"type": "plan", "summary": f"Tasks for {epic['title']}.",
                "epics": [{"title": epic["title"], "goal": epic["goal"], "tasks": TASKS[epic["title"]]}]}, USAGE, "mock"

    def work(self, agent, card_md, thread, files, reads, attempt, check_feedback=None, design_note=False):
        if design_note:
            if "inventory" in card_md.lower():
                return ("DESIGN NOTE:\nWhat I will build: a shared inventory the player fills from any ship.\n"
                        "Questions: 1. Can the player throw away a part from a full inventory?"), USAGE, "mock"
            return ("DESIGN NOTE:\nWhat I will build: ships that dock with five parts and know when they are repaired.\n"
                    "Questions: 1. Does the bay know its ship, or the ship its bay?"), USAGE, "mock"
        out = []
        for path in re.findall(r"`([^`]+)`", re.search(r"\*\*Files:\*\*(.*)", card_md).group(1)):
            if path.endswith(".md"):
                dash = " — " if not check_feedback else ": "  # trips the long-dash check, fixed on the self-check
                body = f"# Technical design\n\nThe game runs as code alone{dash}rules in plain C#.\n"
            else:
                name = path.rsplit("/", 1)[-1][:-3]
                body = f"namespace GoodEnoughRepairs\n{{\n    public class {name} {{ }}\n}}\n"
            out.append(f"=== FILE: {path} ===\n{body}=== END FILE ===")
        return "\n".join(out) + "\nSUMMARY: Wrote the files.", USAGE, "mock"

    def review(self, reviewer, card_md, summary, files, checks, thread, reads=None, images=None):
        if reviewer == "lead_programmer" and "Ship.cs" in card_md and "Rejected on attempt" not in thread:
            return {"verdict": "reject", "summary": "The repaired check is missing.",
                    "issues": [{"where": "Ship.cs", "problem": "No IsRepaired", "fix": "Add it"}]}, USAGE, "mock"
        seen = f" Looked at {len(images)} screenshot(s)." if images else ""
        return {"verdict": "approve", "summary": "Meets the acceptance criteria." + seen, "issues": []}, USAGE, "mock"

    def review_note(self, card_md, note, thread):
        if "inventory" in card_md.lower():
            return {"verdict": "ask", "summary": "One rule the GDD leaves open.", "answers": [],
                    "questions": ["Can the player throw away a part from a full inventory?"]}, USAGE, "mock"
        if "Design note: revise" not in thread:
            return {"verdict": "revise", "summary": "The note leaves out the unit tests.",
                    "issues": ["The task asks for unit tests: say what they will show."]}, USAGE, "mock"
        return {"verdict": "approve", "summary": "Builds what the task asks.",
                "answers": ["The bay knows its ship: docs/crew/technical-design.md, Events."]}, USAGE, "mock"


MERGED = []


def designer_step(board, step, log):
    """What the scripted designer does after each runner pass. Returns False when the scenario is over."""
    items = board.items()
    backlog = [i for i in items if i.kind == "task" and i.status == "Backlog"]
    waiting = [i for i in items if i.status == "Needs designer"]
    prs = [i for i in items if i.kind == "epic" and i.status == "Waiting for review"]
    if backlog:
        for i in backlog:
            board.set_status(i.number, "Todo")
        log("Designer (mock)", "approved the tasks: Backlog -> Todo")
        if step == 0:
            tut = next(i for i in items if i.title == "Guided tutorial")
            board.set_status(tut.number, "Todo")
            log("Designer (mock)", f"dragged unplanned epic #{tut.number} to Todo (the runner sends it back)")
    elif waiting:
        for i in waiting:
            if i.title == "Write the technical design":
                board.set_status(i.number, "Done")
                log("Designer (mock)", f"#{i.number}: accepted the crew document, moved to Done")
            else:
                board.comment(i.number, "No: a full inventory refuses the part; the player bins one first.")
                board.set_status(i.number, "Todo")
                log("Designer (mock)", f"#{i.number}: answered the Director's question, moved to Todo")
    elif prs:
        for i in prs:
            board.close(i.number)
            MERGED.append(i.number)
            log("Designer (mock)", f"epic #{i.number}: squash-merged the pull request")
        return len(MERGED) < 2
    else:
        return False
    return step < 12
