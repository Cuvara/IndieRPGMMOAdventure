---
name: verify-a-result
description: Use before reporting any measurement, benchmark, CI verdict, counter reading, or "it works now" claim on this project. Turns a plausible number into evidence. Also use when a result looks clean, a check is green, or a counter reads zero.
---

# Verify a result before believing it

Every expensive defect on this project produced **a plausible number instead of an error**.
This skill is the checklist that catches that. Full incident history and costs:
`rpg-mmo-server/backend/docs/MEASUREMENT.md`.

Run the checks that apply. Each one is cheap; each one has caught a real failure here.

## 1. Could this result be empty rather than good?

- [ ] Did the instrument return **any rows at all**? Get a non-empty result out of it first.
- [ ] CI: count **passes**, never the absence of failures. `gh pr checks` prints *nothing*
      for a CONFLICTING PR, and nothing for checks that have not registered yet.
- [ ] A counter reading `0`: prove it can be non-zero. Is it reset between the thing that
      writes it and the thing that reads it?
- [ ] A test run: does the runner report a **non-zero executed count**? `dotnet test` exits 0
      when it matched no tests.
- [ ] A gate that matches nothing must **fail**, not pass.

## 2. Am I measuring the object I am talking about?

- [ ] Name the entity the number describes. Player predictor counters say nothing about mobs.
- [ ] If the claim is about entities, is the counter counting entities — or frames?
- [ ] Could the thing legitimately be still / empty / absent for a reason that is not the
      defect? (An entity at rest. An AOI that is correctly empty. A despawn versus a death.)

## 3. Does my control differ in exactly one thing?

- [ ] Same build. Same scene. Same population. Same observer position.
- [ ] Prefer **one binary with a runtime flag** over two builds.
- [ ] Check the control actually exercises the code: a workflow run with one job that never
      invokes Unity is a green control that means nothing.
- [ ] Separate build outputs. Two projects in one directory share `obj/` and `bin/`, and the
      baseline will serve the modified binary.

## 4. Can the test fail?

- [ ] Mutate the logic, confirm **that specific test** goes red, restore, re-verify green.
- [ ] Force a rebuild between mutations — MSBuild resolves timestamps to one second and will
      silently test the previous binary.
- [ ] Is the mutation drawn from the same assumption as the code? Then it proves nothing.
      Mutate the shape that actually shipped and failed.
- [ ] Upper bounds need a lower-bound partner: a build running at half rate satisfies every
      "no more than" assertion.

## 5. Did the command actually do anything?

- [ ] After a push: `git ls-remote`. After a config change: read it back from the API.
      After an env change: read it off `/status` on the running process.
- [ ] `git rev-parse` prints its argument and exits 128 on a missing ref — `git cat-file -e`
      first.
- [ ] `gh pr edit --body-file` silently no-ops here; use `gh api -X PATCH` and read the body
      back.
- [ ] A CI **re-run replays the same commit** — it cannot prove a fix pushed afterwards.

## 6. Is the configuration actually reaching the process?

- [ ] Is every `GAMESERVER_*` variable listed in the compose `environment:` block — in
      **both** `gameserver-dotnet` and `gameserver-dotnet-map02`, which inherits nothing?
- [ ] Read the value back off `/status`. Sensible defaults make an inert knob invisible.

## 7. Is a number in the docs still true?

- [ ] Did this change alter what an existing figure **means**? Grep for the old meaning and
      rewrite what you find; do not delete the superseded reasoning, quote it.
- [ ] Is the result **contingent** on something that could change? Say so, in the same
      sentence as the number.

## Before reporting

State plainly: what was measured, on what build, against which control, and what the result
would look like **if the thing being measured were broken**. If that last answer is "the
same", it is not evidence yet — say so instead of reporting it.

## Machine-specific instruments that lie

`timeout.exe` (returns instantly), `tasklist`/`taskkill` and `powershell Start-Sleep` (fail
silently when WSL interop degrades — bracket waits with `date`), the WSL `docker` wrapper
(use `docker.exe` by full path, Windows paths for `-f`), `tail -f` on `/mnt/e` (no inotify),
Unity batch-mode exit codes (0 with failing tests; `result=Failed` still leaves a plausible
`.exe`), and any rendered diff (prove sameness with `cmp` or a hash).

A **running player locks `lib_burst_generated.dll`** and fails the next build while leaving
the previous working executable in place.
