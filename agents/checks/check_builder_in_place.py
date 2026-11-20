#!/usr/bin/env python3
"""Check that the prefab builder updates a prefab in place, with Unity in batch mode.

Usage: python agents/checks/check_builder_in_place.py <spec path>

The spec must be Assets/Prefabs/UX/ship_in_bay.spec.json. The script runs
GoodEnoughRepairs.EditorTools.BuilderInPlaceCheck.Run. Unity builds a copy of the spec
(kept outside the project, with its own temp prefab), changes one property, then
removes one object and adds one, and compares object identity and property values
before and after. The script prints the RULE lines and the RESULT line from Unity's
log, one "MADE: <path>" line for each file Unity made that is still in the repo, and
checks that nothing temporary is left and the real spec is unchanged.

Unity's executable path comes only from the UNITY_PATH environment variable.
The log is written to a temp folder outside the project.

Exit 0 when every RULE line says PASS and RESULT: PASS is present.
Exit 1 on any FAIL, a missing RESULT or rule, a Unity crash or a start error.
"""

import os
import re
import sys
import tempfile

from unity_common import (ascii_text, fail, find_project, find_unity, read_log,
                          report_compile_errors, run_unity)

METHOD = "GoodEnoughRepairs.EditorTools.BuilderInPlaceCheck.Run"
TOOL_SPEC_PATH = "Assets/Prefabs/UX/ship_in_bay.spec.json"
TEMP_ASSET_FOLDER = "Assets/GerBuilderCheckTemp"
RULE_LINE = re.compile(r"^RULE .*\s(PASS|FAIL)\s*$")
RESULT_LINE = re.compile(r"^RESULT: (PASS|FAIL)\s*$")
REQUIRED = [
    "stage1 prefab not rebuilt",
    "stage1 structure unchanged",
    "stage1 changed properties",
    "stage1 other objects identity",
    "stage2 added objects",
    "stage2 removed objects",
    "stage2 other property changes",
    "stage2 other objects identity",
    "real spec unchanged",
    "temp files removed",
]


def read_bytes(path):
    try:
        with open(path, "rb") as f:
            return f.read()
    except OSError:
        return None


def main(argv):
    if len(argv) != 2:
        return fail("usage: python agents/checks/check_builder_in_place.py <spec path>")

    spec = os.path.abspath(argv[1])
    if not os.path.isfile(spec):
        return fail("spec not found: " + spec)

    project = find_project(spec)
    if project is None:
        return fail("no Unity project above the spec: " + spec)

    rel = os.path.relpath(spec, project).replace(os.sep, "/")
    if os.path.normcase(rel) != os.path.normcase(TOOL_SPEC_PATH):
        return fail("the Unity tool uses only %s, but got %s" % (TOOL_SPEC_PATH, rel))

    unity, error = find_unity()
    if error:
        return fail(error)

    spec_before = read_bytes(spec)

    out_dir = tempfile.mkdtemp(prefix="ger_inplace_log_")
    log_path = os.path.join(out_dir, "builder-in-place-log.txt")

    exit_code, error = run_unity(unity, ["-quit"], project, ["-executeMethod", METHOD], log_path)
    if error:
        return fail(error)

    log_lines = read_log(log_path)
    if not log_lines:
        return fail("Unity wrote no log (exit code %d). Log: %s" % (exit_code, log_path))

    if report_compile_errors(log_lines, log_path):
        return 1

    rules = [l.rstrip() for l in log_lines if RULE_LINE.match(l)]
    errors = [l.rstrip() for l in log_lines
              if l.startswith("RULE ") and not RULE_LINE.match(l)]
    results = [RESULT_LINE.match(l).group(1) for l in log_lines if RESULT_LINE.match(l)]
    temp_dirs = [l[len("TEMPDIR: "):].strip() for l in log_lines if l.startswith("TEMPDIR: ")]
    made = []
    for l in log_lines:
        if l.startswith("MADE: "):
            path = l[len("MADE: "):].strip().replace("\\", "/")
            if path and path not in made:
                made.append(path)

    for line in rules:
        print(ascii_text(line))

    problems = []
    for line in errors:
        problems.append("malformed rule line: " + line)

    failed_lines = [l for l in rules if l.endswith("FAIL")]
    if failed_lines:
        problems.append("%d rule(s) failed" % len(failed_lines))

    for name in REQUIRED:
        if not any(l.startswith("RULE " + name + ":") for l in rules):
            problems.append("no RULE line for: " + name)

    if not results:
        problems.append("RESULT line is missing")
    else:
        last = results[-1]
        print("RESULT: " + last)
        if last != "PASS":
            problems.append("RESULT is FAIL")
        elif failed_lines:
            problems.append("RESULT says PASS but a RULE line failed")

    if exit_code != 0 and not failed_lines and (not results or results[-1] == "PASS"):
        problems.append("Unity exited with code %d" % exit_code)

    # Nothing temporary left behind, and the real spec is untouched.
    if spec_before is None or read_bytes(spec) != spec_before:
        problems.append("the real spec changed: " + rel)
    if not temp_dirs:
        problems.append("Unity did not report its temp spec folder")
    for d in temp_dirs:
        if os.path.exists(d):
            problems.append("temp spec folder left behind: " + d)
    leftover = os.path.join(project, TEMP_ASSET_FOLDER)
    if os.path.exists(leftover) or os.path.exists(leftover + ".meta"):
        problems.append("temp prefab folder left behind: " + TEMP_ASSET_FOLDER)

    for path in made:
        if path.startswith(TEMP_ASSET_FOLDER):
            continue
        if os.path.isfile(os.path.join(project, path)):
            print("MADE: " + ascii_text(path))

    if problems:
        print("FAIL: " + ascii_text("; ".join(problems)) + ". Log: " + ascii_text(log_path))
        return 1

    print("OK: builder updates in place, all rules PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv))
