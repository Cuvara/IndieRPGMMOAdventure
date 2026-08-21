#!/usr/bin/env python3
"""Samples~ is invisible to Unity, so nothing type-checks or asset-checks it.

`package.json` declares both samples, which is a promise they work. But a directory whose
name ends in `~` is skipped by the asset database entirely: no import, no compile, no
missing-reference warning. A sample can rot for months and every gate in this repository
will stay green, because none of them can see it.

The compiler half of that gap needs Unity and is checked in CI by compiling the samples in
a throwaway assembly (see Documentation~/CI.md). This script covers the half a compiler
would not catch even if it ran:

  1. every name a sample's C# looks up with `Q<T>("name")` exists in a sibling `.uxml`
  2. every `<Style src="...">` points at a file that exists
  3. every sample directory declared in package.json is actually present, and has a README

(1) is the one that matters. A `Q<Label>("titel")` compiles perfectly, returns null at
runtime, and fails with a NullReferenceException on a line nowhere near the typo — in code
someone copied into their own project believing it was a working reference. That is a worse
outcome than a sample that does not build, because it fails after adoption rather than
before it.

The check is deliberately shallow: it does not parse C# or UXML properly, it greps. A
sample that builds element names dynamically will produce a false positive here; if that
ever happens, the right fix is to list the name as a literal somewhere the grep can see it,
because a sample whose element wiring cannot be read at a glance is not doing its job.
"""

import json
import re
import sys
from pathlib import Path

QUERY_RE = re.compile(r'\.Q<[A-Za-z_][A-Za-z0-9_<>, ]*>\(\s*"([^"]+)"')
NAME_RE = re.compile(r'\bname="([^"]+)"')
STYLE_RE = re.compile(r'<Style\s+src="([^"]+)"')


def check_sample(sample_dir: Path) -> list[str]:
    problems = []

    uxml_names = set()
    for uxml in sample_dir.rglob("*.uxml"):
        text = uxml.read_text(encoding="utf-8", errors="replace")
        uxml_names.update(NAME_RE.findall(text))

        for src in STYLE_RE.findall(text):
            if not (uxml.parent / src).is_file():
                problems.append(f"{uxml.name}: <Style src=\"{src}\"> points at a file that does not exist")

    for source in sample_dir.rglob("*.cs"):
        text = source.read_text(encoding="utf-8", errors="replace")
        for lineno, line in enumerate(text.splitlines(), 1):
            for queried in QUERY_RE.findall(line):
                if queried not in uxml_names:
                    problems.append(
                        f"{source.name}:{lineno}: queries element \"{queried}\", "
                        f"which no .uxml in this sample defines"
                    )

    if not (sample_dir / "README.md").is_file():
        problems.append("no README.md — a sample nobody can follow is not a sample")

    return problems


# Unity's built-in objects live in these two pseudo-GUIDs. A reference to one is not a
# reference to a file, so it cannot dangle and must not be flagged.
BUILTIN_GUIDS = {
    "0000000000000000e000000000000000",   # built-in extra / editor resources
    "0000000000000000f000000000000000",   # default resources
}

GUID_RE = re.compile(r"guid:\s*([0-9a-f]{32})")


def check_self_contained(sample_dir: Path) -> list:
    """Every asset GUID a sample references must be declared by a .meta inside that sample.

    A sample is copied into a consumer's project on import, and nothing outside it comes
    along. So a reference that points anywhere else — most easily into the developing
    project's own Assets/ — arrives broken on the other side.

    This is not hypothetical and it is why the check exists. Samples~/ScreenFlow shipped a
    PanelSettings whose themeUss pointed at
    Assets/UI Toolkit/UnityThemes/UnityDefaultRuntimeTheme.tss — a file Unity generated in
    the DEVELOPING project, outside the package, and which git was not even tracking. The
    sample looked complete, compiled cleanly, and passed every other gate here; the panel
    would simply have rendered unthemed for whoever imported it, with nothing logged.

    The failure mode is what makes it worth a gate: a missing theme does not throw. UI Toolkit
    falls back to no theme and carries on, so the only symptom is that the sample looks wrong
    on somebody else's machine and right on yours.
    """
    # Ownership is the PACKAGE, not just the sample directory. A sample legitimately
    # references the package's own scripts — a scene naming RootUIDocument, say — and those
    # come along because the package is the dependency being sampled. What must not happen
    # is a reference reaching OUTSIDE the package, into the developing project's Assets/,
    # which travels nowhere.
    #
    # The first version of this check scoped ownership to the sample folder and immediately
    # flagged two correct references. Recorded because the narrower rule reads more rigorous
    # and is simply wrong.
    package_root = sample_dir.parents[1]

    owned = set()
    for meta in package_root.rglob("*.meta"):
        for line in meta.read_text(encoding="utf-8", errors="replace").splitlines():
            if line.startswith("guid:"):
                owned.add(line.split(":", 1)[1].strip())
                break

    problems = []
    for path in sorted(sample_dir.rglob("*")):
        if not path.is_file() or path.suffix in {".meta", ".md", ".cs"}:
            continue
        rel = path.relative_to(sample_dir)
        text = path.read_text(encoding="utf-8", errors="replace")
        for guid in sorted(set(GUID_RE.findall(text))):
            if guid in owned or guid in BUILTIN_GUIDS:
                continue
            problems.append(
                f"{rel} references guid {guid}, which no .meta in this sample declares — "
                f"the reference will dangle once the sample is imported elsewhere")
    return problems


def main() -> int:
    root = Path(__file__).resolve().parents[2]
    manifest = json.loads((root / "package.json").read_text(encoding="utf-8"))

    declared = [entry["path"] for entry in manifest.get("samples", [])]

    if not declared:
        print("package.json declares no samples; nothing to check")
        return 0

    all_problems = []
    checked = 0

    for rel in declared:
        sample_dir = root / rel

        if not sample_dir.is_dir():
            all_problems.append((rel, [f"declared in package.json but not present on disk"]))
            continue

        checked += 1
        problems = check_sample(sample_dir) + check_self_contained(sample_dir)

        if problems:
            all_problems.append((rel, problems))

    print(f"checked {checked} of {len(declared)} declared sample(s)")

    if not all_problems:
        print("samples: element names resolve, styles resolve, READMEs present, "
              "and every referenced asset ships inside its own sample")
        return 0

    total = sum(len(problems) for _, problems in all_problems)
    print(f"::error::{total} problem(s) in declared samples. package.json promises these work.")

    for rel, problems in all_problems:
        for problem in problems:
            print(f"::error file={rel}::{problem}")

    return 1


if __name__ == "__main__":
    sys.exit(main())
