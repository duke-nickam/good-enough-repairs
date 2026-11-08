"""Shared names and formats of the agent board: statuses, agents, task cards and comment markers."""
import json
import re
from dataclasses import dataclass, field

STATUSES = ["Backlog", "Todo", "In progress", "Waiting for review", "In review", "Done", "Needs designer"]

AGENTS = {  # key: display name (the GDD's seven agents)
    "director": "Director",
    "programmer": "Programmer",
    "economy_balancer": "Economy Balancer",
    "ux_designer": "UX Designer",
    "artist": "Artist",
    "sound_designer": "Sound Designer",
    "qa_playtester": "QA Playtester",
}
AGENT_KEYS = {v.lower(): k for k, v in AGENTS.items()}

LABELS = {  # name: (color, description)
    "request": ("0E8A16", "The designer's request to the Director"),
    "epic": ("5319E7", "A feature: groups the tasks of the agents involved"),
    "task": ("1D76DB", "One agent's part of an epic"),
}

CHECK_RE = re.compile(r"^python agents/checks/[A-Za-z0-9_\-]+\.py( [A-Za-z0-9_\-./ ]*)?$")
MARKER_RE = re.compile(r"<!-- crew:(\{.*?\}) -->")


def attempt_label(n, max_attempts):
    return f"{n}/{max_attempts}"


def attempt_number(label):
    try:
        return int(str(label).split("/")[0])
    except (ValueError, IndexError):
        return 0


@dataclass
class Card:
    """A task card, kept as visible Markdown in the task issue's body."""
    agent: str
    reviewer: str
    epic: int
    what: str
    acceptance: list
    files: list
    reads: list = field(default_factory=list)
    checks: list = field(default_factory=list)
    depends_on: list = field(default_factory=list)

    def to_markdown(self):
        def refs(nums):
            return ", ".join(f"#{n}" for n in nums) or "none"

        def paths(items):
            return ", ".join(f"`{p}`" for p in items) or "none"

        return "\n".join([
            f"**Agent:** {AGENTS[self.agent]}",
            f"**Reviewer:** {AGENTS[self.reviewer]}",
            f"**Epic:** #{self.epic}",
            f"**Depends on:** {refs(self.depends_on)}",
            f"**Files:** {paths(self.files)}",
            f"**Reads:** {paths(self.reads)}",
            f"**Checks:** {paths(self.checks)}",
            "",
            "### What to do",
            "",
            self.what.strip(),
            "",
            "### Acceptance criteria",
            "",
            *[f"- {a}" for a in self.acceptance],
        ]) + "\n"

    @staticmethod
    def parse(body):
        """Read a card back from an issue body. Returns None when the body is not a task card."""
        def line(name):
            m = re.search(rf"^\*\*{name}:\*\*\s*(.*)$", body or "", re.M)
            return m.group(1).strip() if m else None

        agent, reviewer, epic = line("Agent"), line("Reviewer"), line("Epic")
        if not agent or not reviewer or not epic:
            return None
        section = lambda title: (re.search(rf"### {title}\s*\n(.*?)(?=\n### |\Z)", body, re.S) or [None, ""])[1].strip()
        return Card(
            agent=AGENT_KEYS.get(agent.lower(), agent),
            reviewer=AGENT_KEYS.get(reviewer.lower(), reviewer),
            epic=int(re.sub(r"\D", "", epic) or 0),
            what=section("What to do"),
            acceptance=[a[2:].strip() for a in section("Acceptance criteria").splitlines() if a.startswith("- ")],
            files=re.findall(r"`([^`]+)`", line("Files") or ""),
            reads=re.findall(r"`([^`]+)`", line("Reads") or ""),
            checks=re.findall(r"`([^`]+)`", line("Checks") or ""),
            depends_on=[int(n) for n in re.findall(r"#(\d+)", line("Depends on") or "")],
        )


@dataclass
class Comment:
    body: str
    created: str = ""

    @property
    def marker(self):
        """The runner's hidden event marker, or None for a comment the designer wrote."""
        m = MARKER_RE.search(self.body or "")
        return json.loads(m.group(1)) if m else None


@dataclass
class Issue:
    number: int
    title: str
    body: str
    labels: list
    state: str  # "OPEN" or "CLOSED"
    comments: list = field(default_factory=list)
    url: str = ""

    def last_marker(self, event=None):
        for c in reversed(self.comments):
            m = c.marker
            if m and (event is None or m.get("event") == event):
                return m
        return None

    def designer_spoke_last(self):
        """True when the newest word in the thread is the designer's (the body counts as theirs)."""
        return not self.comments or self.comments[-1].marker is None

    def last_crew_index(self):
        for i in range(len(self.comments) - 1, -1, -1):
            if self.comments[i].marker:
                return i
        return -1


@dataclass
class Item:
    """An epic or task on the board."""
    number: int
    title: str
    kind: str  # "epic" or "task"
    status: str
    attempt: int
    tokens: int
    state: str
    item_id: str = ""


def crew_comment(who, text, event, **data):
    """A comment the runner posts for an agent: a visible header and a hidden event marker."""
    marker = json.dumps({"event": event, **data}, separators=(",", ":"))
    return f"**[{who}]** {text.strip()}\n\n<!-- crew:{marker} -->"


def slug(text, n=40):
    s = re.sub(r"[^a-z0-9]+", "-", text.lower()).strip("-")
    return s[:n].strip("-") or "epic"
