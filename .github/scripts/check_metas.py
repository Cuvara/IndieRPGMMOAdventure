#!/usr/bin/env python3
"""Every tracked file and folder Unity imports must have a committed `.meta`.

Why this is a CI gate and not a code-review habit: a missing `.meta` does not
fail a build, does not throw, and does not appear in any log. Unity silently
declines to import the asset, so an assembly definition without one is an
assembly that does not exist. That is how this package shipped seven releases
with parts of itself disabled -- green, running, and quietly incomplete.

It is the same failure family as a green test run over zero tests, which is why
it lives next to `assert_test_floors.py`: both check that something is *there*,
because both failures look exactly like success.

Unity ignores paths beginning with a dot and folders ending in `~`, so those are
skipped here for the same reason Unity skips them.
"""

import subprocess
import sys

SKIP_SUFFIXES = (".meta",)


def unity_visible(path):
    parts = path.split("/")
    for part in parts:
        if part.startswith("."):
            return False
    # Samples~, Documentation~ and friends are not imported.
    for part in parts[:-1]:
        if part.endswith("~"):
            return False
    return not parts[-1].endswith("~")


def main():
    tracked = subprocess.run(
        ["git", "ls-files"], capture_output=True, text=True, check=True
    ).stdout.splitlines()

    have = set(tracked)
    missing = []
    directories = set()

    for path in tracked:
        if not unity_visible(path) or path.endswith(SKIP_SUFFIXES):
            continue

        if path + ".meta" not in have:
            missing.append(path)

        # Directories are implied by their contents; git tracks no directory
        # entries of its own, and Unity needs a .meta for each one.
        parts = path.split("/")
        for i in range(1, len(parts)):
            directories.add("/".join(parts[:i]))

    for directory in sorted(directories):
        if not unity_visible(directory):
            continue
        if directory + ".meta" not in have:
            missing.append(directory + "/")

    if missing:
        print(f"{len(missing)} tracked path(s) have no committed .meta:")
        for path in sorted(missing):
            print(f"  {path}")
            print(f"::error file={path.rstrip('/')}::Missing {path.rstrip('/')}.meta — Unity will not import this")
        return 1

    checked = sum(1 for p in tracked if unity_visible(p) and not p.endswith(".meta"))
    print(f"All {checked} Unity-visible tracked file(s) and {len(directories)} folder(s) have a .meta.")
    return 0


if __name__ == "__main__":
    sys.exit(main())
