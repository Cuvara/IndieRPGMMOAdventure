# Changelog

All notable changes to this project are documented here.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Added

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
