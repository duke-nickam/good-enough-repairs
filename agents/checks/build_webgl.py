#!/usr/bin/env python3
"""Build the WebGL version of the game through Unity and check the result.

Usage: python agents/checks/build_webgl.py <unity project path>

Runs GoodEnoughRepairs.EditorTools.WebGlBuild.Build in Unity batch mode, then
checks the folder Build/WebGL in the project:
  - index.html exists
  - the Build folder holds the loader, framework, data and wasm files
  - there is no StreamingAssets folder
  - the Unity log shows no compile error, build error, exception, data error
    or JSON error

It does not open the build in a browser: the designer plays it.
Unity's path comes only from the UNITY_PATH environment variable. The log is
written to a temp folder outside the project.
At the end it prints the folder to zip and upload.

Exit 0 when all is fine, exit 1 otherwise.
"""

import os
import re
import sys
import tempfile

from unity_common import (ascii_text, fail, find_unity, project_error,
                          read_log, report_compile_errors, run_unity)

METHOD = "GoodEnoughRepairs.EditorTools.WebGlBuild.Build"
OUTPUT_FOLDER = os.path.join("Build", "WebGL")

COMPRESSION = r"(\.(gz|br|unityweb))?$"
BUILD_FILES = [
    ("loader (.loader.js)", re.compile(r"\.loader\.js$")),
    ("framework (.framework.js)", re.compile(r"\.framework\.js" + COMPRESSION)),
    ("data (.data)", re.compile(r"\.data" + COMPRESSION)),
    ("code (.wasm)", re.compile(r"\.wasm" + COMPRESSION)),
]

# Lines of Unity's build report that list one file, for example
# "0.1 kb	 0.0% Packages/com.unity.inputsystem/.../JsonParser.cs".
# They hold file names, not errors, so they are never checked.
REPORT_LINE = re.compile(r"^\s*[\d.,]+\s*(kb|mb|gb|b)\b\s+[\d.,]+\s*%", re.IGNORECASE)

# "JSON" must be the upper-case word, so file names like JsonParser.cs
# and fsJsonParser.cs do not match.
LOG_PATTERNS = [
    ("exception", re.compile(r"^\s*[\w.]*Exception\b")),
    ("exception", re.compile(r"Unhandled exception", re.IGNORECASE)),
    ("exception", re.compile(r"Exception:")),
    ("Unity aborted", re.compile(r"Aborting batchmode due to failure")),
    ("method not found", re.compile(r"executeMethod method .* could not be found", re.IGNORECASE)),
    ("build error", re.compile(r"WebGlBuild failed|WebGlBuild: build failed")),
    ("build error", re.compile(r"Error building Player|Build completed with a result of 'Failed'")),
    ("build error", re.compile(r"BuildPlayer failed", re.IGNORECASE)),
    ("data error", re.compile(r"GameDataAsset.*(missing|not set|not found)", re.IGNORECASE)),
    ("data error", re.compile(r"\bfield '\w+' is not set", re.IGNORECASE)),
    ("JSON error", re.compile(r"\bJSON\b.*(?i:error|exception|invalid|parse|failed)")),
    ("JSON error", re.compile(r"(?i:error|exception|invalid|parse|failed).*\bJSON\b")),
]


def log_problems(log_lines):
    """Return (kind, line) pairs for the log lines that show an error."""
    found = []
    for line in log_lines:
        if line.startswith("MADE: "):
            continue
        if REPORT_LINE.search(line):
            continue
        for kind, pattern in LOG_PATTERNS:
            if pattern.search(line):
                found.append((kind, line.strip()))
                break
    return found


def folder_problems(output):
    """Return a list of messages about the built folder."""
    problems = []
    if not os.path.isdir(output):
        return ["output folder is missing: " + output]
    if not os.path.isfile(os.path.join(output, "index.html")):
        problems.append("index.html is missing in " + output)
    build_dir = os.path.join(output, "Build")
    names = []
    if os.path.isdir(build_dir):
        names = [n for n in os.listdir(build_dir) if os.path.isfile(os.path.join(build_dir, n))]
    else:
        problems.append("Build folder is missing in " + output)
    if names or os.path.isdir(build_dir):
        for label, pattern in BUILD_FILES:
            if not any(pattern.search(n) for n in names):
                problems.append("Build file missing: " + label)
    for root, dirs, _ in os.walk(output):
        for d in dirs:
            if d.lower() == "streamingassets":
                problems.append("StreamingAssets folder present: " + os.path.join(root, d))
    return problems


def main(argv):
    if len(argv) != 2:
        return fail("usage: python agents/checks/build_webgl.py <unity project path>")

    project = os.path.abspath(argv[1])
    error = project_error(project)
    if error:
        return fail(error)

    unity, error = find_unity()
    if error:
        return fail(error)

    out_dir = tempfile.mkdtemp(prefix="ger_webgl_")
    log_path = os.path.join(out_dir, "webgl-log.txt")

    exit_code, error = run_unity(unity, ["-quit", "-nographics"], project,
                                 ["-executeMethod", METHOD], log_path)
    if error:
        return fail(error)

    log_lines = read_log(log_path)
    if not log_lines:
        return fail("Unity wrote no log (exit code %d). Log: %s" % (exit_code, log_path))

    failed = False

    if report_compile_errors(log_lines, log_path):
        failed = True

    found = log_problems(log_lines)
    if found:
        failed = True
        print("FAIL: the Unity log shows errors (%d line(s)). Log: %s"
              % (len(found), ascii_text(log_path)))
        for kind, line in found[:10]:
            print("  [%s] %s" % (kind, ascii_text(line)))

    if exit_code != 0:
        failed = True
        print("FAIL: Unity exited with code %d. Log: %s" % (exit_code, ascii_text(log_path)))

    output = os.path.join(project, OUTPUT_FOLDER)
    problems = folder_problems(output)
    if problems:
        failed = True
        for p in problems:
            print("FAIL: " + ascii_text(p))

    if failed:
        return 1

    print("OK: WebGL build is complete.")
    print("Zip the contents of this folder and upload it: " + ascii_text(output))
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv))
