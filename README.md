# Good Enough Repairs

A 2D management / repair puzzle game made in **Unity 6 (URP 2D)**. You are the space mechanic who keeps fighters and freighters flying. Ships dock with weak parts, and the winning move is not to buy new parts but to **swap** them: a fighter's 65% engine fails the fighter's 70% level, yet it is good enough for a freighter. Survive 7 days without going bankrupt.

Game design: [`docs/`](docs/) (Final GDD v1.3, PDF and Markdown). MVP cut order and checkpoints: [`docs/production-plan.md`](docs/production-plan.md).

This repository is the Unity project. The AI agents that help build it live next to the Unity folders in [`agents/`](agents/), outside `Assets/`, so Unity never imports them.

## The agent board

The agents' work is planned and tracked on a GitHub board, like Jira. The designer opens a **request** (issue template "Request to the Director"); the **Director** plans it as **epics** with one **task** per agent; the designer approves the tasks; the runner wakes each task's agent, runs the checks, has the task's reviewer approve or reject it, and moves the card. Agents talk through task comments. Each task shows **Attempt** (0/3 to 3/3) and **Used tokens**; after 3 rejections it goes to **Needs designer**. Each epic is one branch and one pull request the designer squash-merges. Full design: [`docs/agent-board-plan.md`](docs/agent-board-plan.md).

## How to run

Needs **Python 3.10+** (standard library only), the **Claude Code CLI** logged in (agents run through `claude -p` with your Claude login, no API key), and the **GitHub CLI** logged in: `winget install GitHub.cli`, then `gh auth login` and `gh auth refresh -s project`.

```bash
# from the project root
python agents/crew.py setup     # one time: labels, board, columns, fields
python agents/crew.py check     # is everything ready?
python agents/crew.py           # one pass: works until nothing is ready
python agents/crew.py --watch   # keeps running, checks the board every few minutes
python agents/crew.py --mock    # scripted demo: no GitHub, no Claude
```

Settings and toggles: [`agents/board/settings.json`](agents/board/settings.json) (`director_autoplan` and `autoapprove_tasks` off, `sound` on, by default). Close Unity while the runner works: it switches branches.

## Repository structure

```
Assets/                       Unity
Packages/  ProjectSettings/   Unity
.github/ISSUE_TEMPLATE/       request template for the Director
agents/
  crew.py                     agent board runner
  board/                      runner code, settings.json, roles/ (one prompt per agent)
docs/                         GDD, production plan, agent board plan
```

Author: Nikola Bulog - ELVTR AI Game Development course.
