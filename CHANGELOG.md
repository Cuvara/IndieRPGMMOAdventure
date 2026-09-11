# Changelog

All notable changes to this project are documented here.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Changed (2026-09-11)

- `com.cuvara.netcode` v0.36.1 → **v0.36.2** (manifest and lock): a client refused for not
  sealing now retries WITH sealing, so a default player build plays on the sealed dev fleet
  **without any flag**. It escalates and never downgrades — the runtime has exactly one
  assignment to `RequireSealedSession` and it is `= true`, with a test that scans for the
  opposite.

  Measured on k3d-rpg-dev with `GAMESERVER_SEALED=require`, three players, no flags:
  **7 passed, 0 failed**. The client log shows the whole path:

  ```
  will NOT request sealing up front …
  the game server requires a sealed session and refused this one (no_sealed_session);
    reconnecting WITH sealing
  sealed session established; the server's binding was NOT verified …
  ```

- **`TransportSecurityReport` no longer claims the gameplay hop is in the clear.** Its
  startup line said "this matches every deployed environment today (GAMESERVER_SEALED=off)",
  which stopped being true the same day dev and staging were flipped to `require` — a
  sentence that was accurate when written and wrong one deploy later. It now describes what
  the client will *request*, names what a `require` server does about it, and points the
  reader at the `sealed session established` line that settles it.

### Added (2026-09-11)

- **The shipped client can run against a game server that requires a sealed session, and
  speaks Protobuf on the wire by default.** Two flags, in the existing `-cuvara-*` /
  `CUVARA_*` style:

  | Flag | Environment | Values | Default |
  |---|---|---|---|
  | `-cuvara-sealed` | `CUVARA_SEALED` | the usual boolean spellings | **off** |
  | `-cuvara-encoding` | `CUVARA_ENCODING` | `json` \| `proto` | **`proto`** |

  `-cuvara-sealed` sets `NetworkSettings.RequireSealedSession` (ADR-22: ChaCha20-Poly1305
  over authenticated X25519, HKDF-SHA256, sequence doubling as the replay counter).
  `-cuvara-encoding` selects the codec `RegisterNetworking` registers; it has to be an
  argument to that call, because registering a second `IWireCodec` afterwards does not
  override the first — it makes VContainer fail the whole container build with "Conflict
  implementation type".

  **Why this was needed.** `GAMESERVER_SEALED` defaults to `require` on the C# game
  server and every deployed environment pins it to `off`. Turning it on for the live
  `k3d-rpg-dev` fleet on 2026-09-11 passed the Go smoketest with `-sealed -encoding proto`
  and correctly refused the JSON smoketest — and killed the real Unity client outright:
  `[DOTSNet] FATAL: Cuvara.Netcode.Client.NetworkException: gateway closed the connection
  during the handshake`, then twelve refused reconnects, 4 of 8 acceptance rows failing.
  `GameLifetimeScope` called `RegisterNetworking` with no `encoding` argument, so it took
  the package default of `WireEncoding.Json`, and `RequireSealedSession` was never set at
  all. **A JSON client can never seal** — the sealed handshake messages are absent from
  the JSON message set on purpose, so key material cannot be rendered into a
  human-readable payload — so a `require` server refuses it at the join with
  `encoding_cannot_seal`, which arrives at the client as a bare closed connection.

  **Why these defaults.** Sealing is **off** unless asked for: there is no negotiation and
  no fallback, so a client that seals against a server with sealing off waits for a hello
  that never comes and the join times out. Defaulting it on would break every dev run to
  make one environment work. Encoding defaults to **`proto`**, which does change what a
  plain run does, because (a) leaving it at JSON would make `-cuvara-sealed` a flag that
  cannot work unless a second flag is remembered alongside it, (b) Protobuf is what the
  backend defaults to and what the package's golden vectors cover, ~81% smaller on the
  wire once id interning is counted, and (c) it costs no server change — both servers
  sniff the first body byte (`0x08` Protobuf, `0x7B` JSON) and answer in kind. That last
  claim is what the unsealed acceptance run below exists to check rather than assert.
  `-cuvara-encoding json` puts the old behaviour back for a readable capture.

  The one combination that cannot work — sealing over JSON — is reported as an **error**
  at startup naming both flags, rather than silently corrected: forcing Protobuf there
  would be guessing which of the two the operator meant.

- **`TransportSecurityReport` now reports the gameplay hop too — as a request, not an
  outcome.** It previously said nothing about it, deliberately, because whether the
  session is sealed is decided at the join. That is still true and the new lines still say
  so; what changed is that the client now *asks* for something, and the request is a real
  knowable fact at startup. It logs `will REQUEST a sealed session (ADR-22) over
  protobuf` / `will NOT request sealing — gameplay frames cross this hop in the clear`,
  names the address as *assigned at join* rather than pretending to know it, and leaves
  the line that says the session **is** sealed to the netcode client, which carries the
  caveat this one cannot: the server's binding is not verified, so the session is
  confidential against a passive eavesdropper and offers nothing against an active one
  until ADR-22's pinned identity key lands.

- **`Tools/verify-multiclient.sh` takes a `--` passthrough**, the same one
  `run-clients.sh` already had, so the harness can exercise the sealed hop:
  `… -- -cuvara-sealed 1`. Without it there was no way to reach the new flags through the
  verification script at all.

  **Acceptance, run here against the live `k3d-rpg-dev` fleet — not unit tests.** A fresh
  Windows player (Mono2x, `-executeMethod PlayerBuilder.Build`, no `-bootScene`, so the
  real MainScene path) reported `[PlayerBuilder] Build succeeded: 131072079 bytes`, which
  is the line `PlayerBuilder` only prints when `BuildReport.summary.result` is
  `Succeeded`. `Tools/verify-multiclient.sh --count 3` was then run three ways:

  | Run | Fleet | Client | Result |
  |---|---|---|---|
  | (a) regression | `GAMESERVER_SEALED=off` | protobuf, unsealed | **7 passed, 0 failed, 1 not checked** |
  | (b) sealed | `GAMESERVER_SEALED=require` | protobuf, `-cuvara-sealed 1` | **7 passed, 0 failed, 1 not checked** |
  | control | `GAMESERVER_SEALED=require` | protobuf, **no** `-cuvara-sealed` | join/kick loop |

  The seven asserted rows in (a) and (b) are identical: all 3 clients reached IN WORLD, 3
  distinct Nakama user ids, all assigned the same game server, no FATAL in any log,
  `players_online=3`, 3 Redis session keys, `servers:map:map_01` holding exactly one
  member. The eighth row — mutual visibility — is the one only a human can settle and is
  reported NOT CHECKED, never folded into the pass.

  Run (a) is what makes the encoding default defensible: it is the same harness that
  passed before this change, now passing with the client speaking protobuf against an
  unchanged `off` server.

  Run (b)'s client log carries the two lines that say what actually happened, rather than
  leaving it to the exit code:

  ```
  [transport-security] game server (address assigned at join): will REQUEST a sealed session (ADR-22) over protobuf. …
  [Net] sealed session established; the server's binding was NOT verified, so this session is
        confidential against a passive eavesdropper and offers no man-in-the-middle protection
  ```

  The control exists because (b) passing does not by itself prove the flag did anything.
  It shows the flag is load-bearing, and it also **corrects the failure shape recorded
  above**: a protobuf client that does not seal is not refused *at* the join by a
  `require` server — it reaches `InWorld` and is then closed (`PeerClosed`) and
  reconnects, 21 such lines in 45 seconds, where the sealed run logged **zero**. Only the
  *JSON* client is refused outright. So the original `gateway closed the connection during
  the handshake` was the encoding, and `-cuvara-sealed` is what buys a session that
  survives.

  Fleet handling: `GAMESERVER_SEALED` is env index 5 and was patched by replacing the
  **whole** env entry object, never `/value` — index 6 is `GAMESERVER_ADVERTISE_HOST`, a
  `configMapKeyRef`, and a `/value` patch on an entry with `valueFrom` corrupts it. The
  fleet was returned to `off` and recycled afterwards; index 6 was verified intact both
  times.

### Added (2026-09-11)

- **Gateway TLS is wired to the command line, and every hop says what protects it at
  startup.** `-cuvara-gateway-tls` / `CUVARA_GATEWAY_TLS` turns on ADR-23's TLS for the
  gateway connection, and `-cuvara-gateway-tls-cert` / `CUVARA_GATEWAY_TLS_CERT` pins a PEM
  certificate for a gateway holding a self-signed one. Off by default, matching the
  gateway's own default. `-cuvara-nakama-scheme https` already existed and is unchanged.

  `TransportSecurityReport` logs one line per hop when the container is built. The client
  talks to three things — Nakama, the gateway, the game server — and each is protected by a
  different mechanism configured independently, so "the connection is encrypted" is a
  sentence that is true of one hop and believed about all three. A plaintext hop to
  loopback logs at Info and says nothing more; a plaintext hop to a **remote** host logs an
  **error** naming what crosses in the clear and the flag that fixes it, because that is a
  build shipping credentials readable by anyone on the path. A host it does not recognise
  counts as remote, so the failure direction is one warning too many rather than a silent
  plaintext link to a real server.

  It lives in `NDC.Scripts.DI` rather than beside `BackendCommandLine`: `NDC.Scripts.Session`
  references no assemblies at all, so a `using Cuvara.Netcode.Transport` there compiles in a
  hand-written csproj and fails in Unity.

### Changed (2026-09-11)

- `com.cuvara.netcode` v0.35.0 → **v0.36.1** (manifest and lock): TLS on the gateway hop
  with certificate validation that cannot be turned off, plus the fix for a pending socket
  read that ignored cancellation — which is why `NetworkSettings.ConnectTimeout` now
  actually bounds the gateway handshake instead of hanging indefinitely when the gateway
  never answers.

  Acceptance, run here rather than taken from package CI: the full EditMode suite is
  **1223/1223** against v0.36.1, and the sample's five cases were run in **play mode**
  against a real `SslStream` listener — pinned connects over Tls12 and carries a frame, an
  unpinned connection to a self-signed certificate is refused by the platform, a TLS client
  does not downgrade to a plaintext gateway, a plaintext client against a TLS gateway
  stalls rather than being refused, and the factory throws when asked for TLS with no
  options. The first is a positive control, so "everything is refused now" cannot pass for
  success.

  The `Gateway TLS Probe` sample is imported at `Assets/Samples/Cuvara Netcode/0.36.1/`.

### Changed (2026-09-08)

- `com.cuvara.netcode` v0.34.0 → **v0.35.0** (manifest and lock): the client's input send
  cadence is no longer anchored to `GameConstants.DefaultTickRate`. That constant is the
  server's SNAPSHOT rate, not its simulation rate, so the client was sending at exactly the
  snapshot cadence — the one rate at which the acknowledgement floor cannot be measured,
  because the wait term never sweeps. The cadence is now derived from the snapshot rate
  (13 Hz against 15) and the send schedule is pinned, without which the choice is unreachable
  on a frame grid. Cost: ~13% fewer uplink packets, and a direction change waits up to 76.9 ms
  instead of 66.7 ms to reach the server.
- The release also lands the floor-statistic correction, the sweep guard's sample floor, two
  reconcile counter fixes, the ack-floor rate conversion, and assembly definitions for five
  package samples. Full detail in the package CHANGELOG under 0.35.0.

- `com.cuvara.netcode` v0.33.0 → **v0.34.0** (manifest and lock): nine guards against an
  untrustworthy timebase. The staleness estimator fits a rate through two best-case anchors, a
  construction valid only if the minimum achievable delay is the same at both ends; nothing
  checked that before the result reached `SetClockRateScale`, so a displaced fit ran the client's
  base-tick clock 8.3% slow. A fitted rate must now reproduce over a doubled baseline before it
  steers the clock, the age is refused when its slope is, and a provisional age that saturates its
  clamp contributes zero instead of delivering the warm-up fallback.
- `Assets/Samples/Cuvara Netcode/0.28.1/Clock Sync Probe` removed and re-imported at 0.34.0. The
  committed 0.28.1 copy had no assembly definition, so importing the sample a second time was a
  hard compile error (CS0101) that only appears **after** a version bump - it lands on the first
  person to update, never on the person who imported. The 0.34.0 copy ships `ClockSyncProbe.asmdef`.
- `SampleImporter` takes `-samplePackage` instead of `-importPackage`. The latter is a built-in
  Unity batch-mode flag expecting a `.unitypackage` path: Unity acted on it too, failed to
  decompress the package *name* as an archive, and exited 1 **after** the sample had imported
  successfully - an exit code reporting failure for a run that worked.

- `com.cuvara.netcode` v0.32.0 → **v0.33.0** (manifest and lock): client prediction now steers on the
  measured snapshot age instead of a whole snapshot interval during the estimator's ~8 s warm-up, and
  runs its base-tick clock on the server's timebase using the already-fitted skew. Live against the dev
  stack this took the target lead from 4 base ticks to 0 and the standing clock error from 3 ticks to −1,
  halving the correction the local avatar took on every input. Sample folder re-imported at the new
  version.
- `.gitignore`: the Addressables platform folder and its `.meta` (an untracked entry in every status).

### Changed (2026-09-07)

- `com.cuvara.dots` v0.28.0 → **v0.29.0** and `com.cuvara.netcode` v0.31.1 → **v0.32.0** (manifest and
  lock together). Netcode v0.32.0 makes `RegisterNetworking()` take its dependencies, fixing a v0.31.1
  regression that made the container unbuildable for any consumer wanting its own `ITransportFactory`.
  DOTS v0.29.0 adds the Phase B Showcase sample.
- Both packages' sample scenes are imported into `Assets/Samples/` and committed, so the folder version
  records which release was exercised: `Cuvara DOTS/0.29.0/Phase B Showcase` (four offline scenes, each
  self-testing under `-showcaseAutorun`) and `Cuvara Netcode/0.32.0/Reconnect Policy Demo`.

### Added (2026-09-07)

- `Assets/BuildScripts/Editor/SampleImporter.cs` — headless UPM sample import
  (`-executeMethod SampleImporter.Import -importPackage <id> -importSample <name> [-addToBuild 1]`),
  so a package feature's sample scene can be built and run from batch mode. Documented in CLAUDE.md.

### Changed (2026-09-07)

- `com.cuvara.netcode` v0.31.0 → v0.31.1 (manifest + lock): `RegisterNetworking()` now resolves
  `NetworkClient` from a scope (VContainer ignored `DefaultTransportFactory`'s default `string`
  parameter). Required for `MainSessionDriver` / `DotsWorldBridge` injection in MainScene.
### Added (2026-09-07) — MainScene session driver

- **`MainSessionDriver`** (`Assets/Scripts/DI/`, VContainer entry point registered by
  `MainSceneScope`): on scene start authenticates the device with Nakama
  (`NakamaSessionService.AuthenticateDeviceAsync`), connects through the gateway
  (`NetworkClient.ConnectAsync(map)` via the registered `NakamaAuthProvider`), and logs the
  markers `Tools/verify-multiclient.sh` asserts on — `[DOTSNet] Auth OK, user_id=<id>` and
  `[DOTSNet] IN WORLD as <id>` — byte-identical to the netcode DOTS sample. Disposing the scope
  cancels the sequence and disconnects. MainScene therefore authenticates and joins on its own;
  `-bootScene` is no longer required for a real-path multi-client run.
- **`Scripts.Session`** assembly (`Assets/Scripts/Session/`): `BackendCommandLine` (the sample's
  flag/`CUVARA_*` resolution, now with an injectable overload) and `MainSessionFlow` (the pure
  sequence with an endpoint seam). `GameLifetimeScope` resolves the backend once and registers
  `NetworkSettings`/`NakamaSettings` from it plus a `BackendSettings` instance.
- **Per-process identity**: `NakamaSettings.DeviceId` (from `-cuvara-device`, else a per-process
  id when `-cuvara-instance` is given, else null = machine id). `NakamaSessionService` uses it as
  the default device id and `NakamaAuthProvider` skips the PlayerPrefs session restore when it is
  set — three clients on one machine share PlayerPrefs and `SystemInfo.deviceUniqueIdentifier`,
  which made their logins evict each other.
- `NakamaSessionService` constructs its `Client` with `UnityWebRequestAdapter.Instance` (the Unity
  package's documented adapter) instead of the .NET SDK's default `HttpClient` adapter, which in a
  Mono player surfaces fast connection failures as `TaskCanceledException`.
- `MainSessionFlow` reports "Cancelled" only when the session's own token is cancelled; any other
  `OperationCanceledException` (a superseded login generation, a foreign timeout token) is
  `FATAL` with its message. `MainSessionDriver` logs a probe line at start (instance, disposed,
  token cancelled, client state) and at dispose (phase + stack trace), kept for player-log
  diagnosis.
- Tests: `MainSessionFlowTests` (10, fake endpoint: phases, marker lines, auth/connect failure,
  cancel vs. foreign cancellation), `BackendCommandLineTests` (6: precedence, the exact harness
  flag set, bad port, device-id resolution).
- `com.cuvara.netcode` v0.31.1 (RegisterNetworking resolves `NetworkClient`) is required for the
  container to build; the tag did not exist at commit time, so the manifest stays at v0.31.0.

### Changed (2026-09-07)

- `com.cuvara.dots` v0.27.1 → v0.28.0 (manifest + lock): the DOTS improvement plan phases A–E
  (pool ownership, chunk epochs, module install/uninstall, config validation, lifecycle events,
  physics collector, minimap/overlay, camera policies, ingestion generations). Required by the
  production view provider and `DotsWorldBridge` on this branch.
### Added (2026-09-07) — DOTS host provider adoption (plan D12)

Requires `com.cuvara.dots` at the release cut from `integration/dots-phase-b` (>= 63bfa52:
`PooledViewAssetProvider` ownership contract + `IsRegistered`, `DotsModules`,
`ViewConfigCatalog.TryBuild`/`ViewConfigValidator`, `DotsEntityView.BeginGeneration`,
`CameraFollowBootstrap.ResetSmoothing`, `MinimapBootstrap`) and `com.cuvara.netcode` >= 0.31.0
(`NetworkClient.Reconnected`). The manifest/lock bump to that tag is a separate change; until it
lands this code does not compile against the pinned v0.27.1 and the `CUVARA_DOTS` assemblies
will report the missing members.

- **Production view provider**: `LeasedViewAssetProvider` (`Assets/Scripts/DI/Dots/`) — the
  package's `PooledViewAssetProvider` for pooling, an `IViewPrefabLoader` for prefabs, and a
  lease per key between them: loaded once on first prewarm (concurrent callers share the load),
  the Addressables handle held while any instance exists, `Release(key)` dropping pooled
  instances now and the handle only after the last acquired instance returns, `Dispose` destroying
  instances before releasing handles. `Acquire` never loads synchronously (returns `null`,
  `UnloadedAcquires`). `AddressableViewPrefabLoader` resolves view keys through the library
  asset's `AssetReferenceGameObject`s — one handle per key, released once.
- **`DotsViewLibraryAsset`** (`Assets > Create > Cuvara > DOTS View Library`, expected at
  `Assets/Resources/DotsViews/DotsViewLibrary.asset`): one entry per archetype with view key,
  Addressable prefab, pool size, scale and offsets; `BuildLibrary` generates the package's
  `ViewArchetypeLibrary`/`ViewConfig`s at session start. `DotsViewLibraryValidation` runs the
  package validator plus this game's rules (every `DotsViewArchetypes.All` archetype present,
  every server kind mapped, every entry referencing a prefab).
- **Build gate**: `Assets/BuildScripts/Editor/DotsViewLibraryBuildCheck` — `IPreprocessBuildWithReport`
  and an explicit call at the top of `PlayerBuilder.Build` — fails the build with the offending
  entries named when the library has a missing/mismatched key or a reference that does not
  resolve to a prefab. No asset at all is a warning (primitive fallback), so sample/benchmark
  players still build.
- `RegisterDots(viewRoot, world, mode, library, loader, maxActivePerKey)`:
  `DotsViewProviderMode.Production` (default) leases Addressables prefabs from the library into
  the pooled provider; `Primitive` keeps the capsule/sphere placeholder for the sample and
  benchmark scenes. A missing library asset in Production logs an error and falls back to
  primitive rather than failing every scene's container. `DotsViewLibraryReference` is
  registered so the bridge can read the chosen mode/asset.
- **Authoring tool** `Assets/BuildScripts/Editor/DotsViewLibraryAuthoring.cs`: menu
  `Cuvara > DOTS > Create Placeholder View Library`, or headless
  `-executeMethod DotsViewLibraryAuthoring.CreatePlaceholderLibrary`. Creates
  `Assets/DotsViews/Prefabs/{PlayerLocal,PlayerRemote,Mob}.prefab` (blue/green capsules, red
  sphere, own materials, no colliders), marks them Addressable in the default group as
  `dots/view/<archetype>`, writes `Assets/Resources/DotsViews/DotsViewLibrary.asset` with pool
  sizes 4/32/64 and half-height lifts, validates and logs. Idempotent; throws on validation
  errors so a batchmode run fails loudly.
- Tests (`Assets/Tests/Editor`): `LeasedViewAssetProviderTests` (10, fake loader — lease
  refcount contract), `DotsViewLibraryValidationTests` (8), `DotsRegistrationTests` gains the
  production-mode wiring test.

### Changed (2026-09-07)

- **`DotsWorldBridge`** now builds its catalog from the `DotsViewLibraryAsset` through
  `ViewConfigCatalog.TryBuild` with the provider's own `prefabExists` (`LeasedViewAssetProvider.CanProvide`
  or the primitive provider's shape table) plus `ValidateMappings` against
  `DotsViewArchetypes.ServerKindMappings`; an invalid library logs every issue and disables the
  component. Prewarm is asynchronous and the binder starts ticking only when every key is warm.
  Session modules install with `DotsModuleScope.Session`: CameraFollow (targets the local
  player's mirror via `NetworkEntitySpawned`) and, opt-in, Minimap (with a category resolver on
  the `DotsEntityView`). Teardown: prediction → `DotsNetcodeBootstrap.Uninstall(destroyMirrors: true)`
  → `DotsModules.UninstallScope(Session)` → catalog → `Release(key)` for every catalog key
  (handles drop once the view layer recycled the last instance). On `NetworkClient.Reconnected`:
  `view.BeginGeneration()`, `predictor.Reset()`, `CameraFollowBootstrap.ResetSmoothing`.
- `DotsViewArchetypes` gains `All` and `ServerKindMappings`, the single table the resolver, the
  validator and the build gate all read.
- asmdefs: `NDC.Scripts.DI` and `NDC.Tests.Editor` reference `Unity.Addressables` +
  `Unity.ResourceManager`; `BuildScript.Editor` references `NDC.Scripts.DI` + `Cuvara.DOTS.Runtime`
  under a `CUVARA_DOTS` version define.

### Changed (2026-09-07)

- `com.cuvara.netcode` v0.30.0 → v0.31.0 (manifest + lock, hash `40b3e4f`): reconnect policy by
  disconnect cause (60 s budget anchored to the server's clock, verified live with a 45 s game-server
  freeze), operation-generation guard, monotonic heartbeat clock; ability-protocol types held back
  until wired. EditMode 722/722 on this project with that package.
### Fixed

- **Login cancellation and stale completions** (`NakamaSessionService`, `NakamaAuthProvider`,
  workspace audit F09). Every Nakama SDK call now receives the caller's `CancellationToken`
  (`canceller:`), and every login re-checks it *after* the HTTP call returns, before
  `ApplySession`: a round trip that completes in the same frame as the cancel no longer
  installs a session the player backed out of. Logins run under a new
  `Scripts.Nakama.Auth.OperationGeneration`: the newest login wins, an older one that
  completes later is discarded with `OperationCanceledException`, and `SignOut()`
  invalidates anything in flight. `RestoreSessionAsync` only clears persisted tokens when it
  still owns the outcome. `NakamaAuthProvider` mints the gateway token for the session it
  captured and discards the token if the session or login generation changed underneath the
  RPC; a cancelled RPC surfaces as a cancel, not as "Nakama RPC failed".
  `Assets/Tests/Editor/OperationGenerationTests.cs` pins the guard (7 EditMode tests, pure
  C#, no delays). The netcode half of F09 — one operation guard and try/finally ownership of
  both connections in `NetworkClient` — lives in the `com.cuvara.netcode` package
  (0.31.0) and reaches this repo with the next manifest + lock bump.
### Documentation

- Add a Cuvara DOTS improvement plan covering verified pooling ownership risks,
  incomplete modules, integration boundaries, profiling, acceptance tests and
  conditional gameplay-driven extensions. Planning only; no runtime changes.
### Fixed (2026-09-07)

- `Packages/packages-lock.json` recorded the three Cuvara packages as `embedded`
  (`file:com.cuvara.*`) while `Packages/com.cuvara.*/` is gitignored, so every fresh clone
  resolved them from the manifest's git URLs at first open and rewrote the lock. The lock now
  pins the resolved commits (`dots` v0.27.1 `dfcfddc`, `netcode` v0.30.0 `051f787`,
  `uitoolkit` v0.7.2 `3c6fde9`) — the lock is what resolves, a manifest-only pin is not
  enough.
- Addressables `Default Local Group` dropped eight entries (`InventoryPopup`, `SettingsPopup`,
  `ConfirmPopup`, `MainScreen`, `InfoPopup`, `InventoryItem`, `LoadingScreen`, `SecondScreen`)
  whose GUIDs no longer exist anywhere in the project; the Addressables build had been
  cleaning them on every player build. HUD `Hud.uss.meta` / `HudView.uxml.meta` importer
  fileIDs filled in by the Unity 6 importer.
### Added (2026-09-07)

- `tools/verify-multiclient.sh --redis-container NAME`: the two Redis rows (N session keys,
  exactly one `servers:map:<id>` member — ADR-2) now run against the docker compose stack via
  `docker exec`, not only against a k3d cluster via `--kube-context`. Against `stack.sh up` they
  were printed NOT CHECKED on every run and walked by hand. The two flags are mutually exclusive;
  the SKIP message names both. CLAUDE.md gains the compose-stack invocation.

### Added (2026-09-05 to 2026-09-06)

- Cuvara packages switched to git URL dependencies with gitignored local clones
- com.cuvara.dots v0.27.1: pooling, archetype presets, camera follow, lifecycle events, minimap, physics, overlay anchors, editor window, 42 new tests
- com.cuvara.netcode v0.30.0: connection state events, diagnostics, reconnection progress, server time, content ready, ability protocol, status effects, 15 new tests
- com.cuvara.uitoolkit v0.7.2: loading progress, confirm dialog, toast service, screen transitions, settings model, 26 new tests
- rpg-mmo-server: golden vectors, pgstore cleanup, drawio labels, TEAM.md, tagged v0.9.0
- Go 1.27, .NET SDK 10.0, GitHub CLI installed
- Workspace hygiene: BuildConfig, modules, stale samples, CI fixes

### Fixed

- All CI pipelines green across 5 repos
- ADR-3 sid check and ADR-7 entity leak confirmed resolved

## [0.5.0] - 2026-09-05

### Added

- **DOTS stress benchmark** (`Assets/Scripts/Benchmark/Dots/`, CLI flags
  `-stress-pure` / `-stress-hybrid`) — measures cuvara.dots simulation + Unity.Physics
  throughput at entity tiers 100 → 100M. Pure DOTS mode (no GameObjects) hits 1,246 FPS
  at 1K entities and 81 FPS at 1M with 300K physics bodies. Hybrid mode caps views at
  50K GameObjects and runs simulation-only beyond. Memory guard auto-skips tiers that
  would exceed 60% system RAM. Batch entity creation in 64K chunks avoids single large
  allocations. Reusable `StressBenchmark` sample added to `com.cuvara.dots` v0.25.0.
- **`WireConformance` tool fixed** — source paths updated from `Scripts.Net.*` to
  `Cuvara.Netcode.*` (code moved to package), Google.Protobuf NuGet reference added.
  All 49 wire format conformance tests pass.
- **`com.cuvara.dots` bumped to v0.25.0** — `StressBenchmark` sample entry added to
  `package.json` (pure DOTS + hybrid modes, documented in `Samples~/StressBenchmark/README.md`).

### Fixed

- **Unity batch test runner** — `-quit` flag before `-runTests` caused Unity to exit
  before the test runner started. Removing `-quit` allows all 540 EditMode tests to
  execute (539 pass, 1 false failure from MCP socket noise in `NetworkEntityViewTests`).
- **Device benchmark harness** (`Assets/Scripts/Benchmark/`, `Assets/Scenes/DeviceBenchmark.unity`,
  runbook in `docs/DEVICE-BENCHMARK.md`) — the project's first instrument for client
  performance on real hardware. `BenchmarkRecorder` (new `NDC.Scripts.Benchmark` assembly;
  no netcode reference, works in any scene) samples per-frame main-thread CPU time, GC
  bytes/allocations per frame, GC collections, and per-second memory + DOTS entity count via
  `ProfilerRecorder` counters verified against this editor version's player binary, into
  preallocated struct buffers (zero steady-state managed allocation — it measures GC, it
  must not feed it). It aggregates mean/median/p95/p99 frame ms, steady-state allocs/frame,
  and GC spike counts — warm-up window and per-phase settle windows excluded — then writes
  one JSON to `persistentDataPath`, logs it on a single `[BENCH-RESULT]` line for
  `adb logcat -s Unity`, and quits. Configurable via a `BenchmarkConfig` asset (the only
  surface that reaches an Android device) with `-bench*` command-line overrides on desktop.
  The benchmark scene ramps 250 → 500 → 1000 moving view-backed entities through the game's
  own `RegisterDots()` container (new `NDC.Scripts.Benchmark.Workload` assembly; HybridViews
  sample spawning pattern over the package's Burst simulation systems, half mob / half
  player-remote, deterministic seed) and drives the real `HudView` binding path with a
  synthetic once-per-second `HudViewModel` feed. EditMode tests cover the aggregation math
  and argument parsing; a PlayMode test covers the recorder over a live player loop.
  `MaxExpectedFps` defaults to 1000 (was 240): the first live run on desktop hit 595 fps
  uncapped and truncated the buffer at 24k samples, silently losing the 1000-entity phase —
  the sizing bound must cover an uncapped desktop run, not a device target.
- **`-bench` any-scene activation** (`BenchmarkBootstrap`) — launching any player with
  `-bench` spawns a `DontDestroyOnLoad` recorder into whatever scene boots (the netcode
  DOTS sample included) with `-bench-duration`/`-bench-warmup`/`-bench-label` control.
  Default mode is rolling windows: a labeled, window-indexed JSON is written and logged at
  every window boundary while the player keeps running (a connected netcode client must
  stay up); `-bench-quit` opts into single-window-then-quit. The recorder still reads only
  engine/profiler counters — no netcode reference.
- **`PlayerBuilder -development` flag** — adds `BuildOptions.Development` for builds that
  need profiler counters in the player (the device benchmark is the consumer). Absent, the
  build is unchanged.

### Changed

- **`Tools/run-clients.sh` gained `-- ARGS...` passthrough** — everything after `--` is
  handed to every player instance verbatim, so the multi-client harness can launch with
  the `-bench` flags (or any future per-instance player flag) without editing the script.
- **`PlayerBuilder -bootScene` accepts scenes outside Build Settings** — a boot scene that
  is not in the enabled set but exists on disk is now prepended for that build only, so
  harness-only scenes (`DeviceBenchmark.unity`) can boot without ever being enabled or
  shipped. A path matching neither remains a hard error.
- **DOTS → UI Toolkit HUD bridge** (`Assets/Scripts/UI/Hud/`, docs in `docs/HUD-BRIDGE.md`) —
  ECS world data now reaches a UI Toolkit HUD through the packages' existing seams, with the
  packages staying mutually unaware. A game-side `HudStateSystem` (`SimulationSystemGroup`)
  aggregates the netcode mirrors (`NetworkEntity` + `NetworkEntityState` + `LocalTransform`)
  into a `HudState` singleton — local player hp/max-hp, 0.1-quantized position, player and
  entity counts — writing only on change so the uitoolkit bridge's chunk change filter stays
  exact. `HudBridgeSystem` (an `EcsViewModelBridge<HudState, HudSnapshot>` in
  `PresentationSystemGroup`) converts to a boundary snapshot; `HudPresenter` (the
  `IViewModelSink`) writes a `HudViewModel : BindableViewModel`; `HudView` binds the enrolled
  `HudView.uxml` via `SetBinding` + `nameof` + `Require<T>` (committed
  `Generated/HudView.uxml.g.cs`, hybrid data-binding convention). `HudWorldBridge` hosts the
  `UIDocument` and joins the lifetimes (`EcsSinkRegistration`; teardown sink → systems → view)
  — a lightweight host because no uitoolkit screen flow exists yet. New gated assembly
  `NDC.Scripts.UI.Hud.Ecs` (`CUVARA_DOTS` + `CUVARA_NETCODE` + `CUVARA_UITOOLKIT_ENTITIES`);
  the ViewModel/View halves compile with no ECS installed. Tests: EditMode
  (`HudViewModelTests`, `HudSnapshotTests`, `HudPresenterTests`, `HudEcsLifecycleTests`
  against a throwaway world) and the project's first PlayMode assembly
  (`Assets/Tests/Runtime`, `HudViewBindingTests` on a live `UIDocument`).
- **`com.cuvara.dots` wired into the client** — the package was installed but orphaned
  (in `manifest.json` and `testables`, referenced by nothing). `GameLifetimeScope` now calls
  a new `RegisterDots()` (`Assets/Scripts/DI/Dots/`): MessagePipe brokers for the package's
  five messages **before** `RegisterDotsViews` (its adapters resolve `IPublisher<T>` at
  container build), a `PrimitiveViewAssetProvider` fallback as `IViewAssetProvider` (the
  container registers no GameFoundation `IAssetsManager`/`IObjectPoolManager`, so
  `RegisterGameFoundationViewProvisioning()` cannot be used yet), `RegisterSimulationModel()`
  (binds `SharedGameLogicSimulation`, authoritative), and the session's single
  `LocalMovePredictor` built from `GameConstants`. `MainSceneScope` injects a new
  `DotsWorldBridge` scene component that hangs the netcode adapter (`DotsEntityView` +
  `DotsNetcodeBootstrap`) and prediction driver (`DotsPredictionBootstrap`) off the same
  container-owned `NetworkClient`, ticks `WorldViewBinder` per frame (no-predictor overload —
  required with the DOTS adapter), and tears down in the documented order. `NDC.Scripts.DI`
  gains the package/MessagePipe/Entities references and its own `versionDefines`
  (`CUVARA_DOTS`, `CUVARA_DOTS_VCONTAINER`, `CUVARA_DOTS_MESSAGEPIPE`, `CUVARA_NETCODE`,
  `CUVARA_SHARED_GAMELOGIC`) — defines do not flow from package asmdefs. See
  `docs/DOTS-WIRING.md` for the shape, the provider decision, and the traps respected.

- **First Assets-side test assembly** — `Assets/Tests/Editor` (`NDC.Tests.Editor`, EditMode):
  `DotsRegistrationTests` proves the container builds through `RegisterDots` and resolves the
  view layer, MessagePipe-backed publishers, the authoritative simulation model and a single
  predictor instance; `DotsBootstrapLifecycleTests` proves the bridge's install/uninstall
  sequence creates the expected group tree and singletons, is idempotent, and leaks neither
  singletons nor the catalog blob.

- **`com.cuvara.uitoolkit` bumped to 0.4.0 — hybrid data-binding convention.** Unity 6
  runtime data binding is now allowed inside the package's MVP screens, strictly as a
  View-internal detail behind the existing `IView` interfaces: a new `BindableViewModel`
  base (notify-on-real-change is mandatory — a non-notifying source is version-polled
  every UI update), the EcsHud sample retrofitted as the reference hybrid screen
  (`Root.dataSource` + `SetBinding`, `nameof` paths, `BindingMode.ToTarget`, UXML enrolled
  in the codegen), and `Documentation~/HYBRID-DATA-BINDING.md` with the per-screen
  decision table. Commands and navigation stay on `ScreenSubscriptions`; see the package
  changelog for detail.

- **Content pipeline** — item definitions now come from the game server at runtime instead
  of being something the client would ship in its build (ADR-19). The client half lives in
  `com.cuvara.netcode` as `Cuvara.Netcode.Content`, with a `Content Pipeline` sample scene
  built in UXML; see `docs/CONTENT-PIPELINE.md` and the package changelog for detail.
  - `com.cuvara.netcode` bumped to **0.17.0** for the new public namespace and sample.

### Changed

- **`com.rpgmmo.shared-gamelogic` bumped to `sgl-v0.3.1`** in both `manifest.json` and
  `packages-lock.json` (both files, because the lock is what resolves). 0.3.1 names the
  out-of-range attack rejection as the interned constant `CombatLogic.OutOfRangeRejection`
  (rpg-mmo-server#249 follow-up); the message text is unchanged (`target out of range`),
  and the client's golden vectors assert only that prefix, so no client fixture changed.

- **`com.rpgmmo.shared-gamelogic` bumped to `sgl-v0.2.1`** in both `manifest.json` and
  `packages-lock.json`, for the new `Shared.GameLogic.Content` namespace. Both files, because
  the lock is what resolves — a manifest-only bump silently keeps the old commit.
  `0.2.0` shipped that namespace without `.meta` files, so Unity never imported it and the
  client could not see `Shared.GameLogic.Content` at all; `0.2.1` is the tag that works.

- **Account recovery** (`NakamaSessionService`, `docs/ACCOUNT-RECOVERY.md`). Until now a
  character was bound to `SystemInfo.deviceUniqueIdentifier` and nothing else: reinstalling,
  wiping or replacing the phone lost it permanently, with no second credential pointing at
  the account and no support path to restore one. `NakamaAuthProvider` restored a session or
  fell back to device auth, and nothing ever attached anything durable.
  - `LinkEmailAsync` attaches a recovery credential to the *current* account, additively —
    the device id keeps working and the player is not signed out.
  - `RecoverWithEmailAsync` signs in to that account from another device.
  - `GetRecoveryEmailAsync` / `IsRecoverableAsync` report whether an account has been linked
    yet, for a settings screen to branch on.
  - `UnlinkEmailAsync` detaches it, and logs a warning: afterwards the account is device-only
    and unrecoverable again.

### Changed

- **`AuthenticateEmailAsync`'s `create` parameter lost its default**, which was `true`. That
  default is the wrong value for recovery and wrong in a way that is silent: an email Nakama
  does not recognise produces a brand-new empty account and a login that looks entirely
  successful. A player mistyping their address during recovery would land in a fresh
  character with no items and no progress, with no error raised anywhere — the client
  authenticated, the gateway resolved an identity, the session was valid, it was just the
  wrong account. Callers must now state which they mean. The method had no callers, so
  nothing broke.
  - Use `RecoverWithEmailAsync` for recovery; it pins `create: false` so an unknown email
    fails loudly instead of silently succeeding into the wrong place.

- **`unity-build-workflows` bumped `f5616af` → `c4ceb8e`** ("gate Final Report on every result,
  and fail on cancelled"). Provisional: that commit sits on the submodule repo's
  `fix/final-report-gate` branch and is not merged there yet, so the pin should be re-pointed
  once it lands.

### Fixed

- **`Assets/Scripts/UI.meta` is now tracked.** The folder it describes has been in the repo all
  along, and every sibling folder meta (`DI.meta`, `Extensions.meta`, `Nakama.meta`) was
  committed — this one never was, so Unity regenerated it with a fresh guid on every machine
  that opened the project and it kept surfacing as an untracked file.
- **Removed a duplicate `Screen Flow` sample import** at `Assets/Samples/Cuvara UIToolkit/`.
  All 11 files were byte-identical to the tracked copy under
  `Assets/Samples/Cuvara UI Toolkit/0.1.0/Screen Flow (scene)/`, but the duplicate carried no
  `.asmdef`, so its `ScreenFlowSample.cs` and `ScreenFlowSampleScope.cs` compiled into
  `Assembly-CSharp` alongside the same types in `Cuvara.UIToolkit.Samples.ScreenFlow` — the
  ghost-duplicate condition the package's own `1d28bc8` was written to break.

### Added

- **`com.cuvara.uitoolkit` vendored at v0.2.0** — screen flow system, Loading Flow sample with
  full feature coverage (push/pop/replace/popToRoot, modal overlays, model parameters, lifecycle
  hooks, back navigation, list adapter, per-scene scoping). All samples now ship assembly
  definitions so they compile on import.

## [v0.4.2] — 2026-08-21

### Fixed
- **`MainScene` was missing from every build, and the player booted the netcode sample.**
  `EditorBuildSettings` had been reduced to a single enabled scene — the vendored DOTS
  sample — when `MainScene` was dropped in f57117e alongside an unrelated sample reimport.
  Index 0 decides what the player boots, so every artifact built since then started in the
  netcode sample, and `MainScene` was not merely misordered but absent from the build
  entirely. Both scenes are enabled again, with `MainScene` at index 0.

- **Three test-harness display settings were shipping in `ProjectSettings`.** f57117e set
  them for tiling three player windows on one desktop and they were never reverted:
  `defaultScreenWidth`/`defaultScreenHeight` 800x600 -> 1024x768, `defaultIsNativeResolution`
  0 -> 1, `fullscreenMode` 3 (windowed) -> 1 (fullscreen window), `runInBackground` 1 -> 0.
  Every value is restored to what it was immediately before that commit. The web defaults
  (`defaultScreenWidthWeb`/`Height` 960x600) were not touched by f57117e and are unchanged.

- **`bundleVersion` was still `0.1.0` after two tagged releases.** It had never tracked the
  `v0.4.x` tags. Set to `0.4.2`, matching this release.

### Added
- **`PlayerBuilder` accepts `-bootScene <path>`**, which moves the named enabled scene to
  index 0 for that build only. Restoring `MainScene` to index 0 was correct for the release
  but would have broken the three-client multiplayer harness in `CLAUDE.md`, which needs a
  player that boots the DOTS sample. The flag resolves that without either scene leaving the
  build and without a committed `EditorBuildSettings` edit per harness run. An unmatched path
  fails the build rather than silently falling back to index 0. The documented harness
  command now passes it.

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
- **`unity-build-workflows` submodule bumped `43229d2` -> `f5616af`** (9 commits).

  **This is hygiene, not delivery.** The three toolkit workflows this repo calls are referenced
  `@main` — `unity-pipeline.yml@main`, `unity-generate-license.yml@main`,
  `unity-license-check.yml@main` — so toolkit changes reach CI the moment they merge, with or
  without this pointer. The submodule is a local reference; `update-submodule.yml` is supposed to
  keep it current and has been failing since 2026-08-10 (the bot App has no `contents` permission,
  so it cannot push the branch its PR needs).

  Of the 9 commits, only one touches a workflow this repo runs: the `Library` cache gaining a bare
  `Library-` restore-key fallback. The rest are docs and `com.company.build-pipeline` sources —
  and that package reaches the client through a **UPM git tag** (`#v1.1.3`), not through this
  pointer, so the bump does not move it.

- **`com.cuvara.netcode` re-vendored 0.16.1 -> 0.16.3**, byte-identical to upstream `v0.16.3`.

  Test-only upstream: `0.16.2` fixed `PredictionSurfaceContractTests` resolving the prediction
  surface by name, which threw the moment a second overload existed; `0.16.3` taught
  `PredictionLatencyMeasurement` to measure the **unseeded** base tick, which it could not do
  before — it drives the predictor through `WorldViewBinder`, and the binder seeds on every
  snapshot, so every run it had ever produced was already the "after".

  That measurement is why this is worth vendoring rather than skipping. Against staging, medians
  of 3 interleaved runs, 20/20 usable samples, 60 Hz advertised and 60.0 Hz measured off the wire:
  **max correction 0.0833 world units unseeded, 0.0000 seeded**. `0.0833` is speed 5 ÷ 60 Hz —
  *exactly one base tick of movement*, which is what a one-tick phase misalignment produces, so
  the number and the documented mechanism corroborate each other. The reconcile count (140 vs 162)
  is inside the unseeded arm's own spread and is **not** a result.

  No runtime assembly changed between 0.16.1 and 0.16.3, so nothing about transport, codec,
  handshake, snapshots or prediction moves for the player.

  `Assets/Samples/Cuvara Netcode/0.16.1` was renamed to `0.16.3` and `EditorBuildSettings`
  repointed. `Samples~` is byte-identical across the two releases, so this is a rename rather than
  a re-import — but the directory name still had to move, because the imported-sample check
  compares it against the package version and a stale name is exactly the lag that once shipped a
  player ignoring every backend flag.

  `packages-lock.json` needs no edit: the package is `embedded` (`file:`), so the lock carries no
  version, only the dependency set, which is unchanged (verified).

- **`--nakama-key` is now required when running clients by hand.** The Nakama server keys were
  rotated on 2026-08-20 and each backend cluster has its own, so the flag's `defaultkey` default
  no longer authenticates anywhere.

  Documented rather than defaulted differently, because there is no value that would be right for
  every backend.

  **Correction to the first version of this entry.** It claimed omitting the flag "shows up as a
  client that never reaches `IN WORLD` rather than as anything naming the key". Measured against
  staging afterwards, that is wrong in the half that matters: the player does launch and never
  reaches `IN WORLD`, but the log names the cause outright —
  `[DOTSNet] FATAL: UnityWebRequestException: HTTP/1.1 401 Unauthorized`. The diagnosis is one
  line away, not hidden. Overstating how opaque a failure is sends the next person looking in the
  wrong place, which is the same cost as understating it.

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
- **The Addressables watchdog now leaves with code 0 instead of killing the process.** The previous
  revision turned a 120-minute hang into an 8.5-minute *failure*; a build that succeeded must not
  report failure.

  **The experiment it was built for returned a result.** Measured 2026-08-21, job
  `03:03:57 → 03:12:26`:

  ```
  [AddressableBuilder] Addressables build succeeded   ← the build finished
  WATCHDOG FIRED                                      ← EditorApplication.Exit(0) then stalled
  ```

  So the deadlock **is** in managed shutdown — that was the open question, and it is now answered
  rather than assumed. `Process.Kill()` on yourself yields 137, which GameCI reads as a failed
  step, hence the wrong colour on a good build.

  `Environment.Exit` is not the fix either: it runs finalizers and AppDomain unload, which is
  precisely the machinery that is stuck. libc `_exit` returns to the kernel without touching any of
  it. The lane builds `StandaloneLinux64` inside the GameCI container, so libc is there; the
  `Process.Kill()` fallback covers anything else and is no worse than what shipped before.

  The warning is logged, then the thread sleeps two seconds before exiting: `_exit` flushes
  nothing, and without that pause the one piece of evidence that this path was taken can be lost
  with the buffer.

- **The Addressables CI job hung for the full 120-minute timeout after a build that took eight
  minutes.** Bounded to 30 seconds by a watchdog, and instrumented so the next run says which half
  of the problem it is.

  Measured from the cancelled 2026-08-20 staging run:

  | | |
  |---|---|
  | `11:28:21` | `[AddressableBuilder] Addressables build succeeded` |
  | `11:28:25` | `Cleanup mono` — teardown ran, and ran far |
  | `11:28:58` | `[AI] BufferedFileLogStorage Flush called but already disposed` |
  | `13:20:04` | cancelled by the timeout |

  **111 minutes of total silence.** The build was not slow — the process would not terminate.
  Raising `BUILD_TIMEOUT_MINUTES` would only have bought a longer wait for a dead process, and the
  job takes every platform build down with it when it is cancelled.

  `EditorApplication.Exit(0)`, added on 2026-08-12 for this exact symptom, **did help and did not
  fix it**: the 08-12 hang stopped at "Batchmode quit successfully invoked", this one reached
  "Cleanup mono", which is very late in Unity's teardown.

  **The obvious suspect does not survive contact with the evidence.** `com.ivanmurzak.unity.mcp`
  is the last thing to speak, but the Android build on the same day logs the *identical* warning
  one second after its own `Cleanup mono` and exits cleanly. Both paths run
  `-batchmode -quit -executeMethod` with the same flags. Why this one stalls is **not established**,
  and is not claimed here.

  The watchdog is a background thread, so it costs nothing when the exit works — it dies with the
  process. It is also the experiment: if a future log carries its warning line, the graceful
  shutdown stalled and the deadlock is in managed teardown; if it never appears,
  `EditorApplication.Exit` was fine and the stall is somewhere that line cannot see.

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


## [0.4.0] - 2026-09-03

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
