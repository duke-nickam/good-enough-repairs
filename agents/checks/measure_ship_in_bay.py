#!/usr/bin/env python3
"""Measure the ship in bay 1 with Unity in batch mode and check the result.

Usage: python agents/checks/measure_ship_in_bay.py <spec path>

The spec must be the ship in bay spec the Unity tool measures
(Assets/Prefabs/UX/ship_in_bay.spec.json). The script runs
GoodEnoughRepairs.EditorTools.ShipInBayMeasure.Run and prints the
MEASURE lines and the RESULT line from Unity's log, and any
"SCREENSHOT: <absolute path>" lines the run made.

Unity's executable path comes only from the UNITY_PATH environment variable.
The log is written to a temp folder outside the project.

Exit 0 when every MEASURE line says PASS and RESULT: PASS is present.
Exit 1 on any FAIL, a missing RESULT, a missing measurement, a Unity crash
or a Unity start error.
"""

import json
import os
import re
import sys
import tempfile

from unity_common import (ascii_text, fail, find_project, find_unity, read_log,
                          report_compile_errors, run_unity)

METHOD = "GoodEnoughRepairs.EditorTools.ShipInBayMeasure.Run"
TOOL_SPEC_PATH = "Assets/Prefabs/UX/ship_in_bay.spec.json"
MEASURE_LINE = re.compile(r"^MEASURE .*\s(PASS|FAIL)\s*$")
RESULT_LINE = re.compile(r"^RESULT: (PASS|FAIL)\s*$")
REQUIRED = [
    ("ship center", "center"),
    ("red bar", "red bar"),
    ("green bar", "green bar"),
    ("blue bar", "blue bar"),
]


def main(argv):
    if len(argv) != 2:
        return fail("usage: python agents/checks/measure_ship_in_bay.py <spec path>")

    spec = os.path.abspath(argv[1])
    if not os.path.isfile(spec):
        return fail("spec not found: " + spec)

    try:
        with open(spec, "r", encoding="utf-8") as f:
            data = json.load(f)
    except (OSError, ValueError) as e:
        return fail("spec is not readable JSON: %s" % e)
    if not isinstance(data, dict) or not isinstance(data.get("scene"), dict):
        return fail("spec has no scene block: " + spec)

    project = find_project(spec)
    if project is None:
        return fail("no Unity project above the spec: " + spec)

    rel = os.path.relpath(spec, project).replace(os.sep, "/")
    if os.path.normcase(rel) != os.path.normcase(TOOL_SPEC_PATH):
        return fail("the Unity tool measures only %s, but got %s" % (TOOL_SPEC_PATH, rel))

    unity, error = find_unity()
    if error:
        return fail(error)

    out_dir = tempfile.mkdtemp(prefix="ger_measure_")
    log_path = os.path.join(out_dir, "measure-log.txt")

    exit_code, error = run_unity(unity, ["-quit"], project, ["-executeMethod", METHOD], log_path)
    if error:
        return fail(error)

    log_lines = read_log(log_path)
    if not log_lines:
        return fail("Unity wrote no log (exit code %d). Log: %s" % (exit_code, log_path))

    if report_compile_errors(log_lines, log_path):
        return 1

    tolerance = [l.rstrip() for l in log_lines if l.startswith("TOLERANCE: ")]
    measures = [l.rstrip() for l in log_lines if MEASURE_LINE.match(l)]
    errors = [l.rstrip() for l in log_lines
              if l.startswith("MEASURE ") and not MEASURE_LINE.match(l)]
    results = [RESULT_LINE.match(l).group(1) for l in log_lines if RESULT_LINE.match(l)]
    shots = [l.rstrip() for l in log_lines if l.startswith("SCREENSHOT: ")]

    for line in tolerance[:1]:
        print(ascii_text(line))
    for line in measures:
        print(ascii_text(line))

    problems = []
    for line in errors:
        problems.append("malformed measure line: " + line)

    failed_lines = [l for l in measures if l.endswith("FAIL")]
    if failed_lines:
        problems.append("%d measurement(s) failed" % len(failed_lines))

    for key, label in REQUIRED:
        if not any(key in l for l in measures):
            problems.append("no MEASURE line for the " + label)

    if not results:
        problems.append("RESULT line is missing")
    else:
        last = results[-1]
        print("RESULT: " + last)
        if last != "PASS":
            problems.append("RESULT is FAIL")
        elif failed_lines:
            problems.append("RESULT says PASS but a MEASURE line failed")

    if exit_code != 0 and not failed_lines and (not results or results[-1] == "PASS"):
        problems.append("Unity exited with code %d" % exit_code)

    for line in shots:
        path = line[len("SCREENSHOT: "):].strip()
        if not os.path.isabs(path):
            problems.append("screenshot path is not absolute: " + path)
        elif not os.path.isfile(path):
            problems.append("screenshot file does not exist: " + path)
        else:
            print("SCREENSHOT: " + ascii_text(path))

    if problems:
        print("FAIL: " + ascii_text("; ".join(problems)) + ". Log: " + ascii_text(log_path))
        return 1

    print("OK: ship in bay measured, all lines PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv))
