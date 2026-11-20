#!/usr/bin/env python3
"""Check that a second build with the prefab builder changes no file.

Usage: python agents/checks/check_builder_no_diff.py <builder source path> <file> [<file> ...]

  builder source  the C# file of the builder (checked to exist and to hold BuildAll).
                  The Unity project is found by walking up from it.
  file            a prefab or scene file to compare (one or more). Only these files
                  are checked.

The script runs GoodEnoughRepairs.EditorTools.PrefabSpecBuilder.BuildAll once to
settle, records the SHA-256 hash of each given file, runs the build again with no
spec change and compares. It prints PASS or FAIL for each file with both hashes.

Unity's executable path comes only from the UNITY_PATH environment variable.
Logs are written to a temp folder outside the project.

Exit 0 when every file is byte for byte the same after the second build.
Exit 1 when a file changed or is missing, or a build fails.
"""

import hashlib
import os
import re
import sys
import tempfile

from unity_common import (ascii_text, fail, find_project, find_unity, project_error,
                          read_log, report_compile_errors, run_unity)
from build_unity import EXCEPTION_PATTERNS

METHOD = "GoodEnoughRepairs.EditorTools.PrefabSpecBuilder.BuildAll"


def file_hash(path):
    """Return the SHA-256 hex digest of the file, or None if unreadable."""
    try:
        with open(path, "rb") as f:
            return hashlib.sha256(f.read()).hexdigest()
    except OSError:
        return None


def run_build(unity, project, log_path, label):
    """Run the builder once. Return an error message, or None when it went fine."""
    exit_code, error = run_unity(unity, ["-quit"], project, ["-executeMethod", METHOD], log_path)
    if error:
        return "%s build: %s" % (label, error)

    lines = read_log(log_path)
    if not lines:
        return "%s build: Unity wrote no log (exit code %d). Log: %s" % (label, exit_code, log_path)

    if report_compile_errors(lines, log_path):
        return "%s build: compile errors" % label

    exceptions = []
    for line in lines:
        if line.startswith("MADE: ") or line.startswith("SCREENSHOT: "):
            continue
        if any(p.search(line) for p in EXCEPTION_PATTERNS):
            exceptions.append(line)
    if exceptions:
        for line in exceptions[:10]:
            print("  " + ascii_text(line.strip()))
        return "%s build: exception in %s (%d line(s)). Log: %s" % (
            label, METHOD, len(exceptions), log_path)

    if exit_code != 0:
        return "%s build: Unity exited with code %d. Log: %s" % (label, exit_code, log_path)
    return None


def main(argv):
    if len(argv) < 3:
        return fail("usage: python agents/checks/check_builder_no_diff.py "
                    "<builder source path> <file> [<file> ...]")

    source = os.path.abspath(argv[1])
    if not os.path.isfile(source):
        return fail("builder source not found: " + source)

    try:
        with open(source, "r", encoding="utf-8", errors="replace") as f:
            source_text = f.read()
    except OSError as e:
        return fail("could not read builder source: %s" % e)
    if not re.search(r"\bBuildAll\s*\(", source_text):
        return fail("method BuildAll is not in " + source)

    project = find_project(source)
    if project is None:
        return fail("no Unity project above the builder source: " + source)
    error = project_error(project)
    if error:
        return fail(error)

    files = []
    for arg in argv[2:]:
        path = os.path.abspath(arg)
        if path not in files:
            files.append(path)

    unity, error = find_unity()
    if error:
        return fail(error)

    out_dir = tempfile.mkdtemp(prefix="ger_nodiff_")

    error = run_build(unity, project, os.path.join(out_dir, "build1-log.txt"), "first")
    if error:
        return fail(error)

    before = {}
    problems = []
    for path in files:
        digest = file_hash(path)
        if digest is None:
            problems.append("file missing after the first build: " + path)
        before[path] = digest

    error = run_build(unity, project, os.path.join(out_dir, "build2-log.txt"), "second")
    if error:
        problems.append(error)

    changed = 0
    for path in files:
        if before[path] is None:
            print("FAIL: %s (missing)" % ascii_text(path))
            continue
        after = file_hash(path)
        if after is None:
            print("FAIL: %s (missing after the second build)" % ascii_text(path))
            problems.append("file missing after the second build: " + path)
        elif after == before[path]:
            print("PASS: %s before=%s after=%s" % (ascii_text(path), before[path], after))
        else:
            changed += 1
            print("FAIL: %s before=%s after=%s" % (ascii_text(path), before[path], after))

    if changed:
        problems.append("%d file(s) changed by the second build" % changed)

    if problems:
        print("FAIL: " + ascii_text("; ".join(problems)) + ". Logs: " + ascii_text(out_dir))
        return 1

    print("OK: the second build changed none of the %d file(s)" % len(files))
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv))
