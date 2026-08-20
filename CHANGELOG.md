# Changelog

All notable changes to this project are documented here.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Added
- **The vendor drift check now covers `com.cuvara.dots` as well as `com.cuvara.netcode`.** Both jobs
  became a two-leg matrix and the workflow was renamed `netcode-vendor-drift.yml` ->
  `vendor-drift.yml`, since it is no longer about one package. `fail-fast` is off: one package
  drifting says nothing about the other, and cancelling the second leg would hide an independent
  divergence behind the first one found.

  `com.cuvara.dots` is vendored on exactly the same terms as netcode — copied content, no shared git
  history, no submodule — and it had been uncovered for as long as it existed. Two consequences were
  already sitting in the tree when the check was extended, and neither was visible from inside the
  client:

  1. the vendored copy declared **0.21.0** while upstream had reached **0.23.0**; and
  2. at its *own* declared version it did not match upstream `v0.21.0` either — two folder `.meta`
     files had been repaired here and the repair never went back, so the copy silently differed from
     the tag it claimed to be.

  The second is the failure the check exists to catch, and it was found by hand. A check covering one
  of two vendored packages reads, to anyone glancing at a green run, as if it covers vendoring.

### Changed
- **`--nakama-key` is now required when running clients by hand.** The Nakama server keys were
  rotated on 2026-08-20 and each backend cluster has its own, so the flag's `defaultkey` default
  no longer authenticates anywhere.

  Documented rather than defaulted differently, because there is no value that would be right for
  every backend. The failure mode is the reason it is called out: omitting it does not fail at
  launch — the player starts, the window opens, and authentication returns 401, which shows up as
  a client that never reaches `IN WORLD` rather than as anything naming the key.

  `Tools/run-clients.sh` already accepted `--nakama-key`; only the documented invocation needed
  it. The server-side verification harness reads the key from the cluster and exports
  `CUVARA_NAKAMA_SERVER_KEY` itself, so this applies to manual runs only.

- **`Packages/com.cuvara.dots` re-vendored 0.21.0 -> 0.23.1**, byte-identical to upstream `v0.23.1`.

  The substantive change is upstream `0.23.0`: `LocalPredictionSystem` never called
  `SeedBaseTick`, so netcode's #13 fix had no effect on the DOTS path — the only path the DOTS
  sample actually runs. netcode v0.16.0 added that call and wired its own `WorldViewBinder`; its
  CHANGELOG states that a consumer binding views itself must call it "or the feature is inert and you
  keep the defect". This system was that consumer.

  Upstream `0.23.1` is the `.meta` repair described above, which is what makes the vendored copy
  byte-identical rather than merely current.

  `packages-lock.json` needs no edit: both packages are `embedded` (`file:`), so the lock carries no
  version to bump — only the dependency set, which is unchanged. The `versionDefines` expression
  gating `CUVARA_NETCODE` moved `0.8.0` -> `0.15.0` with upstream; the vendored netcode is `0.16.1`,
  so the define still fires.

  **Not measured.** The prediction improvement is a mechanism documented by netcode plus a missing
  call that is a matter of fact, not a before/after run against a live backend.

### Added
- **`netcode-vendor-drift` gains a second job: the imported samples must match the package.** The
  existing job compares `Packages/` against the upstream *release*; this one compares
  `Assets/Samples/` against `Packages/`. Both were green while a built player ignored every
  `-cuvara-*` flag, because the fault sat between them and neither was looking there.
  It fails on any of: an imported version that is not the package's, two imported versions
  coexisting, an import whose content differs from its `Samples~` source, an import of a sample the
  package no longer declares, or an `EditorBuildSettings` scene under a stale import — that last
  being the one that actually shipped. Samples are read from `package.json`'s `samples[]` rather
  than hardcoded, so a new sample is covered the day it is declared.
  Verified by reconstructing the exact defect in a scratch tree: it reports all three faces of it
  and exits 1, and reports clean on the fixed project.

### Fixed
- **The built player ignored every `-cuvara-*` backend flag, because the imported sample lagged the
  package.** `Samples~` carries a `~`, so Unity never imports it; the copy Unity actually compiles
  lives in `Assets/Samples/`, is made once at import time, and **does not update when the package
  is vendored**. The project held two stale imports: `0.15.5` (complete except `BackendCommandLine`)
  and `0.15.0` (two hand-dropped files with their own GUIDs, no asmdef, landing in
  `Assembly-CSharp`). `EditorBuildSettings` pointed scene 0 at the `0.15.5` copy, so a built player
  fell back to Nakama's default `7350` and could never be pointed at a backend.
  Replaced both with a single `0.16.1` import taken from the package. GUIDs are identical between
  package and import (verified per file), so scene references survive the swap untouched — and the
  scene itself was byte-identical to the package's, so no project-local customisation was lost.
  **This is the vendoring problem one level deeper**, and the netcode drift check cannot see it:
  that check compares `Packages/` against the upstream release and says nothing about whether
  `Assets/Samples/` matches `Packages/`.
  Verified end to end against a live backend: three players, three distinct Nakama users, full
  ADR-3 flow — device auth, gateway auth, `map_01` assigned to the Agones-assigned port, direct
  dial, `IN WORLD`, prediction on — and zero references to the default port in any client log.

### Changed
- **Vendored `com.cuvara.netcode` bumped to upstream `v0.16.1`; the drift check now reports zero.**
  `BackendCommandLine` and the DOTS sample's use of it were the last client-only difference. They
  are upstream now, so the vendored copy is **byte-for-byte its upstream release** — 13 differing
  paths at the start of the day, 1 after v0.16.0, **0** now.
  `.vendor-client-only` is kept but empty, with a comment saying why: an empty allowlist is a
  statement, and the drift check reads the file. Anything added back to it needs a reason written
  next to it.
  Upstreaming rather than exempting was the right call because the allowlist deliberately cannot
  exempt a file that *differs* — only one that is *absent* — so the alternative was a check that
  reported the same known difference every week, which is how a check gets ignored.
  EditMode: **391/391**, unchanged from v0.16.0.
- **`netcode-vendor-drift` died on an allowlist containing only comments.** Emptying
  `.vendor-client-only` after upstreaming the last exemption turned a clean run into a bare
  `exit 1` with no summary and no error line. `grep` exits 1 when it matches nothing, and under
  `set -o pipefail` plus the `-e` GitHub adds to `shell: bash` that killed the job before it
  compared anything. An all-comments allowlist is a **valid** state — it means nothing is exempt —
  so the grep is now guarded. Reproduced under `bash -e` with `pipefail` before and after.
- **Vendored `com.cuvara.netcode` bumped to upstream `v0.16.0`, with no local renumbering.** The
  drift the `netcode-vendor-drift` check found is now reconciled: **13 differing paths down to 1**.
  Root cause of the original divergence was commit `31f7beb`, which vendored upstream **0.15.4** and
  **relabelled it 0.15.5** locally; upstream then released its own, different 0.15.5. The two never
  held the same content from the moment both existed, and 16 commits of client-side prediction work
  accumulated on top with no route back. **Never renumber a vendored copy** — the version field is
  the only handle the drift check has, and a local relabel makes it lie.
  The prediction work (`heldFrom` idle guard, `SeedBaseTick`, the estimator's two-observation rule)
  is now upstream in `Cuvara/Netcode` v0.16.0 rather than living only here, so a fresh install of
  the package no longer silently desyncs against the current server.
  EditMode suite before the bump: **388/388**. After: **391/391** — the three added tests are the
  public-surface contract tests that came with v0.16.0.
- **`Samples~/DOTSSample/DOTSNetworkBridge.cs` remains a declared client-only difference.** It wires
  `BackendCommandLine` into the sample, and that file is client-only harness, so the bridge cannot
  go upstream without it. The drift check reports it and will keep reporting it: `.vendor-client-only`
  exempts files that are *absent* upstream, never files that *differ*, because an exemption covering
  modifications would let real drift hide behind it.

### Added
- **`sgl-pin-check` CI: the Shared.GameLogic pin in `manifest.json` and `packages-lock.json` must
  agree.** UPM resolves the **lock**, so a manifest-only bump is silently ignored: the diff looks
  like the upgrade happened, the build stays green, and the client keeps running the old
  simulation. It also blinds the golden-vector tests, which replay fixtures read from the *pinned*
  package — bump the server's fixtures, forget the lock, and the one cross-language check that
  exists keeps passing against the stale ones. The job also verifies the pinned tag exists
  upstream and that its own `package.json` version matches the tag name (nothing on the server
  side re-verifies a tag after creation). Being *behind* the newest release is reported as
  informational, never a failure — sitting on an older release is a valid choice.
  Runs on PRs touching either file, weekly, and on `workflow_dispatch` so `rpg-mmo-server` can
  fire it when a new `sgl-v*` tag is published.
- **`netcode-vendor-drift` CI: fail when the vendored `com.cuvara.netcode` differs from its
  upstream release.** The package is vendored, not a submodule and not a subtree — it shares no
  git history with `Cuvara/Netcode`, so nothing about git can tell you the copies have diverged.
  The job reads the version from the vendored `package.json` and compares against **that tag**,
  not `main`: comparing to `main` flags every legitimate lag and stays silent on the failure that
  matters, which is two copies claiming the same version and holding different code.
  Measured on introduction: **11 files differ at `0.15.5`**, including `LocalMovePredictor.cs`,
  `TickRateEstimator.cs` and `WorldViewBinder.cs` — the prediction path that has to agree with the
  server — plus a `package.json` pinning a different `Shared.GameLogic` (`sgl-v0.1.8` upstream vs
  `sgl-v0.1.9` here). The job deliberately does **not** open a sync PR: the client copy is the one
  that is ahead, so an automated sync from upstream would delete real work. Client-only additions
  are declared in `Packages/com.cuvara.netcode/.vendor-client-only`; a declared file may be absent
  upstream but may not differ, so an exemption cannot hide a real drift.
- **`.gitignore`: ignore `/.verify/`.** The post-deploy verify suite
  (`rpg-mmo-server/backend/deploy/k8s/verify`) writes Unity test logs and NUnit XML there
  when it is pointed at this project. `*.log` already caught the logs, so only the XML
  surfaced — 5.5 MB across 16 files showing as untracked, which is noise in every
  `git status` and a standing invitation to commit run output by accident.
- **Run several built clients at once against a chosen backend.** A built player had
  no way to be told where the backend is: the DOTS sample scene carries only
  `DOTSSceneSetup`, which adds `DOTSNetworkBridge` at runtime, so the component could
  never hold anything but its own field initializers — gateway `127.0.0.1:8000`,
  Nakama `127.0.0.1:7350`, and a `SampleNakamaAuth` constructed with no arguments at
  all. Pointing a player anywhere else meant editing source and rebuilding, which is
  untenable now that the game server is an Agones pod whose port is assigned at
  scheduling time.
  - `BackendCommandLine` (DOTS sample, mirrored in the package's `Samples~` copy)
    resolves gateway host/port, Nakama scheme/host/port/server key, map id, the
    `/status` URL and the device id from the player's command line, falling back to the
    `CUVARA_*` environment variables the Editor live-backend tests already use, then to
    the previous defaults. Read once in `Start`, before anything connects; nothing runs
    per frame and no netcode behaviour changes.
  - Passing `-cuvara-map` also collapses the offered map set to that one map. With the
    scene's two maps the bridge draws a selector and waits for a click, which an
    unattended launcher cannot supply.
  - The device id is now per-process (`-cuvara-device`, else tag+pid+clock). Two
    instances sharing one Nakama identity is the failure that reads as success: the
    second login evicts the first and the survivor sits alone in a world of one.
  - `Tools/run-clients.sh` starts N players, each with its own log file, device
    identity and window, all pointed at a backend given as parameters. `--kill` stops
    them, which is required before a rebuild — a running player holds
    `lib_burst_generated.dll` open.
- `PlayerBuilder` accepts `-buildOutput <path>` in addition to `BUILD_OUTPUT_DIR`.
  Exporting the variable in a WSL shell does not put it in the environment of a Windows
  `Unity.exe`, so the build silently landed in the default `build/`; a command-line flag
  crosses that boundary.

### Fixed
- **Client and server base ticks free-run at arbitrary phase (#13).** The predictor's
  `_baseTick` started at 1 and free-ran via wall-clock accumulation, while the server's
  `current_tick` was in the hundreds of thousands. The absolute values did not matter —
  `StepDeltaTime` and `ApplyHeld` use differences — but the phase did: the hold window
  is `HoldTicks` base ticks, and where each clock's tick boundary fell relative to an
  input changed how many held steps got applied between inputs. On localhost with
  matched rates and no loss, 17 of 20 samples needed a correction of exactly 2 steps.
  Fixed by seeding `_baseTick` from the server's world tick (`WorldState.Tick`, already
  on the wire) on the first snapshot, via a new `SeedBaseTick(long)` method called from
  `WorldViewBinder` before `Reconcile`. The accumulator-driven clock in `Advance` owns
  the counter after seeding; re-seeding on every snapshot would fight it.
- `packages-lock.json` still resolved `shared-gamelogic` to `sgl-v0.1.8`. The v0.4.1
  bump changed only `manifest.json`, so the two disagreed about which version the
  project uses and the lock decides. Now pinned to `sgl-v0.1.9`
  (`514d454192355943a24b822c1441ab25b5e770e1`, the tag's actual commit).

### Removed
- `Assets/AddressableAssetsData/link.xml` is no longer tracked, and is now ignored.
  Addressables regenerates it on build and deletes it in between, so every unrelated
  commit had the chance to carry its churn — which is how it was committed in the
  first place.

## [v0.4.1] — 2026-08-15

### Changed
- Bump `shared-gamelogic` to `sgl-v0.1.9` — single-rate deadzone fix from server v1.4.1

## [v0.4.0] — 2026-08-15

### Changed
- Netcode updated v0.11.0 → v0.15.5 (tick rate from wire, held movement predictor,
  elapsed-time step, per-frame prediction fix)
- DOTS updated to v0.21.0 (per-system parallel thresholds)
- `shared-gamelogic` updated to `sgl-v0.1.8`

### Fixed

- **Local player stutter.** The netcode package advanced prediction twice per frame, so
  the predictor's clock ran at ~2x real time and the server's hold window expired in
  half the real time it should — the controlled avatar moved for part of each send
  period and stood still for the rest, at every frame rate, while remote entities stayed
  smooth. Fixed in `com.cuvara.netcode` 0.15.3; see that package's CHANGELOG for the
  measurements.
### Changed

- **Input send cadence in the DOTS sample** now runs off an `Update` accumulator instead
  of a timer loop (`UniTask.Delay`), so the delivered rate is the configured
  `inputRateHz` by construction rather than by a timer's accuracy. The send rate is a
  contract with the server, not a preference: it must be at least the server's hold
  window or the avatar stalls between sends however well prediction behaves. Verified
  afterwards at exactly **15 sends per `real=1.000s`** against an independent
  `Stopwatch`.

  **This was not the stutter, and an earlier note here claiming the timer delivered
  ~7.5 Hz was wrong.** That figure came from `ObservedInputInterval` reading 0.138 s —
  measured in the predictor's own clock, which was the thing running at 2x. In real time
  that is ~0.069 s, i.e. the timer was delivering close to the configured 15 Hz all
  along. The only independently-clocked measurement of the send rate was taken *after*
  this change, so it cannot attribute anything to it. The change stands on determinism,
  not on a repair it did not perform.

### Added

- `FrameRateCap` — optional `-targetFps N` launch override for the render frame rate.
  Uncapped by default: a 60 fps cap was tried against this stutter and measurably did
  not help, which is what ruled the frame rate out as the cause. The mechanism stays for
  pinning the rate during a measurement, and for battery and thermals.


## [Unreleased]

### Fixed

- **`NakamaAuthProvider` returned the wrong token, and failed silently**
  (`Assets/Scripts/Nakama/Auth/NakamaAuthProvider.cs`). `GetJwtAsync` returned
  `NakamaSessionService.Session.AuthToken` — the Nakama *session* token — where the
  gateway expects a *gateway* token minted by the `gateway_token` RPC. The two are
  not interchangeable, and substituting one is not a clean failure: verified against
  a live stack, the gateway **accepts** the session token (the deploy can share a
  single HS256 secret) but the user claim it reads is absent, so the session is
  established with an **empty `user_id`** and the player is nobody. Any feature
  keyed on identity — ownership, persistence, duplicate-login eviction — would have
  silently misbehaved.
  `GetJwtAsync` now exchanges the session for a gateway token via
  `Client.RpcAsync(session, "gateway_token", "{}")` and throws with a message naming
  the RPC if it fails or yields no token, rather than returning a credential that
  half-works. No signing secret is held client-side on this path.
  The payload is parsed **once**: the Unity SDK's `IApiRpc.Payload` already yields
  the inner JSON, unlike Nakama's raw HTTP API where the RPC result is a
  JSON-encoded string nested in an envelope and must be unwrapped twice. Noted in a
  comment so the next reader does not double-parse.
- **`GameLifetimeScope` registered the services but not the component, so the auth
  provider was never actually reached** (`Assets/Scripts/DI/GameLifetimeScope.cs`).
  VContainer only injects components it has been told about, so a `LifetimeScope` in the
  scene was not sufficient: `NetworkBootstrap`'s `[Inject]` never ran, it reported "no
  container found", built its own `NetworkClient`, and fell back to minting a
  development JWT — silently bypassing the `NakamaAuthProvider` registered immediately
  above it. Added `RegisterComponentInHierarchy<NetworkBootstrap>()`. Without this the
  `IAuthProvider` wiring was inert in any real scene.

- **`NakamaSessionService` documentation asserted the two tokens were the same**
  (`Assets/Scripts/Nakama/NakamaSessionService.cs`). That claim is what licensed the
  bug above. Rewritten to state what each credential is for and to spell out the
  empty-`user_id` failure mode.

### Added

- **`IAuthProvider` interface** (`Cuvara.Netcode.Auth`) — contract for JWT
  provisioning, defined in the netcode package. `NetworkClient` accepts an
  optional `IAuthProvider` via DI and exposes a new
  `ConnectAsync(mapId, ct)` overload that resolves the JWT internally.
  `DevAuthProvider` wraps `DevJwt` for local development.

- **Nakama Unity SDK integration** (`com.heroiclabs.nakama-unity` v3.9.0) —
  new `Scripts.Nakama` module (`Assets/Scripts/Nakama/`, assembly
  `NDC.Scripts.Nakama`) with VContainer DI registration.
  - `NakamaSessionService` — wrapper around the Nakama SDK `IClient`,
    registered as a singleton. Provides device auth (primary, auto-creates
    account), email auth (secondary), session token persistence in PlayerPrefs,
    and transparent token refresh via the SDK's refresh token flow.
  - `NakamaAuthProvider` — implements `IAuthProvider`, restores persisted
    session or authenticates via device ID, then returns the JWT. Registered
    as the `IAuthProvider` singleton so `NetworkClient.ConnectAsync(mapId, ct)`
    works out of the box when Nakama is wired up.
  - `NakamaSettings` — connection configuration (scheme, host, port, server
    key), defaulting to the local Nakama dev server (`http://127.0.0.1:7350`,
    `defaultkey`).
  - `NakamaRegistration.RegisterNakama()` — VContainer extension method, called
    from `GameLifetimeScope` alongside `RegisterNetworking()`.

### Changed

- **Netcode module extracted to standalone UPM package** `com.cuvara.netcode` —
  `Assets/Scripts/Net/` → `Packages/com.cuvara.netcode/Runtime/`, with its own
  `package.json`, `CHANGELOG.md`, `README.md`, tests, and documentation.
  Namespace renamed from `Scripts.Net` → `Cuvara.Netcode`. Assembly renamed from
  `NDC.Scripts.Net` → `Cuvara.Netcode.Runtime`. Demo scene and config moved to
  `Samples~/DemoBootstrap/` (import via Package Manager). Tests moved to
  `Cuvara.Netcode.Tests.Editor`. The package is embedded and auto-resolved by
  Unity; no `manifest.json` entry needed.

### Added

- **`Shared.GameLogic` is now a project dependency** —
  `com.rpgmmo.shared-gamelogic` at `sgl-v0.1.0`, resolved from the backend repo
  as a UPM git dependency with a `?path=` subfolder reference. This is the
  deterministic simulation the server runs; the client compiles the same
  **source**, which is what lets prediction and the authoritative simulation
  agree (backend ADR-10).

  Pinned to a **tag, never a branch**. A branch reference would change what the
  client predicts whenever someone pushes to the server repo, with nothing in
  this repo to attribute the change to.

  Now at **`sgl-v0.1.4`**, and verified in the Editor: `Shared.GameLogic.dll`
  appears in `Library/ScriptAssemblies`, and `Packages/packages-lock.json` is
  updated.

  `sgl-v0.1.0` resolved but produced **no assembly**. Unity treats a git package
  as immutable and will not generate `.meta` files inside one, so an asmdef
  shipped without its own `.meta` is never registered and the package's sources
  are silently ignored — no error and no assembly. `sgl-v0.1.1` ships the 19
  `.meta` files. Check for the DLL when bumping this package, not for a green
  compile: `NDC.Scripts.Net` compiled green throughout the period the package was
  producing nothing, because it did not reference it yet.

  The package's asmdef sets `noEngineReferences`, so the shared assembly cannot
  reference `UnityEngine` at all. Netcode references it, never the reverse.

- **`fma_multiply_add_discriminator` passes under Unity, and the multiply-add fix
  is confirmed load-bearing.** Running the pre-`sgl-v0.1.2` expression shape
  (`_posX + _dirX * step`) directly under the Editor's Mono JIT yields
  `0x401B473F` where the split-multiply form yields the correct `0x401B4740`, so
  without the fix Unity would compute a different position from the server on
  these inputs.

  The mechanism, however, is **not FMA contraction** — it is the same
  double-precision widening behind the original `SqrMagnitude` divergence. Both
  hypotheses predict identical bits on this vector, so it cannot separate them;
  `sqrt_negative_components` can, and there FMA predicts `0x4203EB84` while
  double intermediates predict `0x4203EB85`, which is what Unity produced. FMA
  contraction is still unobserved in Mono. The fix denies both, so nothing needs
  changing — but the vector's name oversells what it detects.

  Also worth knowing: adding a fixture case does **not** show up until the domain
  reloads. The first run after the bump reported 111 passing with the new case
  never collected, because NUnit builds `TestCaseSource` at collection time and
  the Test Runner reused the cached list. A green run whose test *count* did not
  move is not evidence.

- **The full core flow now runs end to end against the local stack**, observed in
  the Editor: gateway auth → `enter_world` → direct dial of the assigned game
  server → join → input up → keyframes and deltas down, with `ack_tick` tracking
  the sent input tick exactly (`sent 154 ack 154`) and rtt 8 ms. Documented with
  the verbatim console output in `docs/NETCODE.md`.

  Two client bugs were found by running it, both fixed here:

  - **`NetworkEndpoint.Parse` rejected a host-less `server_addr`.** The stack
    advertises `GAMESERVER_PUBLIC_ADDR=":9200"` and the gateway returns it
    verbatim, which stopped the bootstrap one step after `enter_world` with
    `server address ':9200' is not host:port`. `backend/deploy/docker-compose.yml`
    documents that the **client** normalises a bare `":9200"` to
    `127.0.0.1:9200`, and Go's `net.Dial` does it natively — which is why no
    Go-side test covered it. Now normalised to loopback, with 16 new tests.
    Loopback rather than the gateway's host on purpose: a host-less address
    reaching a real device is a server misconfiguration, and connecting to
    something merely plausible would hide it.

  - **Input and heartbeat stalled after one frame while the app was unfocused.**
    `Application.runInBackground` is off project-wide, so the player loop stops
    ticking when unfocused — `frameCount` stayed at 1 across six seconds while
    `Time.realtimeSinceStartup` advanced. Snapshots kept arriving on the socket
    threads, so the session looked healthy while nothing was being sent and the
    server would have dropped it at the 30 s pong timeout. The bootstrap now sets
    `Application.runInBackground = true`; whether the shipping player should is
    left to whoever owns the player settings.

  The snapshot log now prints `sent N ack M` side by side, because a line showing
  only the server's ack cannot distinguish "our input is not landing" from "we are
  not sending" — which is exactly what made the second bug look like the first.

- **`NDC.Scripts.Net` now references `Shared.GameLogic`, and merges snapshots with
  it.** `World/WorldState` rebuilds authoritative world state by delegating to
  `Shared.GameLogic.Systems.SnapshotMerger` — the same type the server was diffed
  against — rather than reimplementing "keyframe replaces, delta upserts and
  removes" client-side. `WorldState` is only the adapter between the wire-facing
  `ResolvedSnapshot` and the simulation type `SnapshotData`; interning is resolved
  upstream, because the shared merger keys by real entity id and knows nothing
  about handles.

  `NetworkClient.World` is merged before `SnapshotReceived` fires, and
  `NetworkClient.StateChanged` was added so a caller can narrate the two-hop
  handshake without reaching into either hop.

- **EditMode golden-vector conformance tests** (`Assets/Tests/EditMode/`,
  assembly `NDC.Tests.EditMode`). Replays the `GoldenVectors/*.json` fixtures that
  ship inside the package through `Shared.GameLogic` and compares every float
  **bit-for-bit**; the server's xUnit suite replays the same files. Read with the
  built-in `JsonUtility`, so the gate needs no extra package.

  **95 of 95 tests pass at `sgl-v0.1.2`.** (The Test Runner reports
  `TotalTests: 96`; the extra entry is a container node, not a test.)

  On its first real run, against `sgl-v0.1.1`, the gate found a genuine
  divergence and failed three vectors — `sqrt_irrational_small.sqrMagnitude`,
  `sqrt_negative_components.sqrMagnitude` and `clamped_asymmetric.x`. All three
  traced to one expression, `x * x + y * y`, in `Vec2.SqrMagnitude` and in
  `MovementSystem.ResolveDirection`'s `magSq`: C# permits a float expression to be
  evaluated at higher precision (ECMA-334 §11.3.7), .NET 10's RyuJIT evaluates
  strictly in float32, Unity's Editor Mono JIT keeps double-precision
  intermediates, and the two answers were one ULP apart. Fixed in
  `Shared.GameLogic` at `sgl-v0.1.2` with explicit per-operation `(float)` casts,
  plus the same treatment for FMA contraction in `MovementSystem.Integrate`.
  Server results were unchanged, so Unity moved onto the server's numbers — the
  right direction, the server being authoritative.

  The tests were left red until the library was fixed rather than reconciled with
  a tolerance, which would have passed and deleted the finding.

- **`Assets/Scenes/NetcodeBootstrap.unity`** — press Play and the whole core flow
  runs against a local backend, logging each step: mint a dev JWT, gateway auth,
  `enter_world`, dial the assigned game server, join, then input up and snapshots
  down. Configured by `Assets/Settings/NetworkBootstrapConfig.asset` (gateway host
  and port, user id, HS256 secret, map id, input rate), defaulting to the
  backend's own defaults — `127.0.0.1:8000`, `dev-secret-change-me`, `map_01`,
  15 Hz. The game server address is deliberately not configurable: the gateway
  hands it back from `enter_world`, and hardcoding it would bypass the assignment
  step (ADR-3).

  `Bootstrap/DevJwt` mints the token client-side, which is a **development
  shortcut, not the architecture** — Nakama issues it in the shipped design and
  the client never holds a signing secret.

  Run against a gateway already listening on `127.0.0.1:8000`, the connect,
  framing, JSON encoding and auth round-trip all worked; it stopped at
  `invalid token`, that gateway running with a different `JWT_SECRET` than
  `backend/deploy/.env` documents. The minted token was verified correct
  out-of-band. `enter_world` onward is therefore still unobserved.

- **Client networking layer** — new `NDC.Scripts.Net` assembly
  (`Assets/Scripts/Net/`), the client's first gameplay-adjacent code. Covers the
  two-hop connection (gateway for auth and map assignment, then the game server
  directly — the gateway is never in the gameplay data path, ADR-3), the
  `[4-byte big-endian length][body]` framing, per-frame encoding detection, the
  10 s / 30 s heartbeat shared by both hops, and entity-handle resolution for
  delta snapshots. Documented in `docs/NETCODE.md`.
  - Outbound encoding is latched per connection because both servers latch their
    reply encoding from the first frame they receive; inbound frames are sniffed
    per frame, because gateway eviction frames arrive as JSON whatever the
    connection latched.
  - A `kick` and the `disconnect` that follows it are reported as **one**
    eviction, not two, and `Closed` is raised exactly once per connection.
  - A join token is single-use with a 30 s TTL and is pinned to one server, so a
    join retry re-runs `enter_world` for a fresh one instead of replaying it.
  - An unresolvable entity handle rejects the whole snapshot and requests a
    keyframe. Nothing is guessed: wrong state attributed to the wrong entity is
    far harder to detect than absent state.
  - No game rules are implemented, by design. Movement, combat and validation are
    server-authoritative and belong to `Shared.GameLogic` (ADR-10); the merge of
    snapshots into world state is left to it as well.
- **`Tools/WireConformance`** — a `dotnet run` harness that compiles the
  engine-independent half of the assembly (protocol, JSON, codec, snapshot
  resolution, framing) outside Unity and asserts the wire format against bytes
  taken from the server sources. It includes those files rather than copying
  them, so it cannot drift from what ships.

### Verification

The Unity Editor has **not** compiled this code. `Tools/WireConformance` passes,
and the whole assembly compiles clean with `dotnet` against the Unity 6000.3.9f1
engine DLLs plus UniTask and VContainer sources at C# 9 / netstandard2.1 —
neither of which validates asmdef resolution, IL2CPP or platform defines.

### Documented

- The normative `Shared.GameLogic` UPM line (pinned tag, `git?path=#ref` form) is
  recorded in `docs/NETCODE.md` together with its pre-flight results. It is
  **deliberately not in `Packages/manifest.json`**: the tag does not exist yet, and
  an unresolvable git dependency fails the whole package resolve rather than just
  that entry.

### Known limitations

- Protobuf is not implemented: the codec is behind an interface and only the
  legacy JSON path exists. Both servers still accept JSON. `docs/NETCODE.md` lists
  what adding Protobuf requires.
- KCP is not implemented. A server that advertises `kcp` fails the dial with an
  explicit error rather than silently falling back to TCP, which it is not
  listening on.
- WebGL is unsupported by the TCP transport (`System.Net.Sockets` is unavailable
  there).
- Map transfer, reconnect/resume, prediction and reconciliation are out of scope
  for this change.
