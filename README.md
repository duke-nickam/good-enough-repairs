# Good Enough Repairs

A 2D management / repair puzzle game made in **Unity 6 (URP 2D)**. You are the space mechanic who keeps fighters and freighters flying. Ships dock with weak parts, and the winning move is not to buy new parts but to **swap** them: a fighter's 65% engine fails the fighter's 70% level, yet it is good enough for a freighter. Survive 7 days without going bankrupt.

Game design: [the GDD](docs/Good%20Enough%20Repairs%20-%20GDD.md). MVP cut order and checkpoints: [`docs/production-plan.md`](docs/production-plan.md).

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

## Assignment 4: game content from the GDD

Request [#16](https://github.com/duke-nickam/good-enough-repairs/issues/16) asked the Director for three content outputs from the GDD. It planned epic [#35](https://github.com/duke-nickam/good-enough-repairs/issues/35), merged as PR [#41](https://github.com/duke-nickam/good-enough-repairs/pull/41). A polish request, [#42](https://github.com/duke-nickam/good-enough-repairs/issues/42), followed as epic [#43](https://github.com/duke-nickam/good-enough-repairs/issues/43), PR [#45](https://github.com/duke-nickam/good-enough-repairs/pull/45). Everything ran on the [agent board](https://github.com/users/duke-nickam/projects/1).

**What was generated**, in `Assets/StreamingAssets/content/`:

| File | Agent | Content |
|---|---|---|
| `mechanic_lines.json` | UX Designer (#37, #44) | 3 lines for each moment the GDD says the mechanic speaks (day start, release warning, penalty fill, end of day, bankruptcy, run end win and bankrupt), the GDD's exact tutorial hint, 3 fallback hints |
| `day_briefs.json` | UX Designer (#38, #44) | Days 1-7: the day type copied from `balance.json` and a two-sentence reason |
| `part_catalog.json` | UX Designer (#39) | Name and description for the GDD's five part types |

The epic also produced the content check `agents/checks/content.py` (QA Playtester, #36) and the loader in `Assets/Scripts/Content/` (Programmer, #40). The epic used about 136k tokens and the polish about 53k. Every task passed on attempt 1.

**Does it sound like the game?** Mostly yes. The lines are short, proud and cheeky, and they never give away the swap trick before the hint:

> "Every ship flying home on a patched part. Best feeling in the galaxy, and I earned it the hard way." (run end, win)
>
> "That spare on screen still has some life in it. Shame to waste it on the scrap heap." (fallback hint)
>
> "Patched, dented and still holding the air in, which is more than the factory ever promised for this one." (hull plate)

The day briefs were the weakest part: too long until the polish.

**What the critic caught.** Two critics run on every content task. The check script enforces the rules code can decide: valid JSON, no digits in text (economy numbers can change), no long dashes, LF line endings, exact keys, day types equal to `balance.json`. The Director reviews against the GDD:

- Fallback hints said the spare part is "in your inventory". The GDD only says "on screen", so it can be on a docked ship. Fixed in #44.
- A bankrupt ending mentioned "the next mechanic", but the player is the only mechanic. Fixed in #44.
- The day briefs had 3-4 sentences instead of 1-2, and day 3 didn't say why it's quiet. Fixed in #44.
- Left as they are: a day start line close to hinting the swap trick, the engine description, and day 7 saying time is "the only thing that matters" (money still decides bankruptcy).

**What the loop taught the system.** In the first plans, each content task's check read the whole content folder. Tasks then failed on their siblings' missing files and used up all 3 attempts. The fixes went into the runner, not the content:

- a plan rule: every check names its files, and a task depends on every task whose files it uses;
- an early stop when a check fails exactly like the try before;
- the Director asks instead of guessing what a file holds. That's how the part types got defined in GDD v1.4.

The request was planned again from scratch until the plan passed.

Author: Nikola Bulog - ELVTR AI Game Development course.
