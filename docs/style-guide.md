# Good Enough Repairs - style guide for the mechanic's lines

Every rule below is taken from the GDD (v1.6) or from an earlier assignment's review, and names its source. Nothing here is new: this page collects what the game already decided, so a loop can enforce it. The style loop (`agents/style_loop.py`) reads the rule table below directly, so this page and the loop never disagree.

## Who speaks

The mechanic: the player, and the only mechanic on the station. "Underpaid, resourceful, proud of it, and a little cheeky." The game is about the third, overlooked ship in space games: not the fighter, not the freighter, but the one who fixes both when they get stranded. Tagline: "They think they are important and scary. Wait till the breakdown to see what is scary, and wait for the fix to see who is important."

## The moments the mechanic speaks

From the GDD (Setting & theme, Docking bays, Player actions, Win/lose conditions, The secret strategy). Each line must do its moment's job.

| Moment | When it shows | What the line must do |
|---|---|---|
| day_start | A game day starts (the day type and its reason are shown separately) | Set the mood for the day's work; no day facts |
| release_warning | Before releasing a ship with an empty or too-weak slot; the warning shows the net result | Make clear the shop fills the slot at a premium, so buying the part yourself is cheaper; releasing stays the player's call |
| buy_warning | Before buying a brand-new part; the warning shows the price | Treat buying new as the expensive last resort, without spelling out the swap trick |
| penalty_fill | After a release filled slots with penalty-priced stock parts | Admit the job is done, but it cost more than it had to |
| day_end | The end-of-day summary | Close the day with pride in the work; the numbers are on screen, not in the line |
| bankruptcy | Credits below zero when the day ends: game over | Take the loss without self-pity; the mechanic stays proud |
| run_end_win | The run summary after surviving day 7 | Brag a little: the overlooked mechanic proved who is important |
| run_end_bankrupt | The run summary after a bankruptcy | Accept the loss with dignity and a grin; the tools are still there |
| fallback_hint | First time the balance drops below the day's starting balance, for players who skipped the tutorial | Point at a spare part that is actually on screen, cheekily, without explaining the rule |

The tutorial hint is fixed text in the GDD ("That engine is too weak for a fighter, but a freighter would take it.") and is never generated.

## Rules

Each rule is scored 0-10. A rule that limits what a line may say is fully followed when nothing in the line breaks it: the loop never asks for more of something a rule does not require (learned in the loop's first run, docs/assignment-6/release_warning-seed.md). The total is the weighted sum, out of 100. Rules judged by code are measured, so no agent can argue past them; the rest are judged by the Evaluator with a reason and the words that broke the rule.

| ID | Rule | Weight | Judged by | Source |
|---|---|---|---|---|
| V1 | Proud and cheeky: the voice grins, enjoys squeezing the most out of every part, and is never mean to the player | 15 | Evaluator | GDD, Setting & theme: "Tone: proud and cheeky" and "the game's voice in hints and messages has the same grin" |
| V2 | Underpaid and resourceful: nothing may sound rich, high-tech or wasteful; a line need not mention patching or saving to follow this rule | 10 | Evaluator | GDD, Setting & theme: "Underpaid, resourceful, proud of it" |
| V3 | The player is the only mechanic: no other mechanics, crew, boss, apprentice or successor | 10 | Evaluator | Assignment 4 review (#44): a bankrupt ending mentioned "the next mechanic" |
| M1 | The line does its moment's job, as in the moments table above | 15 | Evaluator | GDD: the moments in Docking bays, Player actions, Win/lose conditions |
| S1 | Never explain the swap trick: no telling the player to move parts between ships or naming the 60% and 70% levels; a nudge with a grin is fine | 15 | Evaluator | GDD, The secret strategy: "The game never explains the trick up front" |
| W1 | Only this game's world: fighters (battle damage), freighters (wear and tear), the static repair station with three bays, the junkyard, the bin, credits, and the five parts (engine, hull plate, shield generator, thruster, power core); no invented places, factions, aliens or tech | 10 | Evaluator | GDD, Setting & theme and Ships and parts |
| W2 | When a line shows the station, it is rugged and meticulously kept (worn metal, patched panels, every tool in its place), never shiny and never a mess; a line that does not show the station follows this rule | 5 | Evaluator | GDD, Art & audio direction: "Mood" |
| F1 | No digits: economy numbers live in the balance file and can change | 5 | Code | Assignment 4 content check: "no digits in text (economy numbers can change)" |
| F2 | No long dashes | 5 | Code | Project writing rule, enforced by the Assignment 4 content check |
| F3 | Short and plain: at most three sentences and 160 characters per line | 5 | Code | GDD, Art & audio direction: "readability matters more than detail"; Assignment 4 review: day briefs cut to one or two sentences (#44) |
| F4 | The asked number of lines, each different, with no generic sci-fi words (warp, hyperdrive, hyperspace, laser, droid, alien, empire, federation, starfleet, captain) | 5 | Code | GDD, Setting & theme (the game's own world); a line the player sees twice in a day wears out |

A fallback hint must say the spare is "on screen", never "in your inventory": the hint may point at a part on a docked ship (Assignment 4 review, #44). The Evaluator scores this under S1 and M1, and code checks it under F4.

## The bar

The loop stops at a total of 90 or more, with no rule below 6. Below that, the Refiner rewrites only what scored low and the Evaluator scores again, up to five rounds. Every round is logged with each rule's score, reason and evidence.
