#!/usr/bin/env python3
"""Buyer / swapper / releaser simulation for Good Enough Repairs.

Usage:
    python agents/checks/simulate.py BALANCE_JSON [--games N] [--seed S]
                                     [--max-daily-net N]

Plays the 7 game days with three bots on the same seeded worlds, prints a
result table and checks the balance targets from the GDD. Exit code 0 only
if the balance file is valid and no target fails; 1 otherwise. A target
printed as SKIP is not checked (see --max-daily-net). Pure standard library.
The same file, --games, --seed and --max-daily-net give the same output.

BALANCE FILE FORMAT (JSON, must be readable by Unity's JsonUtility)
- One object at the top level, never an array.
- No maps with named keys: the day list is an array of objects.
- Percentages are whole numbers 0-100. Money is whole credits.
  Time is whole seconds.

Top-level fields:
  startingCredits            int     starting credits (GDD: 500, may be lowered)
  newPartPrice               int     shop price of a new part (GDD: 150-300)
  penaltyPercent             int     a penalty fill costs this % above newPartPrice
                                     (GDD: 20, range 0-100)
  scrapPrice                 int     paid per binned part (GDD: 10)
  upkeep                     int     paid at the start of each day (GDD: 120)
  actionSeconds              int     game seconds one player action takes: a buy,
                                     a part move (swap or fill) or a release.
                                     The GDD gives no value; the designer must set
                                     it. Range 1-3600.
  slotsPerShip               int     part slots per ship, 4-5 (GDD MVP scope)
  requiredFreighterPercent   int     required level for freighters (GDD: 60)
  requiredFighterPercent     int     required level for fighters (GDD: 70)
  freighterPayment           object  {"min": int, "max": int} (GDD: 150-250)
  fighterPayment             object  {"min": int, "max": int} (GDD: 200-350)
  junkyard                   object  see below
  days                       array   exactly 7 day objects, day 1 first

junkyard object:
  price                      int     paid per salvaged part. Above scrapPrice,
                                     below newPartPrice
  usableConditionMin         int     lowest condition of a usable junkyard part
                                     (at least requiredFreighterPercent)
  usableConditionMax         int     highest condition of a usable junkyard part
                                     (at most 100)

day object (every field is required; unused fields are 0):
  day                        int     1-7, counting up from 1
  type                       string  "quiet" or "busy"
  ships                      int     quiet day: ships in the day (GDD: 5-10).
                                     Busy day: must be 0 (the count comes from
                                     the countdown)
  countdownSeconds           int     busy day: countdown (GDD: 120-300).
                                     Quiet day: must be 0
  shipEverySeconds           int     busy day: seconds between arrivals, at least
                                     1 and at most countdownSeconds. Quiet day:
                                     must be 0
  junkParts                  int     unusable junk among the 15 junkyard parts
                                     (GDD: 7-10)
  weakPartsMin               int     weak parts per ship, lowest (GDD: 1)
  weakPartsMax               int     weak parts per ship, highest (GDD: 3)
  weakConditionMin           int     weak part condition, lowest
  weakConditionMax           int     weak part condition, highest (below
                                     requiredFreighterPercent)
  healthyConditionMin        int     other part condition, lowest (at least
                                     requiredFighterPercent)
  healthyConditionMax        int     other part condition, highest (at most 100)

Arrival rules used by the sim:
- Quiet day: all ships are waiting at the day start. No time limit.
- Busy day: a ship arrives at 0, shipEverySeconds, 2 * shipEverySeconds, ...
  while the time is below countdownSeconds. Ship count is
  ceil(countdownSeconds / shipEverySeconds).
- When the countdown reaches 0 no new ships arrive. Ships still waiting
  outside a bay then leave unpaid (turned away). Docked ships are finished,
  and the day ends when the last docked ship flies away.

Modelling notes (the sim is a QA tool, not part of the game):
- Fixed GDD rules in the sim: 3 bays, 10 inventory slots, 15 junkyard parts,
  bankruptcy checked only at day end, 7 days.
- Each buy, part move and release takes actionSeconds of game time.
  Salvage at the day start takes no time. Binning inside a move and the
  penalty fills inside a release take no extra time.
- A junk junkyard part has condition 0. Conditions are whole percents.
- The releaser discards the parts it replaces (no payment, no scrap).
- The swapper buys a new part only when no harvest is possible, so buying
  stays the last resort.
"""

import argparse
import copy
import json
import random
import statistics
import sys

BAYS = 3
INVENTORY_SLOTS = 10
JUNKYARD_SIZE = 15
DAY_COUNT = 7
JUNK_MAX = 10           # GDD: 7-10 junk parts; the bad-luck day uses the most
BUYER_TARGET_DAY = 3    # GDD: a buy-only player goes bankrupt by about day 3
QUIET_SHIPS = (5, 10)   # GDD: quiet day ship count
BUSY_COUNTDOWN = (120, 300)  # GDD: 2-5 minutes
JUNK_RANGE = (7, 10)    # GDD: junk parts per junkyard
WEAK_RANGE = (1, 3)     # GDD: weak parts per ship
SLOT_RANGE = (4, 5)     # GDD MVP scope: 4-5 part slots
PENALTY_PERCENT_RANGE = (0, 100)
ACTION_SECONDS_RANGE = (1, 3600)  # sanity bound only; the value is the designer's
NEW_CONDITION = 100     # a new part is 100%

DAY_INT_FIELDS = [
    ("ships", 0, 10),
    ("countdownSeconds", 0, 3600),
    ("shipEverySeconds", 0, 3600),
    ("junkParts", 0, JUNKYARD_SIZE),
    ("weakPartsMin", 0, 5),
    ("weakPartsMax", 0, 5),
    ("weakConditionMin", 0, 100),
    ("weakConditionMax", 0, 100),
    ("healthyConditionMin", 0, 100),
    ("healthyConditionMax", 0, 100),
]


# ---------------------------------------------------------------- validation

def _field(obj, key, kind, where, errors):
    name = where + key
    if not isinstance(obj, dict) or key not in obj:
        errors.append("missing field: " + name)
        return None
    val = obj[key]
    if kind == "int":
        ok = isinstance(val, int) and not isinstance(val, bool)
    elif kind == "str":
        ok = isinstance(val, str)
    elif kind == "obj":
        ok = isinstance(val, dict)
    else:
        ok = isinstance(val, list)
    if not ok:
        errors.append("wrong type for %s: expected %s" % (name, kind))
        return None
    return val


def _int(obj, key, where, errors, lo, hi):
    val = _field(obj, key, "int", where, errors)
    if val is None:
        return None
    if not lo <= val <= hi:
        errors.append("out of range for %s%s: %d (allowed %d to %d)"
                      % (where, key, val, lo, hi))
        return None
    return val


def _payment(data, key, errors):
    obj = _field(data, key, "obj", "", errors)
    if obj is None:
        return None
    lo = _int(obj, "min", key + ".", errors, 0, 10 ** 9)
    hi = _int(obj, "max", key + ".", errors, 0, 10 ** 9)
    if lo is None or hi is None:
        return None
    if lo > hi:
        errors.append("%s: min is above max" % key)
        return None
    return (lo, hi)


def _junkyard(data, errors):
    obj = _field(data, "junkyard", "obj", "", errors)
    if obj is None:
        return None
    price = _int(obj, "price", "junkyard.", errors, 0, 10 ** 9)
    lo = _int(obj, "usableConditionMin", "junkyard.", errors, 0, 100)
    hi = _int(obj, "usableConditionMax", "junkyard.", errors, 0, 100)
    if price is None or lo is None or hi is None:
        return None
    return {"price": price, "usableConditionMin": lo, "usableConditionMax": hi}


def _days(data, errors):
    entries = _field(data, "days", "list", "", errors)
    if entries is None:
        return None
    if len(entries) != DAY_COUNT:
        errors.append("days must have exactly %d entries (found %d)"
                      % (DAY_COUNT, len(entries)))
        return None
    days = []
    for n, entry in enumerate(entries):
        where = "days[%d]." % n
        if not isinstance(entry, dict):
            errors.append("days[%d] must be an object" % n)
            days.append(None)
            continue
        day = {"day": _int(entry, "day", where, errors, 1, DAY_COUNT)}
        kind = _field(entry, "type", "str", where, errors)
        if kind is not None and kind not in ("quiet", "busy"):
            errors.append('wrong value for %stype: use "quiet" or "busy"' % where)
            kind = None
        day["type"] = kind
        for key, lo, hi in DAY_INT_FIELDS:
            day[key] = _int(entry, key, where, errors, lo, hi)
        days.append(day)
    if any(d is None or any(v is None for v in d.values()) for d in days):
        return None
    return days


def _logic_errors(s):
    errs = []
    req_f = s["requiredFreighterPercent"]
    req_x = s["requiredFighterPercent"]
    for n, d in enumerate(s["days"]):
        where = "day %d: " % (n + 1)
        if d["day"] != n + 1:
            errs.append("days[%d].day must be %d" % (n, n + 1))
        if d["type"] == "quiet":
            if not QUIET_SHIPS[0] <= d["ships"] <= QUIET_SHIPS[1]:
                errs.append(where + "quiet day ships must be %d-%d (GDD)" % QUIET_SHIPS)
            if d["countdownSeconds"] != 0 or d["shipEverySeconds"] != 0:
                errs.append(where + "quiet day countdownSeconds and shipEverySeconds "
                            "must be 0")
        else:
            if d["ships"] != 0:
                errs.append(where + "busy day ships must be 0 (the count comes from "
                            "the countdown)")
            if not BUSY_COUNTDOWN[0] <= d["countdownSeconds"] <= BUSY_COUNTDOWN[1]:
                errs.append(where + "busy day countdownSeconds must be %d-%d (GDD)"
                            % BUSY_COUNTDOWN)
            if not 1 <= d["shipEverySeconds"] <= d["countdownSeconds"]:
                errs.append(where + "busy day shipEverySeconds must be at least 1 and "
                            "at most countdownSeconds")
        if not JUNK_RANGE[0] <= d["junkParts"] <= JUNK_RANGE[1]:
            errs.append(where + "junkParts must be %d-%d (GDD)" % JUNK_RANGE)
        if not WEAK_RANGE[0] <= d["weakPartsMin"] <= d["weakPartsMax"] <= WEAK_RANGE[1]:
            errs.append(where + "weakPartsMin/Max must be ordered and within %d-%d (GDD)"
                        % WEAK_RANGE)
        if d["weakPartsMax"] > s["slotsPerShip"]:
            errs.append(where + "weakPartsMax cannot exceed slotsPerShip")
        if d["weakConditionMin"] > d["weakConditionMax"] or d["weakConditionMax"] >= req_f:
            errs.append(where + "weak conditions must be ordered and below "
                        "requiredFreighterPercent")
        if d["healthyConditionMin"] > d["healthyConditionMax"] or d["healthyConditionMin"] < req_x:
            errs.append(where + "healthy conditions must be ordered and at least "
                        "requiredFighterPercent")
    jy = s["junkyard"]
    if jy["usableConditionMin"] > jy["usableConditionMax"] or jy["usableConditionMin"] < req_f:
        errs.append("junkyard: usable conditions must be ordered and at least "
                    "requiredFreighterPercent")
    return errs


def validate_balance(data):
    errors = []
    if not isinstance(data, dict):
        return None, ["top level must be one JSON object"]
    s = {}
    s["startingCredits"] = _int(data, "startingCredits", "", errors, 0, 10 ** 9)
    s["newPartPrice"] = _int(data, "newPartPrice", "", errors, 1, 10 ** 9)
    s["penaltyPercent"] = _int(data, "penaltyPercent", "", errors,
                               PENALTY_PERCENT_RANGE[0], PENALTY_PERCENT_RANGE[1])
    s["scrapPrice"] = _int(data, "scrapPrice", "", errors, 0, 10 ** 9)
    s["upkeep"] = _int(data, "upkeep", "", errors, 0, 10 ** 9)
    s["actionSeconds"] = _int(data, "actionSeconds", "", errors,
                              ACTION_SECONDS_RANGE[0], ACTION_SECONDS_RANGE[1])
    s["slotsPerShip"] = _int(data, "slotsPerShip", "", errors, SLOT_RANGE[0], SLOT_RANGE[1])
    s["requiredFreighterPercent"] = _int(data, "requiredFreighterPercent", "", errors, 0, 100)
    s["requiredFighterPercent"] = _int(data, "requiredFighterPercent", "", errors, 0, 100)
    s["freighterPayment"] = _payment(data, "freighterPayment", errors)
    s["fighterPayment"] = _payment(data, "fighterPayment", errors)
    s["junkyard"] = _junkyard(data, errors)
    s["days"] = _days(data, errors)
    if errors:
        return None, errors
    errors = _logic_errors(s)
    if errors:
        return None, errors
    return s, []


# ---------------------------------------------------------------- world

def penalty_price(cfg):
    return (cfg["newPartPrice"] * (100 + cfg["penaltyPercent"]) + 50) // 100


def arrival_times(day):
    """Arrival time in seconds of every ship on the day, in order."""
    if day["type"] == "quiet":
        return [0] * day["ships"]
    return list(range(0, day["countdownSeconds"], day["shipEverySeconds"]))


def make_ship(cfg, day, rng):
    kind = rng.choice(["fighter", "freighter"])
    if kind == "fighter":
        low, high = cfg["fighterPayment"]
    else:
        low, high = cfg["freighterPayment"]
    slots = [rng.randint(day["healthyConditionMin"], day["healthyConditionMax"])
             for _ in range(cfg["slotsPerShip"])]
    weak_count = rng.randint(day["weakPartsMin"], day["weakPartsMax"])
    for i in rng.sample(range(len(slots)), weak_count):
        slots[i] = rng.randint(day["weakConditionMin"], day["weakConditionMax"])
    return {"kind": kind, "payment": rng.randint(low, high), "slots": slots}


def build_world(cfg, days, seed):
    rng = random.Random(seed)
    jy = cfg["junkyard"]
    world = []
    for day in days:
        arrivals = [(t, make_ship(cfg, day, rng)) for t in arrival_times(day)]
        usable = JUNKYARD_SIZE - day["junkParts"]
        yard = [0] * day["junkParts"]
        yard += [rng.randint(jy["usableConditionMin"], jy["usableConditionMax"])
                 for _ in range(usable)]
        rng.shuffle(yard)
        world.append({"arrivals": arrivals, "junkyard": yard})
    return world


# ---------------------------------------------------------------- game rules

def best_index(values, need):
    """Index of the smallest value that is at least need, or None."""
    best = None
    for k, v in enumerate(values):
        if v >= need and (best is None or v < values[best]):
            best = k
    return best


class Game:
    def __init__(self, cfg, credits):
        self.cfg = cfg
        self.credits = credits
        self.penalty = penalty_price(cfg)
        self.action_seconds = cfg["actionSeconds"]
        self.bays = [None] * BAYS
        self.inventory = []
        self.queue = []       # arrived ships waiting for a bay, in arrival order
        self.future = []      # (arrival time, ship) not yet arrived
        self.countdown = None
        self.closed = False
        self.clock = 0
        self.expected_weak = 0.0
        self.repaired = 0
        self.released = 0
        self.salvaged = 0
        self.binned = 0
        self.turned_away = 0

    def start_day(self, day, arrivals, expected_weak):
        self.clock = 0
        self.countdown = day["countdownSeconds"] if day["type"] == "busy" else None
        self.closed = False
        self.future = list(arrivals)
        self.queue = []
        self.expected_weak = expected_weak
        self._admit()

    def req(self, ship):
        if ship["kind"] == "fighter":
            return self.cfg["requiredFighterPercent"]
        return self.cfg["requiredFreighterPercent"]

    def lowest_req(self):
        return min(self.cfg["requiredFreighterPercent"],
                   self.cfg["requiredFighterPercent"])

    def weak_slots(self, ship):
        need = self.req(ship)
        return [i for i, c in enumerate(ship["slots"]) if c < need]

    def docked(self):
        return [s for s in self.bays if s is not None]

    def day_over(self):
        return not self.future and not self.queue and not self.docked()

    def _fill(self):
        for i in range(BAYS):
            if self.bays[i] is None and self.queue:
                self.bays[i] = self.queue.pop(0)

    def _admit(self):
        while self.future and self.future[0][0] <= self.clock:
            self.queue.append(self.future.pop(0)[1])
        self._fill()

    def _advance(self, t):
        closing = False
        if self.countdown is not None and not self.closed and t >= self.countdown:
            t = self.countdown
            closing = True
        self.clock = t
        self._admit()
        if closing:
            # The countdown reached 0: no new ships, and ships still waiting
            # outside a bay leave unpaid. Docked ships are finished.
            self.closed = True
            self.turned_away += len(self.queue) + len(self.future)
            self.queue = []
            self.future = []

    def act(self):
        """One player action (buy, move or release) takes game time."""
        self._advance(self.clock + self.action_seconds)

    def idle(self):
        """Nothing to do: wait for the next ship to arrive."""
        if not self.future:
            raise RuntimeError("idle with no ship to wait for")
        self._advance(self.future[0][0])

    def bin(self, part):
        self.credits += self.cfg["scrapPrice"]
        self.binned += 1

    def put_removed(self, part):
        if len(self.inventory) < INVENTORY_SLOTS:
            self.inventory.append(part)
        else:
            self.bin(part)

    def buy(self, ship, i):
        self.credits -= self.cfg["newPartPrice"]
        old = ship["slots"][i]
        ship["slots"][i] = NEW_CONDITION
        self.put_removed(old)
        self.act()

    def salvage(self, part):
        self.credits -= self.cfg["junkyard"]["price"]
        self.inventory.append(part)
        self.salvaged += 1

    def release(self, ship, penalty_fill):
        need = self.req(ship)
        if not self.weak_slots(ship):
            self.repaired += 1
        if penalty_fill:
            for i, c in enumerate(ship["slots"]):
                if c < need:
                    self.credits -= self.penalty
                    ship["slots"][i] = NEW_CONDITION
        self.credits += ship["payment"]
        idx = next(k for k, s in enumerate(self.bays) if s is ship)
        self.bays[idx] = None
        self.released += 1
        self.act()


# ---------------------------------------------------------------- bots

class Buyer:
    name = "buyer"

    def start_day(self, g, yard):
        pass

    def round(self, g):
        actions = 0
        for ship in g.docked():
            for i in g.weak_slots(ship):
                g.buy(ship, i)
                actions += 1
            g.release(ship, penalty_fill=False)
            actions += 1
        return actions

    def force(self, g):
        return False


class Releaser:
    name = "releaser"

    def start_day(self, g, yard):
        pass

    def round(self, g):
        ships = g.docked()
        for ship in ships:
            g.release(ship, penalty_fill=True)
        return len(ships)

    def force(self, g):
        return False


class Swapper:
    name = "swapper"

    def start_day(self, g, yard):
        # Salvage usable junkyard parts, best first, up to the expected need.
        good_now = sum(1 for c in g.inventory if c >= g.lowest_req())
        need = round(g.expected_weak) - good_now
        price = g.cfg["junkyard"]["price"]
        usable = sorted((c for c in yard if c >= g.lowest_req()), reverse=True)
        for c in usable:
            if need <= 0 or len(g.inventory) >= INVENTORY_SLOTS or g.credits < price:
                break
            g.salvage(c)
            need -= 1

    def round(self, g):
        actions = 0
        for ship in sorted(g.docked(), key=lambda s: len(g.weak_slots(s))):
            actions += self._repair(g, ship)
        for ship in g.docked():
            if not g.weak_slots(ship):
                g.release(ship, penalty_fill=False)
                actions += 1
        return actions

    def force(self, g):
        # Last resort: nothing could be harvested, so buy one part.
        ships = [s for s in g.docked() if g.weak_slots(s)]
        if not ships:
            return False
        ship = min(ships, key=lambda s: len(g.weak_slots(s)))
        g.buy(ship, g.weak_slots(ship)[0])
        return True

    def _repair(self, g, ship):
        actions = 0
        while True:
            weak = g.weak_slots(ship)
            if not weak:
                return actions
            if not any(self._fix(g, ship, i) for i in weak):
                return actions
            actions += 1

    def _fix(self, g, ship, i):
        if self._from_inventory(g, ship, i) or self._from_donor(g, ship, i):
            g.act()
            return True
        return False

    def _stash(self, g, part):
        # Parts below every requirement are useless: scrap them.
        if part < g.lowest_req():
            g.bin(part)
        else:
            g.put_removed(part)

    def _from_inventory(self, g, ship, i):
        k = best_index(g.inventory, g.req(ship))
        if k is None:
            return False
        part = g.inventory.pop(k)
        old = ship["slots"][i]
        ship["slots"][i] = part
        self._stash(g, old)
        return True

    def _from_donor(self, g, ship, i):
        # Take a part that passes this ship from a docked donor, and refill
        # the donor's slot from the inventory so the donor keeps passing.
        need = g.req(ship)
        choice = None
        for donor in g.docked():
            if donor is ship:
                continue
            k = best_index(g.inventory, g.req(donor))
            if k is None:
                continue
            for j, c in enumerate(donor["slots"]):
                if c >= need and (choice is None or c < choice[0]):
                    choice = (c, donor, j, k)
        if choice is None:
            return False
        c, donor, j, k = choice
        refill = g.inventory.pop(k)
        donor["slots"][j] = refill
        old = ship["slots"][i]
        ship["slots"][i] = c
        self._stash(g, old)
        return True


# ---------------------------------------------------------------- play

def play(cfg, days, world, bot, credits):
    g = Game(cfg, credits)
    log = []
    bust = None
    for day, data in zip(days, world):
        expected = len(data["arrivals"]) * (day["weakPartsMin"] + day["weakPartsMax"]) / 2
        g.start_day(day, copy.deepcopy(data["arrivals"]), expected)
        start = g.credits
        turned_before = g.turned_away
        g.credits -= cfg["upkeep"]
        bot.start_day(g, list(data["junkyard"]))
        while not g.day_over():
            if bot.round(g) or bot.force(g):
                continue
            if g.docked():
                raise RuntimeError("%s bot is stuck on day %d" % (bot.name, day["day"]))
            g.idle()
        log.append({"day": day["day"], "start": start, "end": g.credits,
                    "turned": g.turned_away - turned_before})
        if g.credits < 0:
            bust = day["day"]
            break
    return {"final": g.credits, "bust": bust, "days": log,
            "repaired": g.repaired, "released": g.released,
            "salvaged": g.salvaged, "binned": g.binned,
            "turned": g.turned_away}


def run_all(cfg, games, base_seed):
    names = ("buyer", "swapper", "releaser")
    bots = {"buyer": Buyer, "swapper": Swapper, "releaser": Releaser}
    seeds = range(base_seed, base_seed + games)
    results = {name: [] for name in names}
    starts = {}
    for seed in seeds:
        world = build_world(cfg, cfg["days"], seed)
        for name in names:
            r = play(cfg, cfg["days"], world, bots[name](), cfg["startingCredits"])
            results[name].append(r)
            if name == "swapper":
                for entry in r["days"]:
                    starts.setdefault(entry["day"], []).append(entry["start"])
    hard = max(cfg["days"],
               key=lambda d: len(arrival_times(d)) * (d["weakPartsMin"] + d["weakPartsMax"]))
    bad_day = dict(hard)
    bad_day["junkParts"] = JUNK_MAX
    bad_day["weakPartsMin"] = bad_day["weakPartsMax"]
    worst_start = min(starts.get(hard["day"], [cfg["startingCredits"]]))
    bad_finals = []
    for seed in seeds:
        world = build_world(cfg, [bad_day], seed)
        bad_finals.append(play(cfg, [bad_day], world, Swapper(), worst_start)["final"])
    return {"results": results, "hard": hard, "worst_start": worst_start,
            "bad_finals": bad_finals}


def bust_day(r):
    return r["bust"] if r["bust"] is not None else DAY_COUNT + 1


def daily_nets(swapper_results):
    nets = {d: [] for d in range(1, DAY_COUNT + 1)}
    for r in swapper_results:
        for e in r["days"]:
            nets[e["day"]].append(e["end"] - e["start"])
    return nets


def make_targets(cfg, run, max_net):
    """Return (status, text) pairs. status is True (PASS), False (FAIL) or
    None (SKIP: not checked)."""
    results = run["results"]
    nets = daily_nets(results["swapper"])
    out = []

    busts = [bust_day(r) for r in results["buyer"]]
    med = statistics.median(busts)
    out.append((med <= BUYER_TARGET_DAY,
                "buyer goes bankrupt by about day %d (median bankruptcy day %.1f)"
                % (BUYER_TARGET_DAY, med)))

    swap_bankrupt = sum(1 for r in results["swapper"] if r["bust"] is not None)
    out.append((swap_bankrupt == 0,
                "swapper never goes bankrupt (%d bankrupt games)" % swap_bankrupt))

    failing = [d for d in nets if not nets[d] or statistics.mean(nets[d]) <= 0]
    out.append((not failing,
                "swapper ends each day above its start on average, hardest days included "
                "(failing days: %s)" % (", ".join(str(d) for d in failing) or "none")))

    if max_net is None:
        out.append((None,
                    "swapper mean net per day has an upper limit (not checked: no limit "
                    "given, use --max-daily-net)"))
    else:
        over = [d for d in nets if nets[d] and statistics.mean(nets[d]) > max_net]
        out.append((not over,
                    "swapper mean net per day is at most %d (days over: %s)"
                    % (max_net, ", ".join(str(d) for d in over) or "none")))

    hard = run["hard"]
    worst = min(run["bad_finals"])
    out.append((worst >= 0,
                "bad-luck swapper day (day %d: %d junk parts, most weak parts, start %d "
                "credits) ends at or above 0 (worst %d)"
                % (hard["day"], JUNK_MAX, run["worst_start"], worst)))

    start = cfg["startingCredits"]
    rel_mean = statistics.mean(r["final"] for r in results["releaser"])
    out.append((rel_mean < start,
                "releaser loses money over the run (mean final %.0f, starting %d)"
                % (rel_mean, start)))

    pen = penalty_price(cfg)
    price = cfg["newPartPrice"]
    out.append((pen > price,
                "releasing with an unrepaired part costs more than buying it "
                "(penalty %d, shop %d)" % (pen, price)))

    lowest_pay = min(cfg["fighterPayment"][0], cfg["freighterPayment"][0])
    out.append((pen > lowest_pay,
                "one penalty fill costs more than the lowest ship payment "
                "(penalty %d, lowest payment %d)" % (pen, lowest_pay)))

    jy = cfg["junkyard"]["price"]
    scrap = cfg["scrapPrice"]
    out.append((scrap < jy < price,
                "junkyard price is above the scrap price and below a new part, so "
                "binning never pays (junkyard %d, scrap %d, new %d) "
                "(partial: the GDD's 'far below 150' margin is not set)"
                % (jy, scrap, price)))
    return out


# ---------------------------------------------------------------- output

def summary_row(name, rs):
    busts = [bust_day(r) for r in rs]
    return "%-9s %6d %9d %11.1f %12.0f %9.1f %9.1f %7.1f %8.1f" % (
        name,
        len(rs),
        sum(1 for r in rs if r["bust"] is not None),
        statistics.median(busts),
        statistics.mean(r["final"] for r in rs),
        statistics.mean(r["repaired"] for r in rs),
        statistics.mean(r["salvaged"] for r in rs),
        statistics.mean(r["binned"] for r in rs),
        statistics.mean(r["turned"] for r in rs),
    )


def main(argv=None):
    parser = argparse.ArgumentParser(
        description="Buyer, swapper and releaser simulation with balance target checks "
                    "for Good Enough Repairs.")
    parser.add_argument("balance", help="path to the balance JSON file "
                                        "(format is in the module docstring)")
    parser.add_argument("--games", type=int, default=300,
                        help="seeded games per bot (default: 300)")
    parser.add_argument("--seed", type=int, default=1,
                        help="first seed (default: 1)")
    parser.add_argument("--max-daily-net", type=int, default=None,
                        help="upper limit for the swapper's mean net per day; "
                             "not checked when omitted")
    args = parser.parse_args(argv)
    if args.games < 1:
        parser.error("--games must be at least 1")

    try:
        with open(args.balance, encoding="utf-8") as fh:
            data = json.load(fh)
    except OSError as exc:
        print("FAIL: cannot read %s: %s" % (args.balance, exc))
        return 1
    except ValueError as exc:
        print("FAIL: %s is not valid JSON: %s" % (args.balance, exc))
        return 1

    cfg, errors = validate_balance(data)
    if errors:
        print("FAIL: %s has %d problem(s):" % (args.balance, len(errors)))
        for e in errors:
            print("  - " + e)
        return 1

    run = run_all(cfg, args.games, args.seed)
    results = run["results"]
    targets = make_targets(cfg, run, args.max_daily_net)

    print("Balance file: %s" % args.balance)
    print("Games per bot: %d (seeds %d to %d)"
          % (args.games, args.seed, args.seed + args.games - 1))
    print()
    print("%-9s %6s %9s %11s %12s %9s %9s %7s %8s" % (
        "bot", "games", "bankrupt", "median day", "mean final",
        "repaired", "salvaged", "binned", "turned"))
    for name in ("buyer", "swapper", "releaser"):
        print(summary_row(name, results[name]))
    print()

    nets = daily_nets(results["swapper"])
    parts = []
    for d in range(1, DAY_COUNT + 1):
        if nets[d]:
            parts.append("d%d %+.0f" % (d, statistics.mean(nets[d])))
        else:
            parts.append("d%d n/a" % d)
    print("Swapper mean net per day: " + ", ".join(parts))
    print()

    labels = {True: "PASS", False: "FAIL", None: "SKIP"}
    print("Targets:")
    for status, text in targets:
        print("  [%s] %s" % (labels[status], text))
    failed = sum(1 for status, _ in targets if status is False)
    skipped = sum(1 for status, _ in targets if status is None)
    print()
    if failed:
        print("RESULT: %d of %d targets failed" % (failed, len(targets)))
        return 1
    print("RESULT: all %d checked targets pass (%d skipped)"
          % (len(targets) - skipped, skipped))
    return 0


if __name__ == "__main__":
    sys.exit(main())
