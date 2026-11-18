#!/usr/bin/env python3
"""Run a Unity editor builder method in batch mode and check the result.

Usage: python agents/checks/build_unity.py <method> <builder source path> [unity project path]

  method          full name of the static editor method, for example
                  GoodEnoughRepairs.EditorTools.ShipInBayBuilder.Build
  builder source  the C# file of the builder (checked to exist and to hold the method)
  project path    optional; by default the project is found by walking up from the
                  builder source path to the folder that holds Assets and ProjectSettings

Unity's executable path comes only from the UNITY_PATH environment variable.
Unity runs with -executeMethod and -quit, with graphics on (screenshots need
rendering). The log is written to a temp folder outside the project.

The builder prints one line "MADE: <path>" per file Unity made and one line
"SCREENSHOT: <absolute path>" per screenshot. These lines are passed through
unchanged. The check fails when either kind is missing.

Exit 0 when all is fine, exit 1 otherwise.
"""

import os
import re
import subprocess
import sys
import tempfile

TIMEOUT_SECONDS = 1800
COMPILE_ERROR = re.compile(r"error CS\d+")
METHOD_NAME = re.compile(r"^[A-Za-z_][\w]*(\.[A-Za-z_][\w]*)+$")
EXCEPTION_PATTERNS = [
    re.compile(r"^\s*[\w.]*Exception\b"),
    re.compile(r"Unhandled exception", re.IGNORECASE),
    re.compile(r"Aborting batchmode due to failure"),
    re.compile(r"executeMethod method .* could not be found", re.IGNORECASE),
    re.compile(r"Exception:"),
]


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


def find_project(start):
    """Walk up from a file path to the folder holding Assets and ProjectSettings."""
    current = os.path.dirname(os.path.abspath(start))
    while True:
        if (os.path.isdir(os.path.join(current, "Assets"))
                and os.path.isdir(os.path.join(current, "ProjectSettings"))):
            return current
        parent = os.path.dirname(current)
        if parent == current:
            return None
        current = parent


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
    if not os.path.isdir(os.path.join(project, "Assets")):
        return fail("not a Unity project (no Assets folder): " + project)

    unity = os.environ.get("UNITY_PATH", "").strip()
    if not unity:
        return fail("UNITY_PATH is not set. Set it to the Unity editor executable.")
    if not os.path.isfile(unity):
        return fail("UNITY_PATH points to nothing: " + unity)

    out_dir = tempfile.mkdtemp(prefix="ger_build_")
    log_path = os.path.join(out_dir, "build-log.txt")

    command = [
        unity,
        "-batchmode",
        "-quit",
        "-projectPath", project,
        "-executeMethod", method,
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
    if not log_lines:
        return fail("Unity wrote no log (exit code %d). Log: %s" % (exit_code, log_path))

    made = [l.rstrip() for l in log_lines if l.startswith("MADE: ")]
    shots = [l.rstrip() for l in log_lines if l.startswith("SCREENSHOT: ")]

    compile_errors = [l for l in log_lines if COMPILE_ERROR.search(l)]
    if compile_errors:
        print("FAIL: compile errors (%d). Log: %s" % (len(compile_errors), ascii_text(log_path)))
        for line in compile_errors[:10]:
            print("  " + ascii_text(line.strip()))
        return 1

    exceptions = []
    for line in log_lines:
        if line.startswith("MADE: ") or line.startswith("SCREENSHOT: "):
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

    problems = []
    if not made:
        problems.append("no MADE line was printed")
    if not shots:
        problems.append("no SCREENSHOT line was printed")
    for line in shots:
        path = line[len("SCREENSHOT: "):].strip()
        if not os.path.isabs(path):
            problems.append("screenshot path is not absolute: " + path)
        elif not os.path.isfile(path):
            problems.append("screenshot file does not exist: " + path)
    if problems:
        print("FAIL: " + ascii_text("; ".join(problems)) + ". Log: " + ascii_text(log_path))
        return 1

    for line in made + shots:
        print(line)
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv))
