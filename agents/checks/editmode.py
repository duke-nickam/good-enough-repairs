#!/usr/bin/env python3
"""Run the Unity EditMode tests in batch mode and check the result.

Usage: python agents/checks/editmode.py <unity project path>

Unity's executable path comes only from the UNITY_PATH environment variable.
Results and log files are written to a temp folder outside the project.
Exit 0 when all tests pass (and at least one ran), exit 1 otherwise.
"""

import os
import sys
import tempfile
import xml.etree.ElementTree as ET

from unity_common import (ascii_text, fail, find_unity, project_error, read_log,
                          report_compile_errors, run_unity)


def read_counts(results_path):
    """Return (total, passed, failed) from the NUnit results file, or None."""
    try:
        root = ET.parse(results_path).getroot()
    except (OSError, ET.ParseError):
        return None
    if root.tag == "test-run" and root.get("total") is not None:
        try:
            total = int(root.get("total"))
            passed = int(root.get("passed", "0"))
            failed = int(root.get("failed", "0"))
            return total, passed, failed
        except ValueError:
            pass
    total = passed = failed = 0
    for case in root.iter("test-case"):
        total += 1
        result = (case.get("result") or "").lower()
        if result == "passed":
            passed += 1
        elif result in ("failed", "error"):
            failed += 1
    return total, passed, failed


def failed_names(results_path, limit=10):
    names = []
    try:
        root = ET.parse(results_path).getroot()
    except (OSError, ET.ParseError):
        return names
    for case in root.iter("test-case"):
        if (case.get("result") or "").lower() in ("failed", "error"):
            names.append(case.get("fullname") or case.get("name") or "?")
            if len(names) >= limit:
                break
    return names


def main(argv):
    if len(argv) != 2:
        return fail("usage: python agents/checks/editmode.py <unity project path>")

    project = os.path.abspath(argv[1])
    error = project_error(project)
    if error:
        return fail(error)

    unity, error = find_unity()
    if error:
        return fail(error)

    out_dir = tempfile.mkdtemp(prefix="ger_editmode_")
    results_path = os.path.join(out_dir, "editmode-results.xml")
    log_path = os.path.join(out_dir, "editmode-log.txt")

    exit_code, error = run_unity(
        unity,
        ["-nographics"],
        project,
        ["-runTests", "-testPlatform", "EditMode", "-testResults", results_path],
        log_path)
    if error:
        return fail(error)

    log_lines = read_log(log_path)

    if report_compile_errors(log_lines, log_path):
        return 1

    counts = read_counts(results_path)
    if counts is None:
        if not log_lines:
            return fail("Unity wrote no log and no results (exit code %d). Log: %s" % (exit_code, log_path))
        tail = [l.strip() for l in log_lines[-5:] if l.strip()]
        print("FAIL: no results file written (Unity exit code %d). Log: %s" % (exit_code, ascii_text(log_path)))
        for line in tail:
            print("  " + ascii_text(line))
        return 1

    total, passed, failed = counts

    if total == 0:
        return fail("no tests ran. Log: " + log_path)

    if failed > 0:
        print("FAIL: %d test(s) failed, %d passed. Results: %s" % (failed, passed, ascii_text(results_path)))
        for name in failed_names(results_path):
            print("  " + ascii_text(name))
        return 1

    if exit_code != 0:
        return fail("Unity exited with code %d. Log: %s" % (exit_code, log_path))

    if passed == 0:
        return fail("no test passed. Log: " + log_path)

    print("OK: %d EditMode test(s) passed" % passed)
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv))
