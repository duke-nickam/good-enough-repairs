"""Scripted stand-ins for a --mock run: agents that need no Claude calls, and a designer who approves tasks,
accepts a crew document, approves a design note and merges the pull request. The scenario shows the goal loop:
the standing goal's gaps ranked by priority_score(), the top epic planned, an unplanned epic sent back to Backlog,
a failed self-check, a rejection, the design note, the docs/crew/ approval, and the next gap after the merge."""
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
        {"key": "t2", "title": "Write ship and part rules", "agent": "programmer", "reviewer": "qa_playtester",
         "what": "Ships with five part slots and the repaired check, following t1's design.",
         "acceptance": ["A ship is repaired when every part reaches its type's level"],
         "files": ["Assets/Scripts/Rules/Ship.cs"], "reads": ["docs/crew/technical-design.md"], "checks": [],
         "depends_on": ["t1"]},
        {"key": "t3", "title": "Test ship and part rules", "agent": "qa_playtester", "reviewer": "programmer",
         "what": "EditMode tests for t2's rules.", "acceptance": ["Tests the 65% engine on both ship types"],
         "files": ["Assets/Tests/EditMode/ShipTests.cs"], "reads": ["Assets/Scripts/Rules/Ship.cs"], "checks": [],
         "depends_on": ["t2"]},
    ],
    "Inventory": [
        {"key": "t1", "title": "Write the inventory rules", "agent": "programmer", "reviewer": "qa_playtester",
         "what": "A shared 10-slot inventory.", "acceptance": ["A full inventory refuses a part"],
         "files": ["Assets/Scripts/Rules/Inventory.cs"], "reads": [], "checks": [], "depends_on": []},
    ],
}


class MockAgents:
    def __init__(self):
        self.goal_calls = 0

    def goal(self, thread, board, problems=None):
        self.goal_calls += 1
        gaps = [dict(g) for g in GAPS]
        if self.goal_calls > 1:  # the first epic is Done: the rest are the gaps, on the epics the board already has
            gaps = gaps[1:]
            for g in gaps:
                m = re.search(rf"#(\d+) \[epic\] {g['title']} - open, no tasks yet", board)
                g["epic"] = int(m.group(1)) if m else None
            gaps[0]["needs"] = []
        return {"type": "gaps", "goal": "Every GDD feature in the current scope is done.",
                "done_when": "Every such feature has a Done epic.", "summary": "Mock gaps.", "gaps": gaps}, USAGE, "mock"

    def plan(self, thread, board, files, problems=None, epic=None):
        return {"type": "plan", "summary": f"Tasks for {epic['title']}.",
                "epics": [{"title": epic["title"], "goal": epic["goal"], "tasks": TASKS[epic["title"]]}]}, USAGE, "mock"

    def work(self, agent, card_md, thread, files, reads, attempt, check_feedback=None, design_note=False):
        if design_note:
            return ("DESIGN NOTE:\nShip and Part in plain C#, no UnityEngine; the levels come from balance.json.\n"
                    "1. Should a ship know its bay, or the bay its ship?"), USAGE, "mock"
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

    def review(self, reviewer, card_md, summary, files, checks, thread):
        if reviewer == "qa_playtester" and "Rejected on attempt" not in thread:
            return {"verdict": "reject", "summary": "The repaired check is missing.",
                    "issues": [{"where": "Ship.cs", "problem": "No IsRepaired", "fix": "Add it"}]}, USAGE, "mock"
        return {"verdict": "approve", "summary": "Meets the acceptance criteria.", "issues": []}, USAGE, "mock"


def designer_step(board, step, log):
    """What the scripted designer does after each runner pass. Returns False when the scenario is over."""
    items = board.items()
    if step == 0:
        for i in items:
            if i.kind == "task" and i.status == "Backlog":
                board.set_status(i.number, "Todo")
        log("Designer (mock)", "approved the tasks: Backlog -> Todo")
        tut = next(i for i in items if i.title == "Guided tutorial")
        board.set_status(tut.number, "Todo")
        log("Designer (mock)", f"dragged unplanned epic #{tut.number} to Todo (the runner sends it back)")
    elif step in (1, 2):
        for i in items:
            if i.status != "Needs designer":
                continue
            if step == 1:
                board.set_status(i.number, "Done")
                log("Designer (mock)", f"#{i.number}: accepted the crew document, moved to Done")
            else:
                board.comment(i.number, "Approved. The bay knows its ship.")
                board.set_status(i.number, "Todo")
                log("Designer (mock)", f"#{i.number}: answered the design note, moved to Todo")
    elif step == 3:
        for i in items:
            if i.kind == "epic" and i.status == "Waiting for review":
                board.close(i.number)
                log("Designer (mock)", f"epic #{i.number}: squash-merged the pull request")
    else:
        return False
    return True
