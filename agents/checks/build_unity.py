#!/usr/bin/env python3
"""Run a Unity editor builder method in batch mode and check the result.

Usage: python agents/checks/build_unity.py <method> <builder source path> [unity project path]

  method          full name of the static editor method, for example
                  GoodEnoughRepairs.EditorTools.PrefabSpecBuilder.BuildAll
  builder source  the C# file of the builder (checked to exist and to hold the method)
  project path    optional; by default the project is found by walking up from the
                  builder source path to the folder that holds Assets and ProjectSettings

Unity's executable path comes only from the UNITY_PATH environment variable.
Unity runs with -executeMethod and -quit, with graphics on (screenshots need
rendering). The log is written to a temp folder outside the project.

Output, in this order:
  "MADE: <path>"           one per file Unity made (passed through unchanged)
  "SCREENSHOT: <path>"     one per screenshot, only for specs the builder photographed
  "SKIPPED: <spec path>"   one per spec the builder did not photograph because it
                           was unchanged and already had its shot

The builder tells which is which with one line per spec: "BUILT: <spec> (screenshot
taken)", "BUILT: <spec> (no screenshot)" or "SKIPPED (unchanged): <spec>". The
SCREENSHOT lines of a spec come before its BUILT line. The check fails when no MADE
line is printed, when nothing was photographed and nothing was skipped, or when a
SCREENSHOT line does not belong to a photographed spec.

Exit 0 when all is fine, exit 1 otherwise.
"""

import os
import re
import sys
import tempfile

from unity_common import (ascii_text, fail, find_project, find_unity, project_error,
                          read_log, report_compile_errors, run_unity)

METHOD_NAME = re.compile(r"^[A-Za-z_][\w]*(\.[A-Za-z_][\w]*)+$")
EXCEPTION_PATTERNS = [
    re.compile(r"^\s*[\w.]*Exception\b"),
    re.compile(r"Unhandled exception", re.IGNORECASE),
    re.compile(r"Aborting batchmode due to failure"),
    re.compile(r"executeMethod method .* could not be found", re.IGNORECASE),
    re.compile(r"Exception:"),
]

BUILT_PREFIX = "BUILT: "
BUILT_SHOT_SUFFIX = " (screenshot taken)"
BUILT_NO_SHOT_SUFFIX = " (no screenshot)"
SKIPPED_PREFIX = "SKIPPED (unchanged): "
SHOT_PREFIX = "SCREENSHOT: "


def parse_specs(log_lines):
    """Read the per-spec lines of a builder log.

    Returns a dict:
      photographed  list of (spec path, [screenshot paths]) in log order
      built_no_shot list of spec paths that changed but took no screenshot
      skipped       list of spec paths that were unchanged and not photographed
      problems      list of messages about lines that do not fit together
    """
    result = {"photographed": [], "built_no_shot": [], "skipped": [], "problems": []}
    pending = []
    for raw in log_lines:
        line = raw.rstrip()
        if line.startswith(SHOT_PREFIX):
            pending.append(line[len(SHOT_PREFIX):].strip())
        elif line.startswith(BUILT_PREFIX):
            rest = line[len(BUILT_PREFIX):]
            if rest.endswith(BUILT_SHOT_SUFFIX):
                spec = rest[:-len(BUILT_SHOT_SUFFIX)].strip()
                if not pending:
                    result["problems"].append("spec reported as photographed but no SCREENSHOT line: " + spec)
                result["photographed"].append((spec, pending))
                pending = []
            elif rest.endswith(BUILT_NO_SHOT_SUFFIX):
                spec = rest[:-len(BUILT_NO_SHOT_SUFFIX)].strip()
                if pending:
                    result["problems"].append("SCREENSHOT line for a spec reported without a screenshot: " + spec)
                    pending = []
                result["built_no_shot"].append(spec)
        elif line.startswith(SKIPPED_PREFIX):
            spec = line[len(SKIPPED_PREFIX):].strip()
            if pending:
                result["problems"].append("SCREENSHOT line for a skipped spec: " + spec)
                pending = []
            result["skipped"].append(spec)
    if pending:
        result["problems"].append("SCREENSHOT line without a spec line: " + pending[0])
    return result


def log_failure(log_lines, log_path, method, exit_code):
    """Return a list of FAIL lines for compile errors, exceptions or a bad exit code."""
    out = []
    compile_errors = [l for l in log_lines if "error CS" in l and re.search(r"error CS\d+", l)]
    if compile_errors:
        out.append("compile errors (%d)" % len(compile_errors))
    exceptions = []
    for line in log_lines:
        if line.startswith("MADE: ") or line.startswith(SHOT_PREFIX):
            continue
        if any(p.search(line) for p in EXCEPTION_PATTERNS):
            exceptions.append(line)
    if exceptions:
        out.append("exception in %s (%d line(s)): %s"
                   % (method, len(exceptions), ascii_text(exceptions[0].strip())))
    if exit_code != 0:
        out.append("Unity exited with code %d" % exit_code)
    return out


def main(argv):
    if len(argv) not in (3, 4):
        return fail("usage: python agents/checks/build_unity.py <method> "
                    "<builder source path> [unity project path]")

    method = argv[1].strip()
    source = os.path.abspath(argv[2])

    if not METHOD_NAME.match(method):
        return fail("not a full method name (Namespace.Class.Method): " + method)
    if not os.path.isfile(source):
        return fail("builder source not found: " + source)

    try:
        with open(source, "r", encoding="utf-8", errors="replace") as f:
            source_text = f.read()
    except OSError as e:
        return fail("could not read builder source: %s" % e)

    method_short = method.rsplit(".", 1)[1]
    if not re.search(r"\b" + re.escape(method_short) + r"\s*\(", source_text):
        return fail("method %s is not in %s" % (method_short, source))

    if len(argv) == 4:
        project = os.path.abspath(argv[3])
    else:
        project = find_project(source)
        if project is None:
            return fail("no Unity project above the builder source: " + source)
    error = project_error(project)
    if error:
        return fail(error)

    unity, error = find_unity()
    if error:
        return fail(error)

    out_dir = tempfile.mkdtemp(prefix="ger_build_")
    log_path = os.path.join(out_dir, "build-log.txt")

    exit_code, error = run_unity(unity, ["-quit"], project, ["-executeMethod", method], log_path)
    if error:
        return fail(error)

    log_lines = read_log(log_path)
    if not log_lines:
        return fail("Unity wrote no log (exit code %d). Log: %s" % (exit_code, log_path))

    made = [l.rstrip() for l in log_lines if l.startswith("MADE: ")]

    if report_compile_errors(log_lines, log_path):
        return 1

    exceptions = []
    for line in log_lines:
        if line.startswith("MADE: ") or line.startswith(SHOT_PREFIX):
            continue
        if any(p.search(line) for p in EXCEPTION_PATTERNS):
            exceptions.append(line)
    if exceptions:
        print("FAIL: exception in %s (%d line(s)). Log: %s"
              % (method, len(exceptions), ascii_text(log_path)))
        for line in exceptions[:10]:
            print("  " + ascii_text(line.strip()))
        return 1

    if exit_code != 0:
        tail = [l.strip() for l in log_lines[-5:] if l.strip()]
        print("FAIL: Unity exited with code %d. Log: %s" % (exit_code, ascii_text(log_path)))
        for line in tail:
            print("  " + ascii_text(line))
        return 1

    specs = parse_specs(log_lines)
    shots = []
    for _, paths in specs["photographed"]:
        shots.extend(paths)
    skipped = specs["skipped"]

    problems = list(specs["problems"])
    if not made:
        problems.append("no MADE line was printed")
    if not shots and not skipped:
        problems.append("no spec was photographed and none was skipped")
    for path in shots:
        if not os.path.isabs(path):
            problems.append("screenshot path is not absolute: " + path)
        elif not os.path.isfile(path):
            problems.append("screenshot file does not exist: " + path)
    if problems:
        print("FAIL: " + ascii_text("; ".join(problems)) + ". Log: " + ascii_text(log_path))
        return 1

    for line in made:
        print(line)
    for path in shots:
        print(SHOT_PREFIX + path)
    for spec in skipped:
        print("SKIPPED: " + spec)
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv))
