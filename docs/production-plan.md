# Production plan - MVP (weeks 1-2)

The MVP plan from the GDD, with what to cut if the two weeks get tight. The rule: **the swap loop is the one thing to polish, so it is never cut.** Cut items are not deleted; they move to the top of the After the MVP list (week 3).

## Never cut (the swap loop)

- 3 docking bays with ship arrival, 2 ship types
- Condition bars against the required level
- Move, bin, buy and release (with penalty fill)
- 10-slot inventory and the junkyard
- Credits, upkeep, bankruptcy, end-of-day summary
- `economy.json` and the economy simulation (the agent crew)

## Cut order

| # | Cut first | What stays instead |
|---|---|---|
| 1 | Guided tutorial (day 0) | The fallback hint for players who skip it |
| 2 | Run end screen with the mechanic's closing line | The end-of-day summary after day 7 or bankruptcy |
| 3 | Day start screen's reason text | Day type and the junkyard button |
| 4 | Busy days (countdown) | Quiet days only; the crew retunes the ramp by ship count and weak parts |

## Checkpoints

- **End of week 1:** the swap loop is playable - parts move between 3 docked ships, release pays, credits update. If not, cut 1 and 2.
- **Middle of week 2:** a full 7-day run is playable with the junkyard and bankruptcy. If not, cut 3 and 4.
- **End of week 2:** MVP test - a new player plays 7 days, discovers swapping and wants to play again.

## Worst-case economy check

A bad day must stay recoverable. The agent crew checks this on every economy change: target 3 forces a worst-case day 4 (every ship with 3 weak parts, the junkyard all junk), and the swapper bot must still survive the run in 75%+ of runs. If a change breaks it, the file is not approved. See the [crew reports](../agents/reports/).
