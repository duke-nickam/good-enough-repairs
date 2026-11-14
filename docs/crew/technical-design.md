# Good Enough Repairs - Technical Design

Author: Programmer. Status: draft for the designer's approval.

## 1. The one rule

All game rules run in code. The whole game (7-day run, bays, parts, inventory, junkyard, actions, credits, day types, bankruptcy) plays as pure C# with no scene, graphic or sound. Scenes, graphics and sound only show the game and react to its events. They never decide anything.

This is what lets EditMode tests and the QA Playtester's bots play full runs in seconds with no one watching.

## 2. Code folders

All code is under `Assets/Scripts`, one folder per system (GDD: Engine integration).

| Folder | Holds | Status |
|---|---|---|
| `Economy/` | `BalanceData`, `BalanceLoader`, credits, prices, payments, penalty price, upkeep | `BalanceData.cs` and `BalanceLoader.cs` exist; the rest is new |
| `Content/` | `ContentData`, `ContentLoader`: mechanic lines, day briefs, part catalog | exists |
| `Parts/` | part type, condition, required level check, part generation | new |
| `Bays/` | ships, 3 docking bays, arrival schedule, release | new |
| `Inventory/` | the 10-slot shared inventory, bin | new |
| `Junkyard/` | the 15 parts of a day, salvage | new |
| `Run/` | the run, day flow, day types, bankruptcy, the player commands, the events | new |
| `Screens/` | screen logic only (what each screen shows as plain data, such as the release warning's net result) | new |

Scenes, prefabs, sprites and sound are not in `Scripts/`. They are the show layer (section 4).

`BalanceData` and `ContentData` are plain data and lookups, and every rule reads its numbers from them.

## 3. Pure logic and show layer

**Logic (the logic assembly):** everything under `Assets/Scripts`.
- No `MonoBehaviour`, no `GameObject`, no scene, UI, audio or sprite types, no `Time.time`, no `UnityEngine.Random`.
- The only UnityEngine use allowed is in the loaders (`JsonUtility`, `Application.streamingAssetsPath`, `Debug`). None of it needs a scene, so it works in EditMode.

**Show layer (the UX Designer's folder and prefabs):** scenes, prefabs, `MonoBehaviour`s that display state, animations, art and sound.
- It references the logic assembly. The logic assembly never references it.
- It reads state and calls the player commands (section 5). It holds no rules and no copy of the state.

## 4. Events and file separation

- The Programmer owns the logic code, the game's events and the scene files. The UX Designer owns its own folder and visual prefabs (GDD: Engine integration). Neither edits the other's files.
- Events are plain C# `event` delegates on the run and its systems, not `UnityEvent`. The UX layer subscribes from code in its own folder, so no event wiring lives in the logic files.
- An event fires after the state has changed, and carries the data a listener needs (for example the part and the credits it paid). Listeners must not change game state from inside an event. They call commands instead.
- Events are named from the GDD's moments, for example: part binned, part moved, part bought, part salvaged, ship docked, ship released (with net result and penalty fills), credits changed, day started, countdown reached 0, day ended, bankrupt, run ended. The exact list is in the code and is the contract with the UX Designer. A change to it is announced in the task summary.
- Every sound, effect and mechanic line (`ContentData.GetLine`) is triggered by an event. Nothing is triggered by polling the state.

## 5. Driving a run step by step

One `Run` object holds the whole game state. It has no hidden inputs.

- **Inputs:** a `BalanceData`, a `ContentData` and a seed. The same three give the same run.
- **Randomness:** one seeded `System.Random` is used for ships and the junkyard. Mechanic lines use a separate `Random` (`ContentData` already takes one), so choosing a line never changes the game's outcome.
- **Time:** the run has its own clock in milliseconds, moved only by `Advance(ms)`. The show layer calls it with real frame time. A bot or test calls it with the balance file's `playerActionMs`, `shipTurnoverMs` and `dayStartLandingMs`. Nothing reads real time.
- **Commands:** one method per player action (GDD table): move part (swap on occupied slot, snap back on full inventory), salvage, bin, buy, release, plus start the day and continue. Each returns a result with a reason when refused. Nothing throws on a bad move.
- **Previews:** read-only methods give the numbers behind the warnings: the buy price and whether the old part would be binned, and the release net result (payment minus penalty parts). The UI shows these, never works them out itself.
- **Day flow:** day start (brief from `day_briefs.json`, junkyard open) then play, then day end summary, then the next day or run end. Upkeep is charged at the start of the day. Bankruptcy is checked only at day end. A day ends when no ship is left to arrive or docked. A ship can always be released.
- **Reading state:** bays, ships, parts, inventory, junkyard, credits, clock and the day's countdown or ships left, all as read-only views.

A bot loop is: create the run, then repeat: read state, call commands, `Advance`, until the run ends. `agents/checks/simulate.py` is the reference for the rules and the time model, and the C# must give the same results for the same rules (including its rounding of the penalty price).

## 6. Loading balance.json and content

- Files: `Assets/StreamingAssets/balance.json` and `Assets/StreamingAssets/content/` (`mechanic_lines.json`, `day_briefs.json`, `part_catalog.json`).
- **Game start:** `BalanceLoader.Load()` and `ContentLoader.Load()` read the files. They never throw and log errors. `BalanceLoader.Load()` returns null on a bad file, so the start code must stop with a clear message. `ContentLoader.Load()` always returns usable content (empty text for what is missing).
- **Tests and bots:** read the file text themselves and call `BalanceLoader.Parse(json, out error)` and `ContentLoader.ParseLines/ParseBriefs/ParseParts`. These do not log and need no `Application` path. A test can also build `BalanceData` or `ContentData` directly in code.
- **Run creation:** one factory takes `BalanceData`, `ContentData` and a seed and returns a `Run`. The game start, the tests and the bots all use it.
- **Validation:** `BalanceLoader.Parse` only checks that required objects exist. The full balance rules are checked by `agents/checks/simulate.py`. The run does not re-check them.
- JSON stays readable by `JsonUtility`: `[Serializable]` classes, arrays, no dictionaries, no top-level arrays.
- No balance number or player text is hard-coded. Fixed GDD rules (3 bays, 10 inventory slots, 15 junkyard parts, 7 days) are named constants in one place.

## 7. Assemblies and tests

Proposed assembly definitions:

| Assembly | Contains | References |
|---|---|---|
| `GoodEnoughRepairs.Logic` | everything in `Assets/Scripts` | none (UnityEngine core only for the loaders) |
| `GoodEnoughRepairs.Show` | UX Designer's scripts and prefabs | Logic |
| `GoodEnoughRepairs.Tests.EditMode` | EditMode tests | Logic (test-only, not in the build) |
| `GoodEnoughRepairs.Bots` | the QA Playtester's buyer and swapper bots | Logic |

- The Logic assembly cannot see the Show assembly, so a rule cannot depend on a scene.
- EditMode tests and bots call the same factory and commands as the game. Neither needs a scene, play mode, or any asset besides the JSON files.
- Tests can check the GDD rules directly: release always possible, penalty fill price, buy button only for empty or weak slots, inventory full snaps back, binned part pays once, credits add up, same seed gives the same run.

## 8. Open questions for the designer

1. **Ships still waiting when the busy-day countdown reaches 0.** The GDD says no new ships arrive and docked ships are finished. The simulation assumes ships already arrived but not docked leave unpaid, and marks this as unconfirmed. Should the game do the same?
2. **Slots per ship.** The GDD says five part slots (engine, hull plate, shield generator, thruster, power core), and MVP scope says 4-5. `balance.json` has `slotsPerShip` 5. Is it always the five named types in the MVP?
3. **Folder and assembly names.** Where does the UX Designer's folder live, and where do the bots live (inside the Unity project or in a separate test project)? The tables above are a proposal.
4. **UnityEngine in the loaders.** The loaders use `JsonUtility` and `Debug`, so the Logic assembly keeps a UnityEngine core reference. Is that acceptable, or should the loaders move into their own assembly so Logic has no Unity reference at all?
5. **Saving.** The GDD asks "First time playing?" on first start, which needs a stored flag. It does not say whether a run can be saved or resumed. Should there be any saving beyond that one flag?
6. **Tutorial and hints.** Day 0 (fixed ships, no junkyard, reset afterwards) and the fallback hint are not yet specified for code. Should they be one card after the core run works?
