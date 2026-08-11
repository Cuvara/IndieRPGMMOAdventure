# Changelog

All notable changes to this project are documented here.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

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

  Now at **`sgl-v0.1.1`**, and verified in the Editor: `Shared.GameLogic.dll`
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

  **3 of 96 tests fail, and the failures are real.** All three trace to one
  expression — `x * x + y * y`, in `Vec2.SqrMagnitude` and in
  `MovementSystem.ResolveDirection`'s `magSq`. Unity's Editor Mono JIT evaluates
  it with double-precision intermediates and one final rounding; .NET 10 on the
  server evaluates it strictly in float32, and the results differ by one ULP.
  Reproducing both evaluation orders by hand reproduces both results exactly on
  all three cases, which rules out a fixture or reader bug. C# permits either, so
  the fix belongs in `Shared.GameLogic` (explicit `(float)` casts force the
  intermediate rounding), not in the client. **Left red on purpose** — a tolerance
  comparison would pass and delete the finding, which is the one thing this gate
  exists to prevent.

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
