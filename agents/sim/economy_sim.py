"""Economy simulation for Good Enough Repairs.

A text-only copy of the game's economy rules (no graphics). Scripted bots play
the 7-day run many times so the QA Playtester can check the GDD targets.

Bots (simple strategies, not AI):
  buyer    - buys a new part for every weak slot, never swaps or salvages.
  swapper  - moves parts between docked ships, uses the inventory and junkyard,
             buys only when nothing else works.
  releaser - never repairs; releases every ship at the penalty price.
"""
import random
import statistics

PART_TYPES = ["engine", "thruster", "shield", "hull"]
REQUIRED = {"fighter": 70, "freighter": 60}  # locked in the GDD
BAYS = 3
INVENTORY_SLOTS = 10


class Part:
    __slots__ = ("kind", "cond")

    def __init__(self, kind, cond):
        self.kind, self.cond = kind, cond


class Ship:
    def __init__(self, ship_type, parts, pay):
        self.type, self.parts, self.pay = ship_type, parts, pay

    @property
    def req(self):
        return REQUIRED[self.type]

    def weak_slots(self):
        return [k for k, p in self.parts.items() if p is None or p.cond < self.req]


def _rng_int(rng, r):
    return rng.randint(r["min"], r["max"])


def make_ship(rng, eco, day_cfg):
    ship_type = "fighter" if rng.random() < 0.5 else "freighter"
    req = REQUIRED[ship_type]
    n_weak = rng.randint(day_cfg["weakMin"], day_cfg["weakMax"])
    weak = set(rng.sample(PART_TYPES, min(n_weak, len(PART_TYPES))))
    parts = {}
    for k in PART_TYPES:
        if k in weak:
            # Broken parts, and for fighters sometimes "near" parts (60-69) that a freighter could use.
            parts[k] = Part(k, rng.randint(5, req - 1))
        else:
            parts[k] = Part(k, rng.randint(req, 100))
    pay = _rng_int(rng, eco["fighterPayment"] if ship_type == "fighter" else eco["freighterPayment"])
    return Ship(ship_type, parts, pay)


def part_prices(rng, eco):
    """Part prices are fixed per part type for the whole run (GDD: prices fixed)."""
    return {k: _rng_int(rng, eco["partPrice"]) for k in PART_TYPES}


def junkyard(rng, eco, day_index):
    total = eco["junkyardParts"]
    junk = _rng_int(rng, eco["junkyardJunk"])
    # The mechanic's skill grows during the run: fewer junk parts later.
    junk = max(0, junk - day_index // 3)
    parts = []
    for i in range(total):
        k = rng.choice(PART_TYPES)
        cond = rng.randint(5, 55) if i < junk else rng.randint(60, 95)
        parts.append(Part(k, cond))
    rng.shuffle(parts)
    return parts


# ---------------------------------------------------------------- bots

def buyer_repair(group, inv, prices, eco):
    cost = income = 0
    for ship in group:
        for k in ship.weak_slots():
            cost += prices[k]
            income += eco["scrapPrice"]  # old part binned
            ship.parts[k] = Part(k, 100)
    return income - cost


def releaser_repair(group, inv, prices, eco):
    cost = 0
    for ship in group:
        for k in ship.weak_slots():
            cost += round(prices[k] * (1 + eco["penaltyPercent"] / 100))
            ship.parts[k] = Part(k, 100)
    return -cost


def swapper_repair(group, inv, prices, eco):
    """Harvest parts across the docked ships and the inventory; buy only what is left."""
    net = 0
    pool = list(inv)
    inv.clear()
    # Take every weak part off the ships into the pool.
    for ship in group:
        for k in ship.weak_slots():
            if ship.parts[k] is not None:
                pool.append(ship.parts[k])
            ship.parts[k] = None
    # Fill the hardest slots (fighters) first, each with the lowest part that is good enough.
    slots = sorted(((s, k) for s in group for k in PART_TYPES if s.parts[k] is None),
                   key=lambda sk: -sk[0].req)
    unfilled = []
    for ship, k in slots:
        fits = [p for p in pool if p.kind == k and p.cond >= ship.req]
        if fits:
            best = min(fits, key=lambda p: p.cond)
            pool.remove(best)
            ship.parts[k] = best
        else:
            unfilled.append((ship, k))
    # Cross-type trick: a freighter gives its 70+ part to a fighter and takes a 60-69 part.
    still = []
    for ship, k in unfilled:
        done = False
        if ship.type == "fighter":
            near = [p for p in pool if p.kind == k and 60 <= p.cond < 70]
            donors = [s for s in group if s.type == "freighter" and s.parts[k] is not None
                      and s.parts[k].cond >= 70]
            if near and donors:
                donor = donors[0]
                ship.parts[k] = donor.parts[k]
                donor.parts[k] = min(near, key=lambda p: p.cond)
                pool.remove(donor.parts[k])
                done = True
        if not done:
            still.append((ship, k))
    # Buy what is still missing.
    for ship, k in still:
        net -= prices[k]
        ship.parts[k] = Part(k, 100)
    # Keep the most useful leftovers, bin the rest for scrap.
    pool.sort(key=lambda p: -p.cond)
    keep = [p for p in pool if p.cond >= 60][:INVENTORY_SLOTS]
    for p in pool:
        if p not in keep:
            net += eco["scrapPrice"]
    inv.extend(keep)
    return net


def swapper_junkyard(yard, inv, balance, eco):
    """Salvage usable parts while there is inventory room and money above the day's upkeep."""
    spent = 0
    for p in yard:
        if len(inv) >= INVENTORY_SLOTS:
            break
        if p.cond >= 60 and balance - spent - eco["junkyardPartPrice"] >= 0:
            inv.append(p)
            spent += eco["junkyardPartPrice"]
    return -spent


BOTS = {"buyer": buyer_repair, "swapper": swapper_repair, "releaser": releaser_repair}


# ---------------------------------------------------------------- one run

def play_run(eco, bot, seed, bad_day=None):
    """Play days 1-7. Returns dict with bankrupt day (or None), final credits and daily nets."""
    rng = random.Random(seed)
    prices = part_prices(rng, eco)
    balance = eco["startingCredits"]
    inv = []
    daily = []
    for d, day_cfg in enumerate(eco["days"]):
        start = balance
        balance -= eco["stationUpkeep"]  # paid at the start of the day
        cfg = dict(day_cfg)
        if bad_day is not None and d == bad_day:
            cfg["weakMin"] = cfg["weakMax"] = 3  # worst case: every ship arrives with 3 weak parts
        yard = junkyard(rng, eco, d)
        if bad_day is not None and d == bad_day:
            yard = [Part(p.kind, rng.randint(5, 55)) for p in yard]  # and the junkyard is all junk
        if bot == "swapper":
            balance += swapper_junkyard(yard, inv, balance, eco)
        ships = [make_ship(rng, eco, cfg) for _ in range(cfg["ships"])]
        for i in range(0, len(ships), BAYS):
            group = ships[i:i + BAYS]
            balance += BOTS[bot](group, inv, prices, eco)
            balance += sum(s.pay for s in group)
        daily.append(balance - start)
        if balance < 0:
            return {"bankrupt_day": d + 1, "final": balance, "daily": daily}
    return {"bankrupt_day": None, "final": balance, "daily": daily}


# ---------------------------------------------------------------- report

def simulate(eco, runs=300):
    out = {}
    for bot in BOTS:
        res = [play_run(eco, bot, seed) for seed in range(runs)]
        bdays = [r["bankrupt_day"] for r in res if r["bankrupt_day"]]
        survivors = [r for r in res if not r["bankrupt_day"]]
        out[bot] = {
            "bankrupt_rate": round(len(bdays) / runs, 3),
            "median_bankrupt_day": statistics.median(bdays) if bdays else None,
            "survive_rate": round(len(survivors) / runs, 3),
            "mean_score": round(statistics.mean(r["final"] - eco["startingCredits"] for r in survivors), 1)
            if survivors else None,
            "worst_day_mean": round(statistics.mean(min(r["daily"]) for r in res), 1),
            "last_day_net_mean": round(statistics.mean(r["daily"][-1] for r in survivors), 1)
            if survivors else None,
        }
    # Worst-case day in the middle of the run (day 4): can a good swapper recover?
    bad = [play_run(eco, "swapper", seed, bad_day=3) for seed in range(runs)]
    out["swapper_bad_day4"] = {
        "survive_rate": round(sum(1 for r in bad if not r["bankrupt_day"]) / runs, 3),
        "bad_day_net_mean": round(statistics.mean(r["daily"][3] for r in bad if len(r["daily"]) > 3), 1),
    }
    return out


TARGETS = [
    # (id, description, check)
    ("buyer_bankrupt", "Buyer bot goes bankrupt by about day 3 (in 90%+ of runs, median day 2-4)",
     lambda r, e: r["buyer"]["bankrupt_rate"] >= 0.9 and 2 <= (r["buyer"]["median_bankrupt_day"] or 99) <= 4),
    ("swapper_positive", "Swapper bot survives all 7 days (90%+ of runs) and stays slightly positive on the hardest day (day 7 net >= 0)",
     lambda r, e: r["swapper"]["survive_rate"] >= 0.9 and (r["swapper"]["last_day_net_mean"] or -1) >= 0),
    ("bad_day_recoverable", "A worst-case day 4 is recoverable: the swapper still survives the run in 75%+ of runs",
     lambda r, e: r["swapper_bad_day4"]["survive_rate"] >= 0.75),
    ("release_spam_loses", "Releasing unrepaired ships never pays: releaser bot goes bankrupt in 90%+ of runs",
     lambda r, e: r["releaser"]["bankrupt_rate"] >= 0.9),
    ("penalty_invariant", "Filling one weak part costs more than the lowest ship payment",
     lambda r, e: e["partPrice"]["min"] * (1 + e["penaltyPercent"] / 100)
     > min(e["fighterPayment"]["min"], e["freighterPayment"]["min"])),
]


def check(eco, results):
    return [{"id": t, "target": desc, "pass": bool(fn(results, eco))} for t, desc, fn in TARGETS]


if __name__ == "__main__":
    import json, sys
    eco = json.load(open(sys.argv[1]))
    r = simulate(eco)
    print(json.dumps(r, indent=2))
    print(json.dumps(check(eco, r), indent=2))
