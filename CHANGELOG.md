# Changelog

All notable changes to this project are documented here.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

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
