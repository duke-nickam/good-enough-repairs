"""Scripted stand-ins for a --mock run: agents that need no Claude calls, and a designer who approves,
answers an escalated task and merges the pull request. The scenario shows every path of the flow:
a failed check, a rejection, a task reaching Needs designer, the attempts reset, and the epic's pull request."""

USAGE = {"input_tokens": 1200, "cache_read_input_tokens": 14000, "cache_creation_input_tokens": 0, "output_tokens": 300}

PLAN = {
    "type": "plan",
    "summary": "One epic: the UX Designer writes a greeting line, the Programmer loads it in Unity.",
    "epics": [{
        "title": "Greeting line (demo)",
        "goal": "The mechanic greets the player at the start of a day.",
        "tasks": [
            {"key": "t1", "title": "Write the greeting line", "agent": "ux_designer", "reviewer": "director",
             "what": "Write one greeting line in the mechanic's voice.",
             "acceptance": ["Valid JSON with a 'line' field", "Proud and cheeky, no economy numbers"],
             "files": ["Assets/Data/Content/greeting.json"], "reads": [], "checks": [], "depends_on": []},
            {"key": "t2", "title": "Load the greeting line", "agent": "programmer", "reviewer": "qa_playtester",
             "what": "Load greeting.json (the file from t1) at start with JsonUtility.",
             "acceptance": ["Reads the file through a TextAsset", "No hard-coded text"],
             "files": ["Assets/Scripts/Data/GreetingLoader.cs"], "reads": [], "checks": [], "depends_on": ["t1"]},
        ],
    }],
}


class MockAgents:
    def plan(self, thread, board, files, problems=None):
        return PLAN, USAGE, "mock"

    def work(self, agent, card_md, thread, files, reads, attempt, check_feedback=None):
        if agent == "ux_designer":
            dash = " \u2014 " if not check_feedback else ". "  # trips the long-dash check, fixed on the self-check
            text = ('=== FILE: Assets/Data/Content/greeting.json ===\n{"line": "Morning' + dash +
                    'Three bays, one genius. Let\'s get greasy."}\n=== END FILE ===\nSUMMARY: Wrote the greeting.')
        else:
            text = ("=== FILE: Assets/Scripts/Data/GreetingLoader.cs ===\nusing UnityEngine;\n"
                    "public class GreetingLoader : MonoBehaviour { }\n=== END FILE ===\nSUMMARY: Added the loader.")
        return text, USAGE, "mock"

    def review(self, reviewer, card_md, summary, files, checks, thread):
        if reviewer == "qa_playtester" and "attempts reset" not in thread:
            return {"verdict": "reject", "summary": "The loader never reads the file.",
                    "issues": [{"where": "GreetingLoader.cs", "problem": "No TextAsset field", "fix": "Add one"}]}, \
                USAGE, "mock"
        return {"verdict": "approve", "summary": "Meets the acceptance criteria.", "issues": []}, USAGE, "mock"


def designer_step(board, step, log):
    """What the scripted designer does after each runner pass. Returns False when the scenario is over."""
    items = board.items()
    if step == 0:
        for i in items:
            if i.kind == "task" and i.status == "Backlog":
                board.set_status(i.number, "Todo")
        log("Designer (mock)", "approved the tasks: Backlog -> Todo")
    elif step == 1:
        for i in items:
            if i.status == "Needs designer":
                board.comment(i.number, "Use a TextAsset field and JsonUtility, like EconomyLoader does.")
                board.set_status(i.number, "Todo")
                log("Designer (mock)", f"#{i.number}: gave direction, moved to Todo")
    elif step == 2:
        for i in items:
            if i.kind == "epic" and i.status == "Waiting for review":
                board.close(i.number)
                log("Designer (mock)", f"epic #{i.number}: squash-merged the pull request")
    else:
        return False
    return True
