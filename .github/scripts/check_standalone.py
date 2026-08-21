#!/usr/bin/env python3
"""Runtime/ must not reference the host framework this package was extracted from.

The whole value of this package is that it installs, compiles and tests on its own.
That property is invisible day to day and easy to lose by accident: the natural way to
fix "I need a logger here" is to reach for the host's logger, and the natural way to fix
"I need to close the screen underneath" is to reach for the host's screen manager. Either
one compiles fine in the consuming project, where com.gdk.core is present, and passes its
tests. Nothing goes red until someone tries to install this package somewhere else — by
which time the reference has been there for weeks and is load-bearing.

So this is a gate, and it runs on Runtime/ and Tests/ but NOT on Samples~/ or docs. A
sample may legitimately mention a host by name in prose; a README certainly does.

The banned list is by symbol, not by namespace prefix alone, because the extraction
severed specific contracts and each one is a distinct way for the dependency to creep
back:

  IScreenView / IScreenViewBase / ISurfaceScreenView   the host's view contracts
  IScreenManager / IScreenPresenter / IUIView          the host's flow contracts
  IScreenViewBackend / ScreenPresenterViewType         the host's backend-selection seam
  BaseScreenPresenter / BaseScreenPresenterCore        the host's presenter hierarchy
  SignalBus                                            the host's pub-sub
  IAssetsManager                                       the host's asset loading
  ILoggerManager                                       the host's logging
  RootUICanvas / ScreenStatus                          the host's uGUI root and state enum

Replacements all live in Runtime/Core/: IUIToolkitView, IViewLayer, IViewSurface,
IVisualTreeAssetLoader, and plain C# events instead of signals.
"""

import re
import sys
from pathlib import Path

BANNED_SYMBOLS = [
    "IScreenViewBase", "ISurfaceScreenView", "IScreenViewBackend",
    "ScreenPresenterViewType", "BaseScreenPresenterCore", "BaseScreenPresenter",
    "IScreenManager", "IScreenPresenter", "IScreenView", "IUIView",
    "SignalBus", "IAssetsManager", "ILoggerManager", "RootUICanvas", "ScreenStatus",
]
BANNED_NAMESPACES = ["GameFoundation", "UniT.Logging", "UniT.ResourceManagement"]

SCANNED = ["Runtime", "Tests"]
SUFFIXES = {".cs", ".asmdef", ".uxml", ".uss"}


def main() -> int:
    root = Path(__file__).resolve().parents[2]
    symbol_re = re.compile(r"\b(" + "|".join(BANNED_SYMBOLS) + r")\b")
    ns_re = re.compile(r"\b(" + "|".join(re.escape(n) for n in BANNED_NAMESPACES) + r")\b")

    hits = []
    scanned = 0
    for top in SCANNED:
        base = root / top
        if not base.is_dir():
            continue
        for path in sorted(base.rglob("*")):
            if path.suffix not in SUFFIXES or not path.is_file():
                continue
            scanned += 1
            rel = path.relative_to(root)
            for lineno, line in enumerate(path.read_text(encoding="utf-8", errors="replace").splitlines(), 1):
                for match in (symbol_re.search(line), ns_re.search(line)):
                    if match:
                        hits.append((rel, lineno, match.group(1), line.strip()[:120]))
                        break

    print(f"scanned {scanned} files under {', '.join(SCANNED)}")

    if not hits:
        print("standalone: no host-framework references")
        return 0

    print(f"::error::{len(hits)} host-framework reference(s) found. "
          f"This package must install and compile without com.gdk.core.")
    for rel, lineno, symbol, text in hits:
        print(f"::error file={rel},line={lineno}::{symbol} — {text}")
    print()
    print("If one of these is genuinely needed, the fix is to define the contract HERE and")
    print("have the host adapt to it — not to add the reference. See README.md, "
          "'It does not depend on GameFoundation'.")
    return 1


if __name__ == "__main__":
    sys.exit(main())
