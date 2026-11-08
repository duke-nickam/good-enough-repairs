"""The runner: one general loop, the same for every kind of work.

Each pass: the Director answers new requests, designer moves are applied, then every ready task is worked
(agent -> checks -> reviewer) until nothing is ready. All state lives on the board, so a stopped runner
continues where it left off."""
import datetime
import json
import shlex
import subprocess
import sys

from .agents import AgentError, describe_tokens, parse_work, total_tokens, validate_plan
from .model import AGENTS, Card, crew_comment, slug

ACTIVE = ("In progress", "Waiting for review", "In review")


def log(who, msg):
    print(f"[{datetime.datetime.now():%H:%M:%S}] {who:<17} {msg}", flush=True)


class Runner:
    def __init__(self, board, ws, agents, settings):
        self.board, self.ws, self.agents, self.s = board, ws, agents, settings
        self.max = settings["max_attempts"]
        self.cards = {}

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
        requests = self.board.requests()
        items = {i.number: i for i in self.board.items()}
        for r in requests:
            plans = [c.marker for c in r.comments if c.marker and c.marker.get("event") == "plan"]
            epics = [e for p in plans for e in p.get("epics", [])]
            if epics and all(items.get(e) and items[e].state == "CLOSED" for e in epics) and not r.designer_spoke_last():
                self.board.comment(r.number, crew_comment("Director", "All epics of this request are done. Closing it.",
                                                          "closed"))
                self.board.close(r.number)
                log("Director", f"request #{r.number} done, closed")
                continue
            if r.designer_spoke_last():
                self.plan(r, items)
        if self.s.get("director_autoplan") and not requests and \
                not any(i.status in ("Backlog", "Todo") + ACTIVE for i in items.values()):
            n = self.board.create_issue("[Director proposal] Next work from the GDD",
                                        "Autoplan: the Director proposes the next work from the GDD and the board.",
                                        ["request"])
            log("Director", f"autoplan: opened request #{n}")
            self.plan(self.board.issue(n), items)

    def plan(self, r, items):
        log("Director", f"request #{r.number}: reading")
        thread = f"# {r.title}\n\n{r.body}\n\n" + self.thread_text(r, limit=30)
        summary = "\n".join(f"#{i.number} [{i.kind}] {i.title} - {i.status}" for i in items.values()
                            if i.state == "OPEN")
        usages, problems, plan = [], None, None
        for _ in range(2):  # one retry if the plan breaks the rules
            try:
                plan, usage, _model = self.agents.plan(thread, summary, self.ws.list_files(), problems)
            except (AgentError, ValueError) as e:
                log("Director", f"request #{r.number}: reply unusable ({e})")
                problems = [f"reply unusable: {e}"]
                continue
            usages.append(usage)
            if plan.get("type") in ("question", "note"):
                break
            problems = validate_plan(plan, None)
            if not problems:
                break
        pending = sum(c.marker.get("tokens", 0) for c in r.comments if c.marker and c.marker.get("event") == "question")
        spent = sum(total_tokens(u) for u in usages)
        if plan and plan.get("type") in ("question", "note"):
            self.board.comment(r.number, crew_comment("Director", plan.get("text", ""), "question", tokens=spent))
            log("Director", f"request #{r.number}: answered in the thread ({plan['type']})")
            return
        if not plan or problems:
            self.board.comment(r.number, crew_comment(
                "Director", "I could not make a valid plan. Problems:\n" + "\n".join(f"- {p}" for p in problems or []),
                "question", tokens=spent))
            log("Director", f"request #{r.number}: no valid plan, told the designer")
            return
        created = self.create_plan(r, plan, spent + pending)
        log("Director", f"request #{r.number}: planned {len(created)} epic(s)")

    def create_plan(self, r, plan, tokens):
        """Epics first, then tasks in dependency order, so every card names real issue numbers."""
        status = "Todo" if self.s.get("autoapprove_tasks") else "Backlog"
        share = tokens // len(plan["epics"])
        epic_of, tasks, epics = {}, {}, []
        for e in plan["epics"]:
            en = self.board.create_issue(e["title"], f"**Request:** #{r.number}\n\n{e.get('goal', '')}\n", ["epic"])
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
        for k in order:
            t = tasks[k]
            card = Card(agent=t["agent"], reviewer=t["reviewer"], epic=epic_of[k], what=t.get("what", ""),
                        acceptance=t["acceptance"], files=t["files"], reads=t.get("reads") or [],
                        checks=t.get("checks") or [], depends_on=[numbers[d] for d in t.get("depends_on") or []])
            n = self.board.create_issue(t["title"], card.to_markdown(), ["task"])
            numbers[k] = n
            self.cards[n] = card
            self.board.add_sub_issue(epic_of[k], n)
            self.board.add_item(n, status, attempt=0)
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
                if last and last.get("event") == "needs_designer":
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
            tasks = [i for i in items if i.kind == "task" and self.card(i.number) and self.card(i.number).epic == e.number]
            planning = (self.board.issue(e.number).last_marker("planning") or {}).get("tokens", 0)
            total = planning + sum(t.tokens for t in tasks)
            if total != e.tokens:
                self.board.set_tokens(e.number, total)  # the epic shows planning plus all its tasks
            if e.state == "CLOSED":
                if e.status != "Done":
                    self.board.set_status(e.number, "Done")
                continue
            if not tasks:
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
            try:
                text, usage, model = self.agents.work(agent, card.to_markdown(), self.thread_text(issue), files, reads,
                                                      attempt)
                usages.append(usage)
                written, summary, question = parse_work(text)
            except (AgentError, ValueError) as e:
                self.reject(t, attempt, f"The agent's reply was unusable: {e}", usages, who)
                return
            if question:
                total = self.add_tokens(t, usages)
                self.board.set_status(n, "Needs designer")
                self.board.comment(n, crew_comment(who, f"Question for the designer (attempt {attempt}):\n\n{question}"
                                                        f"\n\nUsed: {describe_tokens(usage)}. Task total: {total:,}.",
                                                   "needs_designer", attempt=attempt))
                log(who, f"#{n}: asked the designer, Needs designer")
                return
            stray = [p for p in written if p not in card.files]
            if stray:
                self.reject(t, attempt, "Wrote files outside the card: " + ", ".join(stray), usages, who)
                return
            for p, c in written.items():
                self.ws.write(p, c)
            sha = self.ws.commit(list(written), f"#{n} {t.title} - {who}, attempt {attempt}")
            link = self.ws.commit_link(sha) if sha else "no change from the last attempt"
            self.board.set_status(n, "Waiting for review")
            self.board.comment(n, crew_comment(who, f"Attempt {attempt} ({model}): {summary}\n\nCommit: {link}\n"
                                                    f"Used: {describe_tokens(usage)}.", "result", attempt=attempt,
                                               summary=summary[:500]))
        # ------------------------------------------------ review
        reviewer = AGENTS[card.reviewer]
        self.board.set_status(n, "In review")
        checks_ok, checks_text = self.run_checks(card, written)
        log(reviewer, f"#{n}: reviewing (checks {'pass' if checks_ok else 'FAIL'})")
        try:
            verdict, usage, _ = self.agents.review(card.reviewer, card.to_markdown(), summary, written, checks_text,
                                                   self.thread_text(self.board.issue(n)))
            usages.append(usage)
        except (AgentError, ValueError) as e:
            verdict = {"verdict": "reject", "summary": f"Review reply unusable ({e}).", "issues": []}
        issues = verdict.get("issues") or []
        lines = "\n".join(f"- {i.get('where', '')}: {i.get('problem', '')} Fix: {i.get('fix', '')}" for i in issues)
        approve = verdict.get("verdict") == "approve" and checks_ok
        if approve:
            total = self.add_tokens(t, usages)
            self.board.set_status(n, "Done")
            self.board.close(n)
            self.board.comment(n, crew_comment(reviewer, f"Approved: {verdict.get('summary', '')}\n\n{lines}\n\n"
                                                         f"Checks:\n{checks_text}\n\nTask total: {total:,} tokens.",
                                               "approved", attempt=attempt))
            log(reviewer, f"#{n}: approved, Done ({total:,} tokens)")
        else:
            reason = verdict.get("summary", "")
            if not checks_ok and verdict.get("verdict") == "approve":
                reason = "The reviewer approved, but a check failed, so it is rejected. " + reason
            self.reject(t, attempt, f"{reason}\n\n{lines}\n\nChecks:\n{checks_text}", usages, reviewer)

    def reject(self, t, attempt, text, usages, who):
        n = t.number
        total = self.add_tokens(t, usages)
        if attempt >= self.max:
            self.board.set_status(n, "Needs designer")
            self.board.comment(n, crew_comment(who, f"Rejected on attempt {attempt}/{self.max}: {text}\n\n"
                                                    f"This was the last attempt: Needs designer. Task total: {total:,} "
                                                    "tokens.", "needs_designer", attempt=attempt))
            log(who, f"#{n}: rejected on the last attempt, Needs designer")
        else:
            self.board.set_status(n, "Todo")
            self.board.comment(n, crew_comment(who, f"Rejected on attempt {attempt}/{self.max}: {text}\n\n"
                                                    f"Task total: {total:,} tokens.", "rejected", attempt=attempt))
            log(who, f"#{n}: rejected, back to Todo")

    # ------------------------------------------------------------ checks

    def run_checks(self, card, written):
        """Code decides these, not an agent: built-in checks on every written file, then the card's scripts."""
        out, ok = [], True
        for p, c in written.items():
            if c is None or not c.strip():
                out.append(f"- FAIL {p}: empty or missing")
                ok = False
                continue
            if "—" in c or "–" in c:
                out.append(f"- FAIL {p}: contains a long dash; use a plain '-'")
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
                res = subprocess.run([sys.executable, *args[1:]], cwd=self.ws.root, capture_output=True, text=True,
                                     encoding="utf-8", timeout=180)
                passed = res.returncode == 0
                detail = (res.stdout + res.stderr).strip()[-1500:]
            except subprocess.TimeoutExpired:
                passed, detail = False, "timed out"
            out.append(f"- {'pass' if passed else 'FAIL'} `{cmd}`" + (f"\n```\n{detail}\n```" if detail else ""))
            ok = ok and passed
        return ok, "\n".join(out) or "- no checks"

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
