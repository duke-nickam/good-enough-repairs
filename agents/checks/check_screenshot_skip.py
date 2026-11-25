#!/usr/bin/env python3
"""Check that the prefab builder photographs only the specs that changed.

Usage: python agents/checks/check_screenshot_skip.py <builder source path> <spec path>

  builder source  the C# file of the builder (checked to exist and to hold BuildAll).
                  The Unity project is found by walking up from it.
  spec path       one *.spec.json that asks for a screenshot (scene.screenshot). One
                  of its properties is changed for the third build.

Steps (each runs GoodEnoughRepairs.EditorTools.PrefabSpecBuilder.BuildAll):
  1. Build once to settle (every spec gets its shot if it has none).
  2. Build again with no spec change. Passes when no SCREENSHOT line is printed and
     every spec under Assets/ is reported as skipped.
  3. Change the posY of the first object in the given spec, build. Passes when only
     that spec is photographed and every other spec is skipped.
  4. Restore the spec, its prefab and its scene byte for byte (always, even after a
     failure) and compare the hashes, so no Git diff is left.

Prints PASS or FAIL for each step with the specs named. Unity's path comes only from
the UNITY_PATH environment variable. Logs go to a temp folder outside the project.

Exit 0 when all steps pass, exit 1 otherwise.
"""

import hashlib
import json
import os
import re
import sys
import tempfile

from unity_common import (ascii_text, fail, find_project, find_unity, project_error,
                          read_log, report_compile_errors, run_unity)
from build_unity import parse_specs, log_failure

METHOD = "GoodEnoughRepairs.EditorTools.PrefabSpecBuilder.BuildAll"
SPEC_SUFFIX = ".spec.json"


def rel(project, path):
    return os.path.relpath(path, project).replace("\\", "/")


def find_specs(project):
    found = []
    for folder, _, names in os.walk(os.path.join(project, "Assets")):
        for name in names:
            if name.endswith(SPEC_SUFFIX):
                found.append(rel(project, os.path.join(folder, name)))
    return sorted(found)


def read_bytes(path):
    try:
        with open(path, "rb") as f:
            return f.read()
    except OSError:
        return None


def digest(data):
    return None if data is None else hashlib.sha256(data).hexdigest()


def build(unity, project, log_path, label):
    """Run BuildAll. Return (parsed spec lines, error message or None)."""
    exit_code, error = run_unity(unity, ["-quit"], project, ["-executeMethod", METHOD], log_path)
    if error:
        return None, "%s build: %s" % (label, error)
    lines = read_log(log_path)
    if not lines:
        return None, "%s build: Unity wrote no log (exit code %d). Log: %s" % (label, exit_code, log_path)
    if report_compile_errors(lines, log_path):
        return None, "%s build: compile errors" % label
    bad = log_failure(lines, log_path, METHOD, exit_code)
    if bad:
        return None, "%s build: %s. Log: %s" % (label, "; ".join(bad), log_path)
    return parse_specs(lines), None


def judge(name, parsed, all_specs, expected_photographed):
    """Compare a build with the expected set of photographed specs.

    Return a list of problem messages (empty means the step passes).
    """
    problems = list(parsed["problems"])
    photographed = [s for s, _ in parsed["photographed"]]
    skipped = parsed["skipped"]
    handled = set(photographed) | set(skipped) | set(parsed["built_no_shot"])

    for spec in photographed:
        if spec not in expected_photographed:
            problems.append("unchanged spec was photographed: " + spec)
    for spec in expected_photographed:
        if spec not in photographed:
            problems.append("changed spec was not photographed: " + spec)
    for spec in all_specs:
        if spec in expected_photographed:
            continue
        if spec in skipped:
            continue
        if spec in parsed["built_no_shot"]:
            problems.append("spec was rebuilt as changed, not skipped: " + spec)
        elif spec not in handled:
            problems.append("spec has no BUILT or SKIPPED line: " + spec)
    return problems


def report(step, problems):
    if problems:
        print("FAIL: %s" % step)
        for p in problems:
            print("  " + ascii_text(p))
        return False
    print("PASS: %s" % step)
    return True


def main(argv):
    if len(argv) != 3:
        return fail("usage: python agents/checks/check_screenshot_skip.py "
                    "<builder source path> <spec path>")

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

    spec_file = os.path.abspath(argv[2])
    if not os.path.isfile(spec_file):
        return fail("spec not found: " + spec_file)
    spec_rel = rel(project, spec_file)
    if not spec_rel.endswith(SPEC_SUFFIX):
        return fail("not a spec file (needs the suffix %s): %s" % (SPEC_SUFFIX, spec_rel))

    original_spec = read_bytes(spec_file)
    try:
        data = json.loads(original_spec.decode("utf-8"))
    except (ValueError, UnicodeDecodeError) as e:
        return fail("spec is not readable JSON: %s" % e)

    scene = data.get("scene") or {}
    if not scene.get("screenshot"):
        return fail("spec asks for no screenshot (scene.screenshot): " + spec_rel)
    objects = data.get("objects") or []
    if not objects:
        return fail("spec has no objects to change: " + spec_rel)

    guarded = {spec_file: original_spec}
    for key_path in (data.get("prefabPath"), scene.get("path")):
        if key_path:
            full = os.path.join(project, key_path)
            guarded[full] = read_bytes(full)
    before = {p: digest(b) for p, b in guarded.items()}

    all_specs = find_specs(project)
    if spec_rel not in all_specs:
        return fail("spec is not under Assets: " + spec_rel)

    unity, error = find_unity()
    if error:
        return fail(error)

    out_dir = tempfile.mkdtemp(prefix="ger_skip_")
    ok = True

    try:
        parsed, error = build(unity, project, os.path.join(out_dir, "build1-log.txt"), "first")
        if error:
            return fail(error)

        parsed, error = build(unity, project, os.path.join(out_dir, "build2-log.txt"), "second")
        if error:
            return fail(error)
        problems = judge("second", parsed, all_specs, [])
        if parsed["photographed"]:
            problems.append("SCREENSHOT lines printed on a build with no spec change: %d"
                            % sum(len(p) for _, p in parsed["photographed"]))
        if not report("second build, no spec change: nothing photographed, all %d spec(s) skipped"
                      % len(all_specs), problems):
            ok = False

        changed = json.loads(original_spec.decode("utf-8"))
        first = changed["objects"][0]
        first["posY"] = round(float(first.get("posY", 0)) + 0.1, 3)
        with open(spec_file, "wb") as f:
            f.write((json.dumps(changed, indent=2) + "\n").encode("utf-8"))

        parsed, error = build(unity, project, os.path.join(out_dir, "build3-log.txt"), "third")
        if error:
            return fail(error)
        problems = judge("third", parsed, all_specs, [spec_rel])
        for spec, paths in parsed["photographed"]:
            if spec == spec_rel and len(paths) != 1:
                problems.append("expected one SCREENSHOT line for %s, got %d" % (spec, len(paths)))
        if not report("third build, %s changed: only that spec photographed" % spec_rel, problems):
            ok = False
    finally:
        for path, content in guarded.items():
            if content is not None:
                try:
                    with open(path, "wb") as f:
                        f.write(content)
                except OSError as e:
                    print("FAIL: could not restore %s: %s" % (ascii_text(path), e))
                    ok = False

    problems = []
    for path in guarded:
        if digest(read_bytes(path)) != before[path]:
            problems.append("file differs from the original: " + rel(project, path))
    if not report("restore: spec, prefab and scene are byte for byte as before", problems):
        ok = False

    if not ok:
        print("FAIL: screenshot skip check. Logs: " + ascii_text(out_dir))
        return 1
    print("OK: only changed specs are photographed, unchanged specs are skipped")
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv))
