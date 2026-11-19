"""Helpers shared by the Unity check scripts in agents/checks/.

Used by build_unity.py, editmode.py and measure_ship_in_bay.py. The scripts
import this module as "unity_common": when a script is run as
'python agents/checks/<name>.py', Python puts the script's own folder first on
the import path, so the import works from the repo root.

Uses only the Python standard library.
"""

import os
import re
import subprocess

TIMEOUT_SECONDS = 1800
COMPILE_ERROR = re.compile(r"error CS\d+")


def ascii_text(text):
    return text.encode("ascii", "replace").decode("ascii")


def fail(message):
    """Print a FAIL line and return exit code 1."""
    print("FAIL: " + ascii_text(message))
    return 1


def read_log(path):
    """Return the log file as a list of lines, or an empty list if unreadable."""
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


def project_error(project):
    """Return an error message if the folder has no Assets folder, else None."""
    if not os.path.isdir(os.path.join(project, "Assets")):
        return "not a Unity project (no Assets folder): " + project
    return None


def find_unity():
    """Return (unity executable path, None) or (None, error message).

    The path comes only from the UNITY_PATH environment variable.
    """
    unity = os.environ.get("UNITY_PATH", "").strip()
    if not unity:
        return None, "UNITY_PATH is not set. Set it to the Unity editor executable."
    if not os.path.isfile(unity):
        return None, "UNITY_PATH points to nothing: " + unity
    return unity, None


def run_unity(unity, flags, project, args, log_path):
    """Run Unity in batch mode.

    The command is: unity -batchmode <flags> -projectPath <project> <args>
    -logFile <log_path>. Returns (exit code, None) or (None, error message)
    when Unity times out or cannot start.
    """
    command = [unity, "-batchmode"] + list(flags) + ["-projectPath", project] + list(args)
    command += ["-logFile", log_path]
    try:
        proc = subprocess.run(command, timeout=TIMEOUT_SECONDS)
        return proc.returncode, None
    except subprocess.TimeoutExpired:
        return None, "Unity timed out after %d seconds. Log: %s" % (TIMEOUT_SECONDS, log_path)
    except OSError as e:
        return None, "could not start Unity: %s" % e


def report_compile_errors(log_lines, log_path):
    """Print the compile errors from the log. Return True if there were any."""
    compile_errors = [l for l in log_lines if COMPILE_ERROR.search(l)]
    if not compile_errors:
        return False
    print("FAIL: compile errors (%d). Log: %s" % (len(compile_errors), ascii_text(log_path)))
    for line in compile_errors[:10]:
        print("  " + ascii_text(line.strip()))
    return True
