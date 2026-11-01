# GDD excerpt for the economy crew

Source: docs/Good Enough Repairs - Final GDD v1.3 (sections Mechanics summary, Economy & balancing, Win/lose). The full GDD is in docs/.

## Mechanics summary

- 3 docking bays. Ships: fighters need every part at 70% or more, freighters at 60%. Parts are shared across ship types.
- Ships arrive with 1-3 weak parts (below the required level). The Economy Balancer sets the ramp.
- The player can move parts between docked ships (swap), store parts in a 10-slot inventory, bin parts for scrap, buy a new part (100%) for a slot, or release a ship. Releasing with weak or empty slots fills them at the penalty price.
- Junkyard: before each day, 15 parts are offered at one flat price, 7-10 of them junk. The mechanic finds more usable parts as the run goes on.
- The secret strategy: harvest good parts from one ship to fix another. A fighter's 65% engine fails the fighter but passes a freighter.

## Economy & balancing

The economy is tuned so that pure buying loses money and smart swapping makes money. That gap is the whole game.

| Item | Amount in credits  | Why |
|---|---|---|
| Starting credits | 500 | Enough to survive a few mistakes (the Economy Balancer may lower it, even to 0) |
| New part price | 150-300 | Higher than most single-ship payments |
| Fighter payment | 200-350 | Pays more, but needs 70% parts |
| Freighter payment | 150-250 | Pays less |
| Station upkeep | 120 per day | Constant pressure to keep earning |
| Scrap price (bin) | 10 per part | Small reward for clearing the inventory, never worth more than a swap |
| Junkyard part price | Same for every part: above 10, far below 150 | Cheap parts let salvaging and swapping beat buying, and binning them never pays |
| Penalty part price | New part price + 20% | Buying the part yourself is always cheaper than releasing an unfinished ship. Filling one weak part must cost more than the lowest ship payment |

Balance check to run in playtests: a player who only buys new parts should go bankrupt by about day 3. A player who swaps well should end each day with slightly more money than they started, also on the hardest days.

## Win/lose conditions & progression

- Tutorial: the first time the game starts, it asks "First time playing?" (tutorial) or "I know how to play". The tutorial is day 0: a guided quiet day with fixed ships and step-by-step instructions. It leads the player through swapping and can't be lost. There is no junkyard on day 0, since the player doesn't know yet what parts are for. After day 0 the credits and inventory are reset, and the real run starts at day 1. Players who skip it go straight to the main run.
- Structure: the game is played in game days (rounds). Each day is one of two kinds, and the start of the day tells the player which one and why. On a quiet day, a fixed number of ships (5-10) arrives, and the day ends when the last one flies away. On a busy day, more ships come than anyone could finish, so time is the only limit: ships keep arriving until a countdown (2-5 minutes) reaches 0, and the player tries to serve as many as possible. At 0 no new ships arrive, and the day ends when the last docked ship flies away, the same as on a quiet day. The two kinds are mixed, with more busy days later in the run. Upkeep is paid at the start of each day.
- No timer per ship: a docked ship waits as long as needed. The pressure comes from the day's limit and from money, not from individual ships leaving. Because a ship can always be released, every day can end.
- Money during the day: the credit counter changes live with every purchase, payment and scrap sale, and it may go below 0 and back up again. Only the balance at the end of the day counts: if it is below 0 then, the player is bankrupt. Holding the last ship back gains nothing, because the run only continues when the day ends.
- End of the run: after day 7, or after a bankruptcy, a final screen shows the run summary (credits earned, ships repaired, parts saved from the junkyard) and a closing line from the mechanic that depends on how the run went.
- Lose: credits are below 0 when the day ends → bankrupt, game over.
- Win: survive days 1-7. (Endless mode comes after the MVP.)
- Score: final credits minus starting credits.
- Difficulty ramp: as the mechanic's reputation grows, harder ships arrive, with more weak parts and lower conditions. Part prices stay fixed.
