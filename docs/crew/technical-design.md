# Good Enough Repairs - Technical Design

Author: Programmer. Status: updated with the designer's answers, for approval.

## 1. The one rule

All game rules run in code. The whole game (7-day run, bays, parts, inventory, junkyard, actions, credits, day types, bankruptcy) plays as pure C# with no scene, graphic or sound. Scenes, graphics and sound only show the game and react to its events. They never decide anything.

This lets EditMode tests and the QA Playtester's bots play full runs in seconds with no one watching.

## 2. Folders and assemblies

All code is under `Assets/Scripts`, one subfolder per assembly. Each assembly has its own assembly definition.

| Folder | Assembly | Holds | References |
|---|---|---|---|
| `Logic/` | `GoodEnoughRepairs.Logic` (`noEngineReferences` on) | all rules, one subfolder per system (below) | nothing |
| `Data/` | `GoodEnoughRepairs.Data` | the loaders: `BalanceLoader`, `ContentLoader` | Logic, UnityEngine |
| `UX/` | `GoodEnoughRepairs.UX` | the UX Designer's scripts (show layer) | Logic, Data |
| `Bots/` | `GoodEnoughRepairs.Bots` | the QA Playtester's buyer and swapper bots | Logic, Data |
| `Tests/EditMode/` | `GoodEnoughRepairs.Tests.EditMode` | EditMode tests (not in the build) | Logic, Data |

The namespace stays `GoodEnoughRepairs` in every folder. `UX/` is outside `Logic/`, so the boundary is real: Logic cannot see UX, Data, a scene or any UnityEngine type.

### Logic subfolders (GDD: bays, parts, inventory, economy, screens)

| Subfolder | Holds | Status |
|---|---|---|
| `Logic/Economy/` | `BalanceData`, credits, prices, payments, penalty price, upkeep | `BalanceData.cs` is here; the rest is new |
| `Logic/Content/` | `ContentData` and its file classes: mechanic lines, day briefs, part catalog | `ContentData.cs` is here |
| `Logic/Parts/` | part type, condition, required level check, part generation | new |
| `Logic/Bays/` | ships, 3 docking bays, arrival schedule, release | new |
| `Logic/Inventory/` | the 10-slot shared inventory, bin | new |
| `Logic/Junkyard/` | the 15 parts of a day, salvage | new |
| `Logic/Run/` | the run, day flow, day types, bankruptcy, player commands, events | new |
| `Logic/Screens/` | what each screen shows as plain data (for example the release warning's net result) | new |

The loaders `BalanceLoader.cs` and `ContentLoader.cs` live in `Data/`. They are the only code that uses `JsonUtility`, `Application`, `File` and `Debug`. `BalanceData` and `ContentData` are plain `[Serializable]` classes (System only), so they stay in Logic.

Scenes, prefabs, sprites and sound are not code. They are the show layer.

## 3. Logic and show layer

**Logic:** no `MonoBehaviour`, `GameObject`, scene, UI, audio or sprite types, no `Time.time`, no `UnityEngine.Random`, no `UnityEngine` at all. Randomness is `System.Random`.

**Show layer (`UX/`, scenes, prefabs):** `MonoBehaviour`s that display state, animations, art and sound.
- It references Logic. Logic never references it.
- It reads state and calls player commands (section 5). It holds no rules and no copy of the state.
- The first-time-playing flag is saved with `PlayerPrefs` by the show layer. There is no other saving in the MVP.

## 4. Events and file separation

- As in the GDD's Engine integration: the Programmer owns the game's events and the scene files. The UX Designer's feedback only listens to those events from its own folder and visual prefabs. Neither edits the other's files.
- Events are plain C# `event` delegates on the run and its systems, not `UnityEvent`. The UX layer subscribes from code in `UX/`, so no event wiring lives in Logic.
- An event fires after the state has changed and carries what a listener needs (for example the part and the credits it paid). Listeners must not change state from inside an event. They call commands instead.
- Events are named from the GDD's moments, for example: part binned, part moved, part bought, part salvaged, ship docked, ship released (with net result and penalty fills), credits changed, day started, countdown reached 0, day ended, bankrupt, run ended. The exact list is in the code and is the contract with the UX Designer. A change to it is announced in the task summary.
- Every sound, effect and mechanic line (`ContentData.GetLine`) is triggered by an event. Nothing is triggered by polling.

## 5. Driving a run step by step

One `Run` object holds the whole game state. It has no hidden inputs.

- **Inputs:** a `BalanceData`, a `ContentData` and a seed. The same three give the same run.
- **Randomness:** separate `System.Random` streams are derived from the seed: one for ships, one for the junkyard. Mechanic lines use their own `Random` (`ContentData` already takes one), so choosing a line never changes the game's outcome.
- **Time:** the run has its own clock in milliseconds, moved only by `Advance(ms)`. The show layer calls it with real frame time. A bot or test calls it with `playerActionMs`, `shipTurnoverMs` and `dayStartLandingMs` from `balance.json`. Nothing reads real time.
- **Ship arrivals:** a ship is created just before it enters the screen, never earlier. Waiting ships are not visible to the player and do not exist in the state. On a busy day, arrival times follow `shipEverySeconds`; when the countdown reaches 0, ships that have not yet entered never come and pay nothing. Docked ships are finished, and the day ends when the last one is released.
- **Commands:** one method per player action (GDD table): move part (swap on an occupied slot, snap back on a full inventory), salvage, bin, buy, release, plus start the day and continue. Each returns a result with a reason when refused. Nothing throws on a bad move.
- **Previews:** read-only methods give the numbers behind the warnings: the buy price and whether the old part would be binned, and the release net result (payment minus penalty parts). The UI shows these and never works them out itself.
- **Day flow:** day start (brief from `day_briefs.json`, junkyard open), play, day end summary, then the next day or run end. Upkeep is charged at the start of the day. Bankruptcy is checked only at day end. A ship can always be released.
- **Reading state:** bays, ships, parts, inventory, junkyard, credits, clock and the day's countdown or ships left, as read-only views.

A bot loop: create the run, then repeat read state, call commands, `Advance`, until the run ends. `agents/checks/simulate.py` is the reference for the rules and the time model. The C# must give the same results for the same rules, including its rounding of the penalty price (the shop price times 120%, rounded half up).

## 6. Loading balance.json and content

- Files: `Assets/StreamingAssets/balance.json` and `Assets/StreamingAssets/content/` (`mechanic_lines.json`, `day_briefs.json`, `part_catalog.json`).
- **Game start:** `BalanceLoader.Load()` and `ContentLoader.Load()` (in Data) read the files. They never throw and log errors. `BalanceLoader.Load()` returns null on a bad file, so the start code must stop with a clear message. `ContentLoader.Load()` always returns usable content (empty text for what is missing).
- **Tests and bots:** read the file text themselves and call `BalanceLoader.Parse(json, out error)` and `ContentLoader.ParseLines/ParseBriefs/ParseParts`. These do not log and need no `Application` path or scene. A test can also build `BalanceData` or `ContentData` directly in code, with no Data reference needed for that.
- **Run creation:** one factory in `Logic/Run/` takes `BalanceData`, `ContentData` and a seed and returns a `Run`. The game start, tests and bots all use it.
- **Validation:** `BalanceLoader.Parse` only checks that required objects exist. The full balance rules are checked by `agents/checks/simulate.py`. The run does not re-check them.
- JSON stays readable by `JsonUtility`: `[Serializable]` classes, arrays, no dictionaries, no top-level arrays.
- No balance number or player text is hard-coded. Fixed GDD rules (3 bays, 10 inventory slots, 15 junkyard parts, 7 days, the five part types engine, hull plate, shield generator, thruster and power core) are named constants in one place in Logic.

## 7. Tests and bots

- EditMode tests and bots call the same factory and commands as the game. Neither needs a scene, play mode or any asset besides the JSON files.
- Tests check the GDD rules directly: release always possible, penalty fill price, buy button only for empty or weak slots, full inventory snaps back, binned part pays once, credits add up, ships that have not entered at countdown 0 never come, same seed gives the same run.

## 8. Open questions for the designer

1. **Scenes and prefabs folder.** The GDD says the Programmer owns the scene files and the UX Designer owns visual prefabs, but not where they live. Should they sit outside `Assets/Scripts` (for example `Assets/Scenes` and `Assets/Prefabs`, with the UX Designer's prefabs in a `UX` subfolder)?

   **Answer (designer):** Scenes stay in `Assets/Scenes` (`Main.unity`). There are no prefabs for now: the show layer in `Assets/Scripts/UX/` builds placeholder visuals in code at runtime. Agents do not edit scene or prefab files. The designer adds the show layer's entry component to `Main.unity` by hand. When real art comes, prefabs go in `Assets/Prefabs/UX`.
2. **How bots run.** The bots live in the Unity project in `Scripts/Bots/`. Should they run as EditMode tests, or from a batch-mode menu command? Either works without a scene.
