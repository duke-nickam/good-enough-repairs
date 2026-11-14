#!/usr/bin/env python3
"""Run the Unity EditMode tests in batch mode and check the result.

Usage: python agents/checks/editmode.py <unity project path>

Unity's executable path comes only from the UNITY_PATH environment variable.
Results and log files are written to a temp folder outside the project.
Exit 0 when all tests pass (and at least one ran), exit 1 otherwise.
"""

import os
import re
import subprocess
import sys
import tempfile
import xml.etree.ElementTree as ET

TIMEOUT_SECONDS = 1800
COMPILE_ERROR = re.compile(r"error CS\d+")


def ascii_text(text):
    return text.encode("ascii", "replace").decode("ascii")


def fail(message):
    print("FAIL: " + ascii_text(message))
    return 1


def read_log(path):
    try:
        with open(path, "r", encoding="utf-8", errors="replace") as f:
            return f.read().splitlines()
    except OSError:
        return []


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
    if not os.path.isdir(os.path.join(project, "Assets")):
        return fail("not a Unity project (no Assets folder): " + project)

    unity = os.environ.get("UNITY_PATH", "").strip()
    if not unity:
        return fail("UNITY_PATH is not set. Set it to the Unity editor executable.")
    if not os.path.isfile(unity):
        return fail("UNITY_PATH points to nothing: " + unity)

    out_dir = tempfile.mkdtemp(prefix="gер_editmode_".replace("\u0435", "e"))
    results_path = os.path.join(out_dir, "editmode-results.xml")
    log_path = os.path.join(out_dir, "editmode-log.txt")

    command = [
        unity,
        "-batchmode",
        "-nographics",
        "-projectPath", project,
        "-runTests",
        "-testPlatform", "EditMode",
        "-testResults", results_path,
        "-logFile", log_path,
    ]

    try:
        proc = subprocess.run(command, timeout=TIMEOUT_SECONDS)
        exit_code = proc.returncode
    except subprocess.TimeoutExpired:
        return fail("Unity timed out after %d seconds. Log: %s" % (TIMEOUT_SECONDS, log_path))
    except OSError as e:
        return fail("could not start Unity: %s" % e)

    log_lines = read_log(log_path)

    compile_errors = [l for l in log_lines if COMPILE_ERROR.search(l)]
    if compile_errors:
        print("FAIL: compile errors (%d). Log: %s" % (len(compile_errors), ascii_text(log_path)))
        for line in compile_errors[:10]:
            print("  " + ascii_text(line.strip()))
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
