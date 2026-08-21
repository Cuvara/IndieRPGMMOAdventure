#!/usr/bin/env python3
"""Every USS class name this package exports must start with `cuvara-`.

This is an ALLOWLIST, and that is the whole point of it.

`check_standalone.py` enumerates forbidden host prefixes. It has now been wrong twice, in
the same way both times, and each escape was a SHORTER abbreviation than the one on the
list:

  * it passed a Runtime/ shipping `gdk-grid-row`, `gdk-grid-cell` and
    `gdk-multi-template-shell` — caught by a person, then `gdk` was added;
  * it then passed a Runtime/ still shipping `gf-safe-area` — `gf` for GameFoundation,
    two letters, not on the list.

A denylist can only ban the abbreviations somebody thought of. An allowlist bans everything
nobody chose, which is the set the mistakes actually live in.

This matters more than an internal name would. A USS class name is **public API**: consumers
write stylesheets against it, so renaming one after release breaks their styling silently —
their rules simply stop matching and the screen renders unstyled.

Scanned:
  Runtime/**/*.uss    class selectors            .foo-bar
  Runtime/**/*.uxml   class attributes           class="foo-bar baz"
  Runtime/**/*.cs     string literals assigned to a *UssClassName member, and the argument
                      of AddToClassList / RemoveFromClassList / EnableInClassList / ToggleInClassList

Exempt, deliberately:
  unity-*   Unity's own control classes; we do not own that namespace and must not reprefix it
  cuvara-*  ours, the point of the check
"""

import re
import sys
from pathlib import Path

REQUIRED = "cuvara-"
EXEMPT_PREFIXES = ("unity-", REQUIRED)

USS_SELECTOR = re.compile(r"(?<![\w.#-])\.([a-zA-Z_][\w-]*)")
UXML_CLASS = re.compile(r'\bclass\s*=\s*"([^"]*)"')
CS_CONST = re.compile(r'\b\w*UssClassName\w*\s*=\s*"([^"]+)"')
CS_CALL = re.compile(r'\b(?:AddToClassList|RemoveFromClassList|EnableInClassList|ToggleInClassList)\s*\(\s*"([^"]+)"')


def offending(name: str) -> bool:
    return bool(name) and not name.startswith(EXEMPT_PREFIXES)


def main() -> int:
    root = Path(__file__).resolve().parents[2]
    runtime = root / "Runtime"
    if not runtime.is_dir():
        print("no Runtime/ to scan")
        return 0

    hits, scanned = [], 0
    for path in sorted(runtime.rglob("*")):
        if path.suffix not in {".uss", ".uxml", ".cs"} or not path.is_file():
            continue
        scanned += 1
        rel = path.relative_to(root)
        for lineno, line in enumerate(path.read_text(encoding="utf-8", errors="replace").splitlines(), 1):
            names = []
            if path.suffix == ".uss":
                names = USS_SELECTOR.findall(line)
            elif path.suffix == ".uxml":
                names = [n for attr in UXML_CLASS.findall(line) for n in attr.split()]
            else:
                names = CS_CONST.findall(line) + CS_CALL.findall(line)
            for n in names:
                if offending(n):
                    hits.append((rel, lineno, n, line.strip()[:110]))

    print(f"scanned {scanned} files under Runtime/")

    if not hits:
        print(f"uss-prefix: every exported class name starts with '{REQUIRED}'")
        return 0

    print(f"::error::{len(hits)} USS class name(s) without the '{REQUIRED}' prefix. "
          f"These are public API — consumers style against them, so renaming after release "
          f"breaks their stylesheets silently.")
    for rel, lineno, name, text in hits:
        print(f"::error file={rel},line={lineno}::'{name}' — {text}")
    return 1


if __name__ == "__main__":
    sys.exit(main())
