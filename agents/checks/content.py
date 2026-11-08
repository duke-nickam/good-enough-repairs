#!/usr/bin/env python3
"""Content check for the game's text files.

Usage:
    python content.py FILE [FILE ...]

Files are recognised by name:
    mechanic_lines.json, day_briefs.json, part_catalog.json, balance.json

balance.json is only used to cross-check the day types in day_briefs.json.
Only the files given as arguments are read.
Prints each failure and exits 1 on any failure, 0 when all pass.
"""
import json
import os
import sys

LONG_DASHES = {
    "\u2012": "figure dash",
    "\u2013": "en dash",
    "\u2014": "em dash",
    "\u2015": "horizontal bar",
    "\u2212": "minus sign",
    "\u2e3a": "two-em dash",
    "\u2e3b": "three-em dash",
}

MOMENTS = [
    "day_start", "release_warning", "penalty_fill", "day_end", "bankruptcy",
    "run_end_win", "run_end_bankrupt", "tutorial_hint", "fallback_hint",
]
PART_TYPES = ["engine", "hull_plate", "shield_generator", "thruster", "power_core"]

failures = []


def fail(name, msg):
    failures.append("%s: %s" % (name, msg))


def load(path):
    """Return (parsed json or None). Reports read, LF and JSON failures."""
    name = os.path.basename(path)
    try:
        with open(path, "rb") as f:
            raw = f.read()
    except OSError as e:
        fail(name, "cannot read file (%s)" % e)
        return None
    if b"\r" in raw:
        fail(name, "file has CR characters; LF line endings only")
    try:
        text = raw.decode("utf-8")
    except UnicodeDecodeError as e:
        fail(name, "not valid UTF-8 (%s)" % e)
        return None
    if text.startswith("\ufeff"):
        fail(name, "file starts with a byte order mark")
        return None
    try:
        return json.loads(text)
    except ValueError as e:
        fail(name, "invalid JSON (%s)" % e)
        return None


def walk_strings(node, where):
    if isinstance(node, str):
        yield where, node
    elif isinstance(node, dict):
        for k, v in node.items():
            yield from walk_strings(v, "%s.%s" % (where, k))
    elif isinstance(node, list):
        for i, v in enumerate(node):
            yield from walk_strings(v, "%s[%d]" % (where, i))


def check_text(name, data):
    for where, s in walk_strings(data, "$"):
        if not s.strip():
            fail(name, "empty string at %s" % where)
        if any(c.isdigit() for c in s):
            fail(name, "digit in text at %s: %r" % (where, s))
        for c, label in LONG_DASHES.items():
            if c in s:
                fail(name, "%s (long dash) in text at %s: %r; use plain '-'" % (label, where, s))


def is_text(v):
    return isinstance(v, str) and v.strip() != ""


def check_moments(name, data):
    if not isinstance(data, dict) or not isinstance(data.get("moments"), dict):
        fail(name, "top level must be an object with an object 'moments'")
        return
    moments = data["moments"]
    for k in MOMENTS:
        if k not in moments:
            fail(name, "missing moment '%s'" % k)
    for k in moments:
        if k not in MOMENTS:
            fail(name, "unexpected moment '%s'" % k)
    for k in MOMENTS:
        if k not in moments:
            continue
        v = moments[k]
        if not isinstance(v, list) or len(v) == 0:
            fail(name, "moment '%s' must be a non-empty list" % k)
            continue
        for i, s in enumerate(v):
            if not is_text(s):
                fail(name, "moment '%s' entry %d must be a non-empty string" % (k, i))


def check_briefs(name, data, balance):
    if not isinstance(data, dict) or not isinstance(data.get("days"), list):
        fail(name, "top level must be an object with a list 'days'")
        return
    days = data["days"]
    if len(days) != 7:
        fail(name, "'days' must have exactly 7 entries, found %d" % len(days))
    bal_types = {}
    if balance is not None:
        if isinstance(balance, dict) and isinstance(balance.get("days"), list):
            for d in balance["days"]:
                if isinstance(d, dict):
                    bal_types[d.get("day")] = d.get("type")
        else:
            fail("balance.json", "no 'days' list to compare with")
    for i, e in enumerate(days):
        want = i + 1
        if not isinstance(e, dict):
            fail(name, "entry %d must be an object" % i)
            continue
        d = e.get("day")
        if isinstance(d, bool) or not isinstance(d, int) or not 1 <= d <= 7:
            fail(name, "entry %d: 'day' must be an integer 1 to 7" % i)
        elif d != want:
            fail(name, "entry %d: 'day' is %d, expected %d (days in order)" % (i, d, want))
        t = e.get("type")
        if t not in ("quiet", "busy"):
            fail(name, "entry %d: 'type' must be 'quiet' or 'busy'" % i)
        if not is_text(e.get("reason")):
            fail(name, "entry %d: 'reason' must be a non-empty string" % i)
        if balance is not None and t in ("quiet", "busy") and isinstance(d, int) and not isinstance(d, bool):
            if d not in bal_types:
                fail(name, "day %d is not in balance.json" % d)
            elif bal_types[d] != t:
                fail(name, "day %d type '%s' does not match balance.json '%s'" % (d, t, bal_types[d]))


def check_parts(name, data):
    if not isinstance(data, dict) or not isinstance(data.get("parts"), list):
        fail(name, "top level must be an object with a list 'parts'")
        return
    parts = data["parts"]
    if len(parts) != 5:
        fail(name, "'parts' must have exactly 5 entries, found %d" % len(parts))
    seen = []
    for i, p in enumerate(parts):
        if not isinstance(p, dict):
            fail(name, "entry %d must be an object" % i)
            continue
        for f in ("type", "name", "description"):
            if not is_text(p.get(f)):
                fail(name, "entry %d: '%s' must be a non-empty string" % (i, f))
        if isinstance(p.get("type"), str):
            seen.append(p["type"])
    for t in PART_TYPES:
        n = seen.count(t)
        if n == 0:
            fail(name, "part type '%s' is missing" % t)
        elif n > 1:
            fail(name, "part type '%s' appears %d times" % (t, n))
    for t in seen:
        if t not in PART_TYPES:
            fail(name, "unknown part type '%s'" % t)


def main(argv):
    if not argv:
        print("usage: content.py FILE [FILE ...]")
        return 1
    loaded = {}
    for path in argv:
        name = os.path.basename(path)
        if name not in ("mechanic_lines.json", "day_briefs.json",
                        "part_catalog.json", "balance.json"):
            fail(name, "unknown file name; cannot decide what to check")
            continue
        loaded[name] = load(path)
    for name, data in loaded.items():
        if data is None or name == "balance.json":
            continue
        check_text(name, data)
        if name == "mechanic_lines.json":
            check_moments(name, data)
        elif name == "day_briefs.json":
            check_briefs(name, data, loaded.get("balance.json"))
        elif name == "part_catalog.json":
            check_parts(name, data)
    if failures:
        for f in failures:
            print("FAIL " + f)
        print("%d failure(s)" % len(failures))
        return 1
    print("content check: all pass")
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
