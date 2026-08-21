# What CI here can and cannot check

## The gap

This package extends GameFoundation. All 19 runtime files reference `com.gdk.core`, which
is a git **submodule** of `git@github.com:NightHowlGames/GameFoundation.git` — a private
repository in a different organisation.

`com.cuvara.netcode` gets a real Unity CI because everything it needs resolves from a
public URL or a public registry: it bootstraps a throwaway project, writes a
`manifest.json`, and runs the tests. The same job here fails at package resolution, before
a line of this package is compiled, because Cuvara's Actions have no credentials for
NightHowlGames.

**A job that always fails for a reason unrelated to the code is worse than no job.** It
trains everyone to ignore a red check, and the day the code genuinely breaks the signal is
already discarded. So that job is not present, and this file exists so its absence is a
recorded decision rather than an oversight.

## What runs

| Check | Catches |
|---|---|
| `package.json` shape | a missing required field |
| CHANGELOG has the current version | a version bump with no entry |
| every Unity-visible file has a `.meta` | see below — this one has cost real time |
| asmdef names are printed | an accidental rename |

The `.meta` gate is the one that earns its place. A missing `.meta` does not fail a build,
does not throw, and does not appear in this repository's test results — it fails a
**consumer's** suite, because Unity logs an Error and the test framework turns an
unexpected log error into an exception. `com.cuvara.dots` hit exactly this with 137/137
EditMode and 29/29 PlayMode green and not one failing test, which is the state in which
people conclude the runner is flaky and re-run it.

This package shipped its first tranche with 5 missing `.meta` files and 15 more that
carried only `fileFormatVersion` and `guid` with no `MonoImporter` block. Both were caught
by hand before the first push; the gate is so the next one is not.

## Where the tests actually run

In `IndieRPGMMOAdventure`, where `com.gdk.core` is checked out as a submodule and the UI
Toolkit tests run as part of `Unity Tests (All)`. **That is the real test signal for this
code.** Treat a green check here as "the packaging is well-formed", nothing more.

## What would unblock a full CI run

Exactly one thing: read access to `NightHowlGames/GameFoundation` from this repository's
Actions — a deploy key or a fine-grained PAT stored as a Cuvara secret. That is a
cross-organisation credential decision, not a technical one, which is why it has not been
done unilaterally.

With that in place, the netcode-shaped `test` job becomes possible: bootstrap a project,
add the OpenUPM scoped registry for `com.cysharp` and `com.frostbun`, check out
GameFoundation into `Packages/com.gdk.core`, and run EditMode + PlayMode. Note the same
trap netcode documents — the runner is started with `USE_EXIT_CODE=false`, so Unity exiting
0 means only that Unity started and stopped. It exits 0 on "No tests were executed" too.
Any test job added here must parse the result XML and fail on `total == 0`.

The alternative unblock — severing the dependency on GameFoundation so the package stands
alone — is not a packaging change. It would mean this package declaring its own screen,
presenter and DI abstractions and GameFoundation adapting to them, which is a redesign of
the seam, not a build fix.
