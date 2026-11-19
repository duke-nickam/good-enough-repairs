"""The runner: one general loop, the same for every kind of work.

Each pass: the Director answers new requests, designer moves are applied, then every ready task is worked
(agent -> checks -> reviewer) until nothing is ready. All state lives on the board, so a stopped runner
continues where it left off."""
import datetime
import hashlib
import json
import os
import re
import shlex
import subprocess
import sys

from .agents import (AgentError, added_file_problems, describe_tokens, parse_design_note, parse_work, sum_usage, total_tokens,
                     validate_gaps, validate_plan)
from .model import AGENTS, STANDING_GOAL, Card, crew_comment, slug
from .priority import rank

ACTIVE = ("In progress", "Waiting for review", "In review")
ASCII_ONLY = (".cs", ".py", ".asmdef", ".asmref", ".json")  # code and data; player text in content/ is exempt
STANDING_BODY = ("Opened by the Director because `director_autoplan` is on (see Goals in docs/agent-board-plan.md).\n\n"
                 "**Goal:** every GDD feature in the current scope (the production plan's \"Current scope\" line) "
                 "is done.\n**Done when:** every such feature has a Done epic (built, tested and merged).\n\n"
                 "Confirmed by the designer in the agent board plan. When the scope is done, the designer changes the "
                 "\"Current scope\" line and reopens this. Close it to stop the standing goal.")


def unity_metas(files):
    """The .meta of every written file under Assets/ and of its folders: Unity writes them when a check imports
    the files, so the designer never has to open Unity to make them. Missing ones are skipped at commit."""
    out = set()
    for path in files:
        if not path.startswith("Assets/"):
            continue
        out.add(path + ".meta")
        parts = path.split("/")
        for k in range(2, len(parts)):  # Assets/X, Assets/X/Y, ... (Assets itself has no .meta)
            out.add("/".join(parts[:k]) + ".meta")
    return sorted(out)


def log(who, msg):
    print(f"[{datetime.datetime.now():%H:%M:%S}] {who:<17} {msg}", flush=True)


def alert(settings, kind):
    """A sound on the designer's PC: 'needs_designer' (a task waits for you) or 'stopped' (the run ended)."""
    if not settings.get("sound", True):
        return
    try:
        import winsound  # Windows only
        winsound.MessageBeep(winsound.MB_ICONEXCLAMATION if kind == "needs_designer" else winsound.MB_ICONASTERISK)
    except ImportError:
        print("\a", end="", flush=True)  # other systems: the terminal bell


class Runner:
    def __init__(self, board, ws, agents, settings):
        self.board, self.ws, self.agents, self.s = board, ws, agents, settings
        self.max = settings["max_attempts"]
        self.cards = {}
        self.goals = {}
        self.created = []

    # ------------------------------------------------------------ helpers

    def card(self, number):
        if number not in self.cards:
            self.cards[number] = Card.parse(self.board.issue(number).body)
        return self.cards[number]

    def add_tokens(self, item, usage_list):
        """Adds the calls' tokens to the item's Used tokens (never reset) and returns the new total."""
        item.tokens += sum(total_tokens(u) for u in usage_list)
        self.board.set_tokens(item.number, item.tokens)
        return item.tokens

    def thread_text(self, issue, limit=12):
        """The task's thread for the next agent: comments without the hidden markers, newest last."""
        out = []
        for c in issue.comments[-limit:]:
            body = c.body.split("<!-- crew:")[0].strip()
            out.append(body if c.marker else f"**[Designer]** {body}")
        return "\n\n".join(out)

    def branch(self, epic_number, epic_title):
        return f"epic/{epic_number}-{slug(epic_title)}"

    # ------------------------------------------------------------ one pass

    def run_once(self):
        self.cards = {}
        self.sync()
        self.handle_requests()
        self.sync()
        while True:
            items = self.board.items()
            task = self.next_task(items)
            if not task:
                break
            self.work(task, items)
        self.sync()
        self.budget()

    # ------------------------------------------------------------ requests and planning

    def handle_requests(self):
        """Every open request is a goal. Requests come first; the standing goal (autoplan) waits while one is open."""
        items = {i.number: i for i in self.board.items()}
        requests = self.board.requests()
        standing = [r for r in requests if r.title == STANDING_GOAL]
        others = [r for r in requests if r.title != STANDING_GOAL]
        for r in others:
            self.goal_turn(r, items)
        if not self.s.get("director_autoplan"):
            return
        if not standing and not any(r.title == STANDING_GOAL for r in self.board.requests("all")):
            n = self.board.create_issue(STANDING_GOAL, STANDING_BODY, ["request"])
            log("Director", f"autoplan: opened the standing goal #{n}")
            standing = [self.board.issue(n)]
        if others:
            if standing:
                log("Director", f"standing goal #{standing[0].number} waits: a request is open")
            return
        for r in standing:
            self.goal_turn(r, items)

    def goal_turn(self, r, items):
        """Decides whether the goal needs the Director now: the designer spoke, or its planned epic is done."""
        planned = [e for c in r.comments if c.marker and c.marker.get("event") == "plan"
                   for e in c.marker.get("epics", [])]
        unplanned = set(self.unplanned_epics(items))  # e.g. its tasks were closed as not planned, to plan it again
        open_planned = [e for e in planned if items.get(e) and items[e].state == "OPEN" and e not in unplanned]
        if r.designer_spoke_last():
            return self.goal_step(r, items, open_planned)
        last = r.last_marker() or {}
        if last.get("event") == "gaps" and not open_planned:
            return self.goal_step(r, items, open_planned)  # gaps were ranked but no epic got its tasks: plan now
        if last.get("event") != "epic_done" or open_planned:
            return  # waiting: for the designer's answer, or for the open epic
        done = 0
        for c in reversed(r.comments):
            if not c.marker:
                break
            done += c.marker.get("event") == "epic_done"
        limit = int(self.s.get("goal_epic_limit", 3))
        if done >= limit:
            self.board.comment(r.number, crew_comment(
                "Director", f"{done} epics of this goal are Done and its done-when is not checked yet. Before I plan "
                            "more: keep going, change the goal, or stop? Answer here.", "limit", done=done))
            alert(self.s, "needs_designer")
            log("Director", f"request #{r.number}: {done} epics done, asked the designer before planning more")
            return
        self.goal_step(r, items, open_planned)

    def epic_goal(self, number):
        """An epic's one-line goal: its body without the request line."""
        if number not in self.goals:
            body = re.sub(r"\*\*Request:\*\*.*", "", self.board.issue(number).body or "")
            line = next((l.strip() for l in body.splitlines() if l.strip()), "")
            self.goals[number] = line[:240]
        return self.goals[number]

    def board_summary(self, items):
        """The board as the Director sees it: epics with their goal and state, open tasks, and the Priority field."""
        lines = []
        for i in sorted(items.values(), key=lambda i: i.number):
            if i.kind != "epic" or i.reason == "NOT_PLANNED":
                continue
            goal = self.epic_goal(i.number)
            name = f"#{i.number} [epic] {i.title}" + (f" (goal: {goal})" if goal else "")
            if i.state == "CLOSED":
                lines.append(f"{name} - Done")
                continue
            tasks = [t for t in items.values() if t.kind == "task" and t.reason != "NOT_PLANNED" and
                     self.card(t.number) and self.card(t.number).epic == i.number]
            prio = "" if i.priority is None else f", Priority {i.priority}"
            if not tasks:
                lines.append(f"{name} - open, no tasks yet{prio}")
                continue
            lines.append(f"{name} - in progress ({i.status}){prio}")
            lines += [f"  #{t.number} [task] {t.title} - {t.status}" for t in tasks if t.state == "OPEN"]
        return "\n".join(lines)

    def unplanned_epics(self, items):
        has_tasks = {self.card(t.number).epic for t in items.values()  # a task closed as not planned does not count
                     if t.kind == "task" and t.reason != "NOT_PLANNED" and self.card(t.number)}
        return [i.number for i in items.values() if i.kind == "epic" and i.state == "OPEN" and i.number not in has_tasks]

    def pending_tokens(self, r):
        """The Director's tokens on this goal since its last planned epic, charged to the next one."""
        total = 0
        for c in reversed(r.comments):
            m = c.marker
            if m and m.get("event") == "plan":
                break
            if m:
                total += int(m.get("tokens") or 0)
        return total

    def goal_step(self, r, items, open_planned):
        log("Director", f"request #{r.number}: working toward the goal")
        thread = f"# {r.title}\n\n{r.body}\n\n" + self.thread_text(r, limit=30)
        summary = self.board_summary(items)
        unplanned = self.unplanned_epics(items)
        usages, problems, reply = [], None, None
        for _ in range(2):  # one retry if the reply breaks the rules
            try:
                reply, usage, _model = self.agents.goal(thread, summary, problems)
            except (AgentError, ValueError) as e:
                log("Director", f"request #{r.number}: reply unusable ({e})")
                problems, reply = [f"reply unusable: {e}"], None
                continue
            usages.append(usage)
            problems = validate_gaps(reply, unplanned) if reply.get("type") == "gaps" else []
            if not problems:
                break
        spent = sum(total_tokens(u) for u in usages)
        kind = (reply or {}).get("type")
        if problems or kind not in ("question", "note", "goal", "done", "gaps"):
            problems = problems or [f"unknown reply type {kind!r}"]
            self.board.comment(r.number, crew_comment(
                "Director", "I could not make a valid reply. Problems:\n" + "\n".join(f"- {p}" for p in problems),
                "question", tokens=spent))
            log("Director", f"request #{r.number}: no valid reply, told the designer")
            return
        if kind in ("question", "note", "goal"):
            text = reply.get("text", "")
            if kind == "goal":
                text = f"**Goal:** {reply.get('goal', '')}\n**Done when:** {reply.get('done_when', '')}\n\n{text}"
            self.board.comment(r.number, crew_comment("Director", text, "goal" if kind == "goal" else "question",
                                                      tokens=spent))
            log("Director", f"request #{r.number}: answered in the thread ({kind})")
            return
        if kind == "done":
            if self.designer_spoke_since_plan(r):  # the designer commented after the epic was planned: they confirm
                self.board.comment(r.number, crew_comment(
                    "Director", f"I think the goal is met: {reply.get('text', '')}\n\nDesigner: close this request "
                                "to confirm, or comment what is still missing.", "met_asked", tokens=spent))
                alert(self.s, "needs_designer")
                log("Director", f"request #{r.number}: goal looks met, asked the designer to confirm")
                return
            for en in unplanned:  # otherwise the merge was the designer's acceptance
                if (re.search(r"\*\*Request:\*\* #(\d+)", self.board.issue(en).body) or [0, 0])[1] == str(r.number):
                    self.drop_epic(en, r.number)
            self.board.comment(r.number, crew_comment("Director", f"Goal met: {reply.get('text', '')} Closing it.",
                                                      "closed", tokens=spent))
            self.board.close(r.number)
            log("Director", f"request #{r.number}: goal met, closed")
            return
        self.apply_gaps(r, reply, items, unplanned, spent, open_planned)

    def designer_spoke_since_plan(self, r):
        """True when a designer comment comes after the goal's latest planned epic."""
        for c in reversed(r.comments):
            if c.marker is None:
                return True
            if c.marker.get("event") == "plan":
                return False
        return False

    def drop_epic(self, en, request):
        self.board.comment(en, crew_comment("Director", f"Dropped: no longer a gap of the goal in #{request}.",
                                            "dropped"))
        self.board.close(en, "not planned")
        log("Director", f"epic #{en}: no longer a gap, closed as not planned")

    def apply_gaps(self, r, reply, items, unplanned, spent, open_planned):
        """Gaps become epics in Backlog with their Priority; the top one gets its tasks."""
        gaps = {g["key"]: g for g in reply["gaps"]}
        reused = {g["epic"] for g in gaps.values() if g.get("epic")}
        for en in unplanned:
            if en not in reused and f"**Request:** #{r.number}\n" in self.board.issue(en).body + "\n":
                self.drop_epic(en, r.number)
        for g in gaps.values():
            if not g.get("epic"):
                g["epic"] = self.board.create_issue(g["title"], f"**Request:** #{r.number}\n\n{g.get('goal', '')}\n",
                                                    ["epic"])
                self.board.add_item(g["epic"], "Backlog")
                log("Director", f"gap: epic #{g['epic']} {g['title']}")
        ranked = rank(gaps)
        lines = []
        for key, score, why in ranked:
            en = gaps[key]["epic"]
            self.board.set_priority(en, score)
            text = f"Priority {score}: {why}" if score is not None else f"No priority yet: {why}"
            last = self.board.issue(en).last_marker("priority")
            if not last or last.get("why") != text:
                self.board.comment(en, crew_comment("Director", text, "priority", why=text))
            lines.append(f"- #{en} {gaps[key]['title']}: {text}")
        self.board.comment(r.number, crew_comment(
            "Director", f"**Goal:** {reply['goal']}\n**Done when:** {reply['done_when']}\n\n{reply.get('summary', '')}"
                        f"\n\nGaps, best first:\n" + "\n".join(lines), "gaps",
            epics=[g["epic"] for g in gaps.values()], tokens=spent))
        log("Director", f"request #{r.number}: {len(gaps)} gap(s) ranked")
        if open_planned:
            log("Director", f"request #{r.number}: epic #{open_planned[0]} is still open; one epic per goal at a time")
            return
        top = next((k for k, score, _ in ranked if score is not None), None)
        if not top:
            self.board.comment(r.number, crew_comment(
                "Director", "Every gap waits for another gap, so none can start. Please check the gaps above.",
                "question"))
            alert(self.s, "needs_designer")
            return
        self.plan_epic(r, gaps[top])

    def plan_epic(self, r, gap):
        """The Director plans the tasks of one epic: the gap the priority function put first."""
        en = gap["epic"]
        log("Director", f"request #{r.number}: planning the tasks of #{en} {gap['title']}")
        thread = f"# {r.title}\n\n{r.body}\n\n" + self.thread_text(r, limit=30)
        items = {i.number: i for i in self.board.items()}
        summary = self.board_summary(items)
        epic = {"number": en, "title": gap["title"], "goal": gap.get("goal", "")}
        usages, problems, plan = [], None, None
        for _ in range(2):  # one retry if the plan breaks the rules
            try:
                plan, usage, _model = self.agents.plan(thread, summary, self.ws.list_files(), problems, epic=epic)
            except (AgentError, ValueError) as e:
                log("Director", f"request #{r.number}: reply unusable ({e})")
                problems, plan = [f"reply unusable: {e}"], None
                continue
            usages.append(usage)
            if plan.get("type") in ("question", "note"):
                break
            problems = validate_plan(plan, None)
            if not problems and len(plan.get("epics") or []) != 1:
                problems = ["the plan must have exactly one epic: the one named above"]
            if not problems:
                break
        spent = sum(total_tokens(u) for u in usages)
        if plan and plan.get("type") in ("question", "note"):
            self.board.comment(r.number, crew_comment("Director", plan.get("text", ""), "question", tokens=spent))
            log("Director", f"request #{r.number}: asked before planning #{en}")
            return
        if not plan or problems:
            self.board.comment(r.number, crew_comment(
                "Director", f"I could not plan the tasks of #{en}. Problems:\n" + "\n".join(f"- {p}" for p in problems or []),
                "question", tokens=spent))
            log("Director", f"request #{r.number}: no valid plan for #{en}, told the designer")
            return
        self.created = []
        try:
            self.create_plan(r, plan, spent + self.pending_tokens(self.board.issue(r.number)), existing_epic=en)
        except Exception:
            made = ", ".join(f"#{n}" for n in self.created) or "nothing"
            self.board.comment(r.number, crew_comment(
                "Runner", f"Creating the plan failed halfway; it created {made}. Delete those issues, then comment "
                          "here (for example 'plan again') and run the runner.", "plan_failed"))
            raise
        log("Director", f"request #{r.number}: planned the tasks of #{en}")

    def create_plan(self, r, plan, tokens, existing_epic=None):
        """Epics first, then every task issue, then the cards, which can now name any task by its number.
        With existing_epic, the plan's one epic is that epic (a gap already on the board)."""
        status = "Todo" if self.s.get("autoapprove_tasks") else "Backlog"
        share = tokens // len(plan["epics"])
        epic_of, tasks, epics = {}, {}, []
        for e in plan["epics"]:
            if existing_epic:
                en = existing_epic
            else:
                en = self.board.create_issue(e["title"], f"**Request:** #{r.number}\n\n{e.get('goal', '')}\n", ["epic"])
                self.created.append(en)
                self.board.add_item(en, "Backlog")
            self.board.set_tokens(en, share)
            self.board.comment(en, crew_comment("Director", f"Planned from request #{r.number}. "
                                                            f"Planning used {share:,} tokens.", "planning", tokens=share))
            epics.append((en, e))
            for t in e["tasks"]:
                epic_of[t["key"]], tasks[t["key"]] = en, t
        numbers, order = {}, []

        def visit(k):
            if k in numbers or k in order:
                return
            for d in tasks[k].get("depends_on") or []:
                visit(d)
            order.append(k)
        for k in tasks:
            visit(k)
        for k in order:  # first every task issue, so all numbers are known before the cards are written
            n = self.board.create_issue(tasks[k]["title"], "(The task card follows.)", ["task"])
            self.created.append(n)
            numbers[k] = n
        keys = sorted(numbers, key=len, reverse=True)

        def refs(text):
            """The Director's task keys ("t1") become issue numbers ("#18")."""
            for key in keys:
                text = re.sub(rf"(?<![#\w]){re.escape(key)}(?!\w)", f"#{numbers[key]}", text)
            return text
        for k in order:
            t, n = tasks[k], numbers[k]
            card = Card(agent=t["agent"], reviewer=t["reviewer"], epic=epic_of[k], what=refs(t.get("what", "")),
                        acceptance=[refs(a) for a in t["acceptance"]], files=t["files"], reads=t.get("reads") or [],
                        checks=t.get("checks") or [], depends_on=[numbers[d] for d in t.get("depends_on") or []])
            self.board.set_body(n, card.to_markdown())
            self.cards[n] = card
            self.board.add_item(n, status, attempt=0)  # on the board first, so GitHub's auto-add has nothing to do
            self.board.add_sub_issue(epic_of[k], n)
        lines = []
        for en, e in epics:
            lines.append(f"**#{en} {e['title']}**")
            lines += [f"- #{numbers[t['key']]} {t['title']} ({AGENTS[t['agent']]}, reviewed by "
                      f"{AGENTS[t['reviewer']]})" for t in e["tasks"]]
        where = "Todo (autoapprove is on)" if status == "Todo" else "Backlog: move them to Todo to start"
        text = f"{plan.get('summary', '')}\n\n" + "\n".join(lines) + f"\n\nTasks are in {where}."
        self.board.comment(r.number, crew_comment("Director", text, "plan", epics=[en for en, _ in epics]))
        return [en for en, _ in epics]

    # ------------------------------------------------------------ designer moves, epics, pull requests

    def sync(self):
        items = self.board.items()
        for t in (i for i in items if i.kind == "task" and i.state == "OPEN"):
            if t.status == "Todo" and t.attempt > 0:
                issue = self.board.issue(t.number)
                last = issue.last_marker()
                # Back in Todo from Needs designer, or a Done task the designer reopened: new conditions, fresh attempts
                if last and last.get("event") in ("needs_designer", "design_note", "approved", "accepted"):
                    rnd = sum(1 for c in issue.comments if c.marker and c.marker.get("event") == "reset") + 2
                    self.board.set_attempt(t.number, 0)
                    self.board.comment(t.number, crew_comment(
                        "Runner", f"Designer input: attempts reset (round {rnd}).", "reset", round=rnd))
                    log("Runner", f"#{t.number}: designer input, attempts reset to 0/{self.max}")
            elif t.status == "Done":
                issue = self.board.issue(t.number)
                last = issue.last_marker()
                if last and last.get("event") == "needs_designer":
                    self.board.comment(t.number, crew_comment("Runner", "Accepted by the designer.", "accepted"))
                self.board.close(t.number)
                log("Runner", f"#{t.number}: Done, closed")
        items = self.board.items()
        for e in (i for i in items if i.kind == "epic"):
            tasks = [i for i in items if i.kind == "task" and i.reason != "NOT_PLANNED" and self.card(i.number)
                     and self.card(i.number).epic == e.number]
            planning = (self.board.issue(e.number).last_marker("planning") or {}).get("tokens", 0)
            total = planning + sum(t.tokens for t in tasks)
            if total != e.tokens:
                self.board.set_tokens(e.number, total)  # the epic shows planning plus all its tasks
            if e.state == "CLOSED":
                if e.reason != "NOT_PLANNED" and e.status != "Done":
                    self.board.set_status(e.number, "Done")
                    self.epic_done(e)
                continue
            if not tasks:
                if e.status != "Backlog":  # an epic without tasks is not planned, so it cannot go further
                    self.board.set_status(e.number, "Backlog")
                    self.board.comment(e.number, crew_comment(
                        "Director", "Not planned yet, so it stays in Backlog. To have it planned next, open a request, "
                                    f"e.g. \"Plan #{e.number} next\".", "unplanned"))
                    log("Runner", f"epic #{e.number}: has no tasks, moved back to Backlog")
                continue
            if all(t.state == "CLOSED" for t in tasks):
                branch = self.branch(e.number, e.title)
                if self.board.pr_for(branch) or not self.ws.has_commits(branch):
                    continue
                body = (f"Closes #{e.number}\n\nTasks:\n" + "\n".join(f"- #{t.number} {t.title}" for t in tasks) +
                        "\n\nMerge with **Squash and merge** to accept the epic.")
                url = self.board.open_pr(branch, self.ws.base, f"Epic #{e.number}: {e.title}", body)
                self.board.set_status(e.number, "Waiting for review")
                self.board.comment(e.number, crew_comment("Runner", f"All tasks are Done. Pull request: {url}", "pr",
                                                          url=url))
                log("Runner", f"epic #{e.number}: all tasks Done, pull request opened")
            elif any(t.status in ACTIVE or t.state == "CLOSED" for t in tasks) and e.status != "In progress":
                self.board.set_status(e.number, "In progress")

    def epic_done(self, e):
        """Tells the goal that planned the epic: its next turn checks the goal again."""
        for r in self.board.requests():
            plans = [c.marker for c in r.comments if c.marker and c.marker.get("event") == "plan"]
            if any(e.number in p.get("epics", []) for p in plans):
                self.board.comment(r.number, crew_comment("Runner", f"Epic #{e.number} {e.title} is Done.",
                                                          "epic_done", epic=e.number))
                log("Runner", f"epic #{e.number}: Done, told request #{r.number}")

    # ------------------------------------------------------------ picking and working a task

    def next_task(self, items):
        by_number = {i.number: i for i in items}
        tasks = sorted((i for i in items if i.kind == "task" and i.state == "OPEN"), key=lambda i: i.number)
        for t in tasks:  # interrupted work first
            if t.status in ACTIVE:
                return t
        for t in tasks:
            if t.status != "Todo":
                continue
            card = self.card(t.number)
            if not card:
                continue
            deps = [by_number.get(d) for d in card.depends_on]
            if all(d and (d.status == "Done" or d.state == "CLOSED") for d in deps):
                return t
        return None

    def work(self, t, items):
        n = t.number
        issue = self.board.issue(n)
        card = self.card(n)
        epic = next((i for i in items if i.number == card.epic), None) if card else None
        if not card or not epic:
            self.board.set_status(n, "Needs designer")
            alert(self.s, "needs_designer")
            self.board.comment(n, crew_comment("Runner", "This task has no readable card or epic. Fix the card "
                                                         "and move it to Todo.", "needs_designer"))
            log("Runner", f"#{n}: unreadable card, Needs designer")
            return
        agent, who = card.agent, AGENTS[card.agent]
        self.ws.use_branch(self.branch(epic.number, epic.title))
        resume_review = t.status in ("Waiting for review", "In review")
        usages = []
        if resume_review:
            last = issue.last_marker("result") or {}
            attempt, summary = t.attempt, last.get("summary", "")
            written = {p: self.ws.read(p) for p in card.files}
            log("Runner", f"#{n}: resuming the review of attempt {attempt}")
        else:
            if t.status == "In progress":
                attempt = max(t.attempt, 1)
                log("Runner", f"#{n}: resuming attempt {attempt} after an interruption")
            else:
                attempt = t.attempt + 1
                self.board.set_attempt(n, attempt)
            self.board.set_status(n, "In progress")
            log(who, f"#{n} {t.title}: attempt {attempt}/{self.max}")
            files = {p: self.ws.read(p) for p in card.files}
            reads = {p: self.ws.read(p) for p in card.reads}
            for d in card.depends_on:
                dc = self.card(d)
                for p in (dc.files if dc else []):
                    reads.setdefault(p, self.ws.read(p))
            if agent == "programmer" and any(p.endswith(".cs") for p in card.files) and not self.note_done(issue):
                return self.design_note(t, card, issue, files, reads, attempt, who)
            tries = 1 + max(0, int(self.s.get("check_retries", 0)))
            feedback, check_log, checks_text = None, [], ""
            attempt_files = {}  # every file this attempt wrote, over all its tries

            def keep_tries(why):
                """Files from earlier failed tries are committed, so an attempt never leaves the repo dirty."""
                if check_log:
                    self.ws.commit(list(card.files), f"#{n} {t.title} - {who}, attempt {attempt} ({why})")
            for k in range(1, tries + 1):  # self-check: the agent sees failed checks and fixes them in this attempt
                try:
                    text, usage, model = self.agents.work(agent, card.to_markdown(), self.thread_text(issue), files,
                                                          reads, attempt, feedback)
                    usages.append(usage)
                    written, summary, question = parse_work(text)
                except (AgentError, ValueError) as e:
                    keep_tries("failed checks, then an unusable reply")
                    self.reject(t, attempt, f"The agent's reply was unusable: {e}", usages, who)
                    return
                if question:
                    keep_tries("failed checks, then a question")
                    total = self.add_tokens(t, usages)
                    self.board.set_status(n, "Needs designer")
                    alert(self.s, "needs_designer")
                    self.board.comment(n, crew_comment(
                        who, f"Question for the designer (attempt {attempt}):\n\n{question}\n\n"
                             f"Used: {describe_tokens(sum_usage(usages))}. Task total: {total:,}.",
                        "needs_designer", attempt=attempt))
                    log(who, f"#{n}: asked the designer, Needs designer")
                    return
                stray = [p for p in written if p not in card.files]
                if stray:
                    keep_tries("failed checks, then files outside the card")
                    self.reject(t, attempt, "Wrote files outside the card: " + ", ".join(stray), usages, who)
                    return
                for p, c in written.items():
                    if c is None:
                        self.ws.delete(p)  # a moved or removed file
                    else:
                        self.ws.write(p, c)
                attempt_files.update(written)  # a later try may re-send only the files it fixed
                last_text = checks_text if k > 1 else None
                checks_ok, checks_text, made, shots = self.run_checks(card, attempt_files)
                check_log.append(f"try {k} {'pass' if checks_ok else 'FAIL'}")
                if checks_ok or k == tries:
                    break
                if checks_text == last_text:  # no progress: the next try would fail the same way
                    check_log.append("stopped: same failure as the try before")
                    log(who, f"#{n}: same check failure twice, no more self-check tries")
                    break
                log(who, f"#{n}: checks failed, self-check try {k + 1}/{tries}")
                files = {p: self.ws.read(p) for p in card.files}
                feedback = checks_text
            written = attempt_files  # checks, commit and review all see every file of the attempt
            # every file on the card, so nothing the agent wrote is ever left out of the commit
            paths = list(card.files) + [p + ".meta" for p, c in written.items() if c is None]
            paths += [x for p in made for x in (p, p + ".meta")]  # scenes and prefabs Unity made in a check
            paths += unity_metas(written)  # the .meta files Unity made when a check imported the new files
            sha = self.ws.commit(paths, f"#{n} {t.title} - {who}, attempt {attempt}")
            link = self.ws.commit_link(sha) if sha else "no change from the last attempt"
            self.board.set_status(n, "Waiting for review")
            unity = f"Made by Unity in a check: {', '.join(made)}.\n" if made else ""
            self.board.comment(n, crew_comment(who, f"Attempt {attempt} ({model}): {summary}\n\n"
                                                    f"Self-check: {', '.join(check_log)}.\n{unity}Commit: {link}\n"
                                                    f"Used: {describe_tokens(sum_usage(usages))}.", "result",
                                               attempt=attempt, summary=summary[:500]))
        # ------------------------------------------------ review
        reviewer = AGENTS[card.reviewer]
        if resume_review:
            checks_ok, checks_text, made, shots = self.run_checks(card, written)
            reads = {p: self.ws.read(p) for p in card.reads}
        failed = None if checks_ok else hashlib.sha1(checks_text.encode("utf-8")).hexdigest()[:12]
        before = None  # the previous attempt's failed checks, only since the last designer input
        rejected_before = False  # the previous attempt was rejected, since the last designer input
        for c in reversed(issue.comments):
            m = c.marker
            if m and m.get("event") in ("rejected", "reset", "needs_designer", "approved", "accepted"):
                rejected_before = m.get("event") == "rejected"
                before = m.get("failed") if rejected_before else None
                break
        if failed and failed == before:  # no progress across attempts: the agent cannot fix this itself
            total = self.add_tokens(t, usages)
            self.board.set_status(n, "Needs designer")
            alert(self.s, "needs_designer")
            self.board.comment(n, crew_comment(
                "Runner", f"Attempt {attempt} failed its checks exactly like the attempt before, so the agent "
                          f"cannot fix it by itself (for example the check needs another task's file). No review "
                          f"was run.\n\nChecks:\n{checks_text}\n\nTask total: {total:,} tokens.",
                "needs_designer", attempt=attempt))
            log("Runner", f"#{n}: same check failure as the last attempt, Needs designer")
            return
        self.board.set_status(n, "In review")
        log(reviewer, f"#{n}: reviewing (checks {'pass' if checks_ok else 'FAIL'}"
                      f"{f', {len(shots)} screenshot(s)' if shots else ''})")
        review_used, review_usages = "", []
        for k in (1, 2):  # one retry if the review reply is unusable: the agent's work is not at fault
            try:
                verdict, usage, _ = self.agents.review(card.reviewer, card.to_markdown(), summary, written,
                                                       checks_text, self.thread_text(self.board.issue(n)),
                                                       reads={p: c for p, c in reads.items() if p not in written},
                                                       images=shots)
                review_usages.append(usage)
                break
            except (AgentError, ValueError) as e:
                verdict = {"verdict": "reject", "summary": f"Review reply unusable twice ({e}).", "issues": []}
                if k == 1:
                    log(reviewer, f"#{n}: review reply unusable ({e}), asking again")
        usages += review_usages
        if review_usages:
            review_used = f"Review used: {describe_tokens(sum_usage(review_usages))}. "
        issues = verdict.get("issues") or []
        lines = "\n".join(f"- {i.get('where', '')}: {i.get('problem', '')} Fix: {i.get('fix', '')}" for i in issues)
        approve = verdict.get("verdict") == "approve" and checks_ok
        if approve and any(p.startswith("docs/crew/") for p in card.files):
            total = self.add_tokens(t, usages)
            self.board.set_status(n, "Needs designer")
            alert(self.s, "needs_designer")
            self.board.comment(n, crew_comment(
                reviewer, f"Approved: {verdict.get('summary', '')}\n\n{lines}\n\nThis task writes in docs/crew/, so "
                          "the designer approves it too: move it to Done to accept, or comment and move it to Todo."
                          f"\n\nChecks:\n{checks_text}\n\n{review_used}Task total: {total:,} tokens.",
                "needs_designer", attempt=attempt))
            log(reviewer, f"#{n}: approved, waits for the designer (docs/crew/)")
        elif approve:
            total = self.add_tokens(t, usages)
            self.board.set_status(n, "Done")
            self.board.close(n)
            self.board.comment(n, crew_comment(reviewer, f"Approved: {verdict.get('summary', '')}\n\n{lines}\n\n"
                                                         f"Checks:\n{checks_text}\n\n{review_used}Task total: "
                                                         f"{total:,} tokens.", "approved", attempt=attempt))
            log(reviewer, f"#{n}: approved, Done ({total:,} tokens)")
        else:
            reason = verdict.get("summary", "")
            if not checks_ok and verdict.get("verdict") == "approve":
                reason = "The reviewer approved, but a check failed, so it is rejected. " + reason
            stuck = bool(verdict.get("repeat")) and rejected_before  # same problem twice: the agent cannot fix it
            self.reject(t, attempt, f"{reason}\n\n{lines}\n\nChecks:\n{checks_text}\n\n{review_used}".rstrip(),
                        usages, reviewer, failed=failed, stuck=stuck)

    def note_done(self, issue):
        """May the Programmer write code? Yes after the Director approved the note, or after designer input on it."""
        events = [c.marker.get("event") for c in issue.comments if c.marker]
        if "design_note" not in events:
            return False
        if not self.s.get("design_note_review", True):
            return True  # the designer answers every note
        last_note = len(events) - 1 - events[::-1].index("design_note")
        return "note_approved" in events or "reset" in events[last_note:]

    def design_note(self, t, card, issue, files, reads, attempt, who):
        """The Programmer's first try on a code task: a design note in game terms, not code. The Director reviews
        it like a producer; only questions the GDD and docs/crew/ leave open go to the designer."""
        n = t.number
        try:
            text, usage, model = self.agents.work(card.agent, card.to_markdown(), self.thread_text(issue), files, reads,
                                                  attempt, design_note=True)
        except (AgentError, ValueError) as e:
            self.reject(t, attempt, f"The agent's reply was unusable: {e}", [], who)
            return
        note = parse_design_note(text)
        total = self.add_tokens(t, [usage])
        if not self.s.get("design_note_review", True):
            self.board.set_status(n, "Needs designer")
            alert(self.s, "needs_designer")
            self.board.comment(n, crew_comment(
                who, f"Design note ({model}):\n\n{note}\n\nDesigner: answer the questions or approve in "
                     f"a comment, then move this task to Todo; the code follows the note.\n\n"
                     f"Used: {describe_tokens(usage)}. Task total: {total:,}.", "design_note", attempt=attempt))
            log(who, f"#{n}: design note posted, Needs designer")
            return
        self.board.comment(n, crew_comment(who, f"Design note ({model}):\n\n{note}\n\n"
                                                f"Used: {describe_tokens(usage)}. Task total: {total:,}.",
                                           "design_note", attempt=attempt))
        log(who, f"#{n}: design note posted, the Director reviews it")
        issue = self.board.issue(n)
        try:
            verdict, usage, _ = self.agents.review_note(card.to_markdown(), note, self.thread_text(issue))
            total = self.add_tokens(t, [usage])
        except (AgentError, ValueError) as e:
            verdict = {"verdict": "ask", "summary": f"My review reply was unusable ({e}).", "questions": [
                "Please read the note and approve it or answer it."]}
        kind = verdict.get("verdict")
        events = [c.marker.get("event") for c in issue.comments if c.marker]
        since = events[len(events) - 1 - events[::-1].index("reset"):] if "reset" in events else events
        if kind == "revise" and since.count("note_revise") >= int(self.s.get("note_revise_rounds", 1)):
            kind = "ask"  # the revise round is used up: the designer decides
            verdict["questions"] = (verdict.get("questions") or []) + [
                "The note still has these issues after a revise round: " + "; ".join(verdict.get("issues") or [])]
        answers = "\n".join(f"- {a}" for a in verdict.get("answers") or [])
        answers = f"\n\nAnswers to the note's questions:\n{answers}" if answers else ""
        added = []
        if kind == "approve" and verdict.get("add_files"):
            wanted = [f.strip().strip("`") for f in verdict["add_files"] if f and f.strip().strip("`") not in card.files]
            others = {}
            for i in self.board.items():
                c = self.card(i.number) if i.kind == "task" and i.state == "OPEN" and i.number != n else None
                for f in (c.files if c else []):
                    others[f] = f"#{i.number}"
            problems = [x for f in wanted for x in added_file_problems(card.agent, card.reviewer, f, others)]
            if problems:
                kind = "ask"  # the runner may not add them: the designer decides
                verdict["questions"] = (verdict.get("questions") or []) + [
                    "The note needs files the card does not list, and they cannot be added: " + "; ".join(problems)]
            elif wanted:
                body = self.board.issue(n).body
                line = "**Files:** " + ", ".join(f"`{f}`" for f in card.files + wanted)
                self.board.set_body(n, re.sub(r"^\*\*Files:\*\*.*$", lambda _: line, body, count=1, flags=re.M))
                card.files += wanted
                self.cards[n] = card
                added = wanted
                log("Director", f"#{n}: added to the card: {', '.join(wanted)}")
        if added:
            answers += "\n\nAdded to the card's Files: " + ", ".join(f"`{f}`" for f in added) + "."
        if kind == "approve":
            self.board.set_attempt(n, 0)  # the note is not an attempt: the code gets all its attempts
            self.board.set_status(n, "Todo")
            self.board.comment(n, crew_comment("Director", f"Design note approved: {verdict.get('summary', '')}"
                                                           f"{answers}\n\nThe code follows the note.\n\n"
                                                           f"Task total: {total:,} tokens.", "note_approved"))
            log("Director", f"#{n}: design note approved, back to Todo for the code")
        elif kind == "revise":
            issues = "\n".join(f"- {x}" for x in verdict.get("issues") or [])
            self.board.set_attempt(n, 0)
            self.board.set_status(n, "Todo")
            self.board.comment(n, crew_comment("Director", f"Design note: revise. {verdict.get('summary', '')}\n\n"
                                                           f"{issues}{answers}\n\nTask total: {total:,} tokens.",
                                               "note_revise"))
            log("Director", f"#{n}: design note sent back for one revise round")
        else:
            questions = "\n".join(f"{i}. {q}" for i, q in enumerate(verdict.get("questions") or [], 1))
            self.board.set_status(n, "Needs designer")
            alert(self.s, "needs_designer")
            self.board.comment(n, crew_comment(
                "Director", f"Design note: questions for the designer. {verdict.get('summary', '')}\n\n{questions}"
                            f"{answers}\n\nDesigner: answer in a comment, then move this task to Todo; the code "
                            f"follows the note and your answers.\n\nTask total: {total:,} tokens.",
                "needs_designer", attempt=attempt))
            log("Director", f"#{n}: design note has open questions, Needs designer")

    def reject(self, t, attempt, text, usages, who, failed=None, stuck=False):
        n = t.number
        total = self.add_tokens(t, usages)
        if stuck and attempt < self.max:
            self.board.set_status(n, "Needs designer")
            alert(self.s, "needs_designer")
            self.board.comment(n, crew_comment(who, f"Rejected on attempt {attempt}/{self.max}: {text}\n\nThis is the "
                                                    "same problem as the last rejection, so the agent cannot fix it by "
                                                    "itself: Needs designer. Answer or change the task, then move it to "
                                                    f"Todo. Task total: {total:,} tokens.", "needs_designer",
                                               attempt=attempt))
            log(who, f"#{n}: rejected twice for the same problem, Needs designer")
        elif attempt >= self.max:
            self.board.set_status(n, "Needs designer")
            alert(self.s, "needs_designer")
            self.board.comment(n, crew_comment(who, f"Rejected on attempt {attempt}/{self.max}: {text}\n\n"
                                                    f"This was the last attempt: Needs designer. Task total: {total:,} "
                                                    "tokens.", "needs_designer", attempt=attempt))
            log(who, f"#{n}: rejected on the last attempt, Needs designer")
        else:
            self.board.set_status(n, "Todo")
            self.board.comment(n, crew_comment(who, f"Rejected on attempt {attempt}/{self.max}: {text}\n\n"
                                                    f"Task total: {total:,} tokens.", "rejected", attempt=attempt,
                                               failed=failed))
            log(who, f"#{n}: rejected, back to Todo")

    # ------------------------------------------------------------ checks

    def run_checks(self, card, written):
        """Code decides these, not an agent: built-in checks on every written file, then the card's scripts.
        -> (ok, text, made, shots): files Unity made in a check ("MADE: <path>") and screenshots for the reviewer
        ("SCREENSHOT: <absolute path>")."""
        out, ok, made, shots = [], True, [], []
        for p, c in written.items():
            if c is None:
                out.append(f"- ok {p}: deleted")
                continue
            if not c.strip():
                out.append(f"- FAIL {p}: empty")
                ok = False
                continue
            if "—" in c or "–" in c:
                out.append(f"- FAIL {p}: contains a long dash; use a plain '-'")
                ok = False
            if p.endswith(ASCII_ONLY) and "/content/" not in p:
                bad = [(i, ch) for i, line in enumerate(c.splitlines(), 1) for ch in line if ord(ch) > 127]
                if bad:
                    where = ", ".join(f"line {i} '{ch}' (U+{ord(ch):04X})" for i, ch in bad[:5])
                    out.append(f"- FAIL {p}: code files must be plain ASCII; found {where}")
                    ok = False
            if p.endswith(".json"):
                try:
                    json.loads(c)
                except ValueError as e:
                    out.append(f"- FAIL {p}: not valid JSON ({e})")
                    ok = False
        for cmd in card.checks:
            args = shlex.split(cmd)
            script = self.ws.read(args[1])
            if script is None:
                out.append(f"- FAIL `{cmd}`: the check script does not exist")
                ok = False
                continue
            try:
                env = {**os.environ, "UNITY_PATH": self.s.get("unity_path", "")}  # for the EditMode test check
                res = subprocess.run([sys.executable, *args[1:]], cwd=self.ws.root, capture_output=True, text=True,
                                     encoding="utf-8", timeout=int(self.s.get("check_timeout", 900)), env=env)
                passed = res.returncode == 0
                detail = (res.stdout + res.stderr).strip()[-3000:]
                for line in res.stdout.splitlines():
                    key, _, value = line.partition(":")
                    value = value.strip()
                    if key == "MADE" and value.startswith("Assets/") and ".." not in value:
                        made.append(value)
                    elif key == "SCREENSHOT" and os.path.isfile(value):
                        shots.append(value)
            except subprocess.TimeoutExpired:
                passed, detail = False, "timed out"
            out.append(f"- {'pass' if passed else 'FAIL'} `{cmd}`" + (f"\n```\n{detail}\n```" if detail else ""))
            ok = ok and passed
        return ok, "\n".join(out) or "- no checks", made, shots

    # ------------------------------------------------------------ token budget

    def budget(self):
        used = {k: 0 for k in AGENTS}
        for i in self.board.items():
            if i.kind == "epic":
                used["director"] += (self.board.issue(i.number).last_marker("planning") or {}).get("tokens", 0)
            else:
                c = self.card(i.number)
                if c:
                    used[c.agent] += i.tokens  # reviews are charged to the task, as the plan says
        for k, budget in self.s.get("token_budget", {}).items():
            if budget and used.get(k, 0) > budget:
                log("Director", f"WARNING: {AGENTS[k]} used {used[k]:,} tokens, over its GDD budget of {budget:,}")
        return used
