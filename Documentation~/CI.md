# What CI checks, and what it cannot

## The package is standalone, and that is what makes CI possible

An earlier draft of this package extended GameFoundation: every runtime file referenced it,
and GameFoundation lives in a private repository this org's Actions cannot fetch. A package
in that shape **cannot be compiled or tested by its own CI at all** — the job dies at package
resolution, before a line of the package is read, and it dies that way forever.

Severing that dependency is what bought the jobs below. It is worth stating plainly, because
the cost of the severing was real and this is the return on it.

## What runs

| Job | What it proves | Gates? |
|---|---|---|
| `Validate package` | `package.json` shape, no unresolvable dependency declared, CHANGELOG currency, `.meta` coverage, **the standalone gate** | yes |
| `Unity Tests` | the package compiles and its tests pass in a project built from its own declarations | yes |
| `Install probe (documented)` | a consumer following the README can compile it | yes |
| `Install probe (bare)` | what a consumer sees with no scoped registry | no — informational, expected to fail |

## The three checks that exist because something got through

**The standalone gate** (`.github/scripts/check_standalone.py`) fails on any reference to
GameFoundation or its logging and resource assemblies under `Runtime/` or `Tests/`, by symbol,
by namespace, and by substring.

The substring list was added after the gate passed a `Runtime/` that still shipped the USS
class names `gdk-grid-row`, `gdk-grid-cell` and `gdk-multi-template-shell` — `gdk` being
GameDevelopmentKit, the framework this package came out of. They are string literals, so no
symbol and no namespace matched, and a green gate reported the severing complete while a
consumer's stylesheet would have been written against the previous vendor's prefix
permanently. A person caught it; the script did not. That is the whole reason the third list
exists.

**The `.meta` gate.** A missing `.meta` does not fail a build here and shows in no test result
— it fails a *consumer's* whole suite, because Unity logs an Error and the test framework
turns an unexpected log error into an exception. This package's first tranche shipped five
assets with no `.meta` at all and fifteen more carrying only `fileFormatVersion` and `guid`.

**The assertion that tests actually ran.** The runner is started with `USE_EXIT_CODE=false`,
so Unity exiting 0 means only that Unity started and stopped. It exits 0 on *"No tests were
executed"* as well, and the published check goes neutral rather than red. A repository can run
green for its entire history while executing nothing. The gate parses the result XML and fails
on `total == 0`.

## What CI still does not prove

Stated because a green run is easy to over-read:

- **No device, no rendering.** No screenshot, no Game View, no notched screen. Every safe-area
  test injects the rect it is given; nothing verifies what a real device reports.
- **No real input.** The back-navigation tests synthesise `NavigationCancelEvent`. That a real
  Android back press, a gamepad B, or Escape produces one at the panel root is Unity dispatch
  behaviour and is untested here.
- **No IL2CPP and no stripping.** Nothing is built for Android or WebGL.
- **`Samples~` is not compiled.** Unity does not import a `Samples~` directory until the
  Package Manager copies it under `Assets/`, so sample code has never been through a compiler
  in this repository.

## A note on the bare probe

`Install probe (bare)` is expected to fail and is marked `continue-on-error`. Be careful
reading its result: a job carrying `continue-on-error` at job level reports **success**
whatever happens inside it, which makes a green tick there mean nothing at all. Read its
summary, not its status. Only the `documented` row gates.
