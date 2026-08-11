# Client netcode — transport, codec, handshake

The `NDC.Scripts.Net` assembly (`Assets/Scripts/Net/`) is everything the client
puts on the wire: framing, encoding, the two-hop handshake, the heartbeat, and
entity-handle resolution. It contains **no game rules** — no movement
integration, no damage, no validation. Those are server-authoritative and live in
`Shared.GameLogic` in the backend repo (ADR-10); a client-side copy would
silently diverge from the server, which is the failure this architecture exists
to prevent.

The wire contract is `backend/shared/proto/wire.proto`, with
`backend/gameserver-dotnet/docs/API.md` and `backend/gateway/docs/API.md` as the
normative references. Where those disagree with the server source, the source
wins; see "Known doc/source disagreements" at the end.

## Two connections, not one

The gateway is a redirector and is **never** in the gameplay data path (ADR-3).

```
1. client -> gateway      auth{token}              -> auth_resp{ok, user_id}
2. client -> gateway      enter_world{map_id}      -> enter_world_resp{server_addr, join_token, transport}
3. client -> game server  join_token{token}        -> join_token_resp{ok, user_id}      (direct, not proxied)
4. client -> game server  input{...}      per tick
   game server -> client  snapshot{...}   per tick
```

| | Gateway connection | Game-server connection |
|---|---|---|
| Purpose | auth + map assignment | the session |
| Lifetime | can be dropped after step 2 | held for the whole time on that map |
| Kept open by default? | yes, see below | yes |
| Class | `Client/GatewayClient` | `Client/GameSessionClient` |

`NetworkSettings.KeepGatewayConnection` defaults to **true** even though nothing
requires it after step 2. Two reasons: eviction (`duplicate_login`) is only ever
pushed on the gateway connection, and the gateway destroys its session record
when the socket closes. Set it false and the client simply never learns it was
displaced by another login.

`Client/NetworkClient` drives both hops and is the type to inject.

## Assembly layout

| Folder | Contents | Engine-free? |
|---|---|---|
| `Protocol/` | `MsgType`, message DTOs, kick reasons | yes |
| `Json/` | minimal JSON parser and writer | yes |
| `Codec/` | encoding sniff, `IWireCodec`, the JSON codec | yes |
| `Snapshot/` | handle table, snapshot resolution | yes |
| `Transport/` | framing, `ITransport`, TCP implementation | framing/endpoint yes; TCP uses UniTask |
| `Connection/` | `WireConnection` — loops, heartbeat, close semantics | UniTask |
| `Client/` | `GatewayClient`, `GameSessionClient`, `NetworkClient`, settings | UniTask |
| `Diagnostics/` | `INetLog` and its Unity implementation | `UnityNetLog` only |
| `DI/` | `IContainerBuilder.RegisterNetworking()` | VContainer |

"Engine-free" is load-bearing, not tidiness: `Tools/WireConformance` compiles
those folders with `dotnet` and asserts the wire format outside Unity. Run it
after any change to the codec or to handle resolution:

```bash
cd Tools/WireConformance && dotnet run     # prints ALL CHECKS PASSED, exit 0
```

> **This assembly has never been compiled by the Unity Editor.** It was written
> without one available. Two offline checks stand in for that, and neither is a
> substitute: `Tools/WireConformance` type-checks and exercises the engine-free
> half, and the whole assembly was additionally compiled clean (0 errors,
> 0 warnings) with `dotnet` at `LangVersion 9` / `netstandard2.1` against the real
> Unity 6000.3.9f1 engine DLLs plus UniTask and VContainer sources. Assembly
> definition resolution, IL2CPP and platform defines are still unverified — expect
> to fix something on the first Editor import.

## Registration

```csharp
// GameLifetimeScope.Configure — root scope, so the socket survives scene loads.
builder.RegisterNetworking(new NetworkSettings
{
    GatewayHost = "127.0.0.1",
    GatewayPort = 8000,
});
```

```csharp
public sealed class Connector
{
    private readonly NetworkClient _net;

    public Connector(NetworkClient net) => _net = net;

    public async UniTask GoAsync(string jwt, CancellationToken ct)
    {
        _net.SnapshotReceived += s => { /* hand to the merge layer */ };
        _net.GatewayClosed    += i => { /* i.Cause == Kicked -> evicted */ };
        _net.SessionClosed    += i => { /* session over */ };

        await _net.ConnectAsync(jwt, "map_01", ct);
        _net.Session.SendInput(tick: 1, moveX: 1f, moveY: 0f);
    }
}
```

## Framing

`[4-byte big-endian length][body]`, body at most 1 MiB. A length of zero, a
negative one (the high bit set) or one above the cap is a protocol error, not
something to allocate for.

## Encoding: sniffed, not negotiated (ADR-9)

The body is either Protobuf or legacy JSON, and the receiver tells them apart
from the **first body byte**: `0x08` is Protobuf (proto3 always emits field 1,
`type`, which is >= 1), `0x7B` (`{`) is JSON. No version field, no handshake.

Where the latch lives, and why there are two halves of it:

- **Outbound** — one codec per `WireConnection`, fixed at construction and never
  changed. Both servers latch *their* reply encoding from the first frame we
  send, so switching ours mid-connection would silently switch theirs.
- **Inbound** — sniffed per frame, never assumed to match. Gateway eviction
  frames are written as JSON whatever the connection latched, because the
  gateway builds them off the victim connection's goroutine and may not read its
  latched encoding from there. A per-frame sniff makes that a non-event.

### Protobuf is not implemented

Only `JsonWireCodec` exists. Both servers still accept JSON, so the client works
end to end today, but this is the legacy path and it costs bandwidth: Protobuf
plus the entity-type enum plus id interning is **81% smaller** on the wire.

To add it:

1. Add a Protobuf runtime for Unity (`Google.Protobuf`, via NuGetForUnity or a
   vendored DLL) and generate C# from `backend/shared/proto/wire.proto` — do not
   hand-write the types, and do not add a third definition of the schema.
2. Implement `IWireCodec` over the generated `Envelope`, mapping the
   `EntityType` enum to the same names `GameServer/Net/EntityTypes.cs` uses
   (`player`, `mob`, `npc`, `item`, `projectile`), preferring `type` and falling
   back to `type_name`.
3. Register it in place of `JsonWireCodec` in `NetworkingRegistration`. Nothing
   else changes: `WireConnection` already sniffs inbound frames, and
   `SnapshotResolver` already implements interning.

## Heartbeat — implemented once

Both hops ping every **10 s** and drop a peer after **30 s** without a pong, so
`WireConnection` implements it once and both use it. Ours replies to a `ping`
regardless of session state, exactly as both servers do, and the delay is
`DelayType.Realtime` so a paused or slowed game is not dropped at 30 s.

The gateway pings from the moment it accepts the socket, so a heartbeat can land
in the middle of the handshake, before the loops start. `GatewayClient` answers
those inline while waiting for the frame it came for.

## Eviction: `kick` then `disconnect` is **one** event

```
gateway -> client   kick(15)        {reason}
gateway -> client   disconnect(9)   {same reason}
                    <FIN>
```

`WireConnection` reports `DisconnectCause.Kicked` on the `kick`, sets an
`_evicted` flag, and **ignores** the `disconnect` that follows. Without that flag
every eviction is reported twice. The subsequent FIN is ignored too: `Closed` is
raised exactly once per connection, guarded by an interlocked flag, and the first
cause recorded wins.

An unpaired `disconnect` — a game-server drain (`server_shutdown`), or an
eviction from a gateway build that predates `kick` — is reported as
`DisconnectCause.ServerDisconnect` with its reason. `duplicate_login` and
`server_shutdown` are the only reasons emitted today; anything else is handled
generically.

## Join tokens are single-use

30-second TTL, one `jti` the game server consumes exactly once, and a `sid`
pinned to one server. A retry must call `enter_world` again for a **fresh**
token; replaying one is rejected with `Token already used`, which would turn a
transient failure into a permanent one. `NetworkClient` retries that way, up to
`NetworkSettings.JoinAttempts`.

## Snapshots and entity-handle interning

Snapshots are delta-encoded. `full = true` is a keyframe: the complete AOI set,
and everything not listed is discarded. `full = false` carries only changed
entities plus a `removed` list of ids.

On the Protobuf wire, entity ids are **interned**: `id` appears only on the
message introducing a `handle`, and later mentions carry the handle alone.
Handles are allocated from 1, reset at every keyframe, and never reused within an
interval. The JSON encoding never interns, so on today's JSON path every entity
carries its id — the resolver is still in the path so that turning Protobuf on
changes nothing above it.

`SnapshotResolver` implements the rules, and one of them decides the design:

> **If a handle does not resolve, do not guess.** Apply nothing from that
> snapshot and send `resync`.

Not "skip the entity", not "use the last one seen". A wrong resolution renders a
real entity in the wrong place and nothing detects it; absent state is loud and
self-repairing. A resolve failure therefore rejects the **whole** snapshot,
records no new bindings, and triggers one `resync` — repeated requests are
suppressed until a keyframe arrives, because a resync costs a full AOI snapshot.

On a keyframe the handle table is cleared **before** resolving, so a handle-only
entity on a keyframe is unresolvable rather than resolved against the previous
interval's bindings. (The Go reference implementation clears it *after*; see
below.)

`ack_tick` is surfaced on `GameSessionClient.AckTick` and on every
`ResolvedSnapshot`, monotonically — a snapshot that omits it carries zero and
must never lower it. Nothing here consumes it: it is the reconciliation anchor
for the prediction workstream.

**The merge is deliberately not implemented here.** `ResolvedSnapshot` is handed
out with ids resolved, and reconstructing world state from keyframes and deltas
is `Shared.GameLogic.Systems.SnapshotMerger`'s job — the same code the server was
diffed against. A second copy in the client is the divergence ADR-10 exists to
prevent.

## Not implemented

| | Status |
|---|---|
| Protobuf codec | interface and sniff in place; no implementation (see above) |
| KCP transport | `DefaultTransportFactory` throws rather than silently downgrading to TCP — a KCP server is not listening on TCP at all, so a fallback would surface as an unexplained connection refusal |
| WebGL | `System.Net.Sockets` is unavailable there; needs a WebSocket `ITransport`, which the gateway does not speak today either |
| Map transfer (13/14) | `transfer_map` is not sent; an inbound `transfer_map_resp` decodes to a null payload and is logged |
| Reconnect / resume | none. A closed session is reported, not retried; the server holds the entity 30 s (60 s in a dungeon) |
| World merge, prediction, reconciliation | out of scope by design |

## Known doc/source disagreements

Found while implementing, against `develop` of the backend repo. None of them are
worked around silently; the code follows the source.

1. **`Shared.GameLogic.Systems.SnapshotMerger` does not implement interning.**
   `gameserver-dotnet/docs/API.md` calls it the normative C#/Unity reference for
   the merge, and in the same section says a client MUST implement interning to
   read Protobuf snapshots. It keys entities by `EntitySnapshotData.Id`, and that
   type has no `handle` field at all — so a Unity client that used it against a
   Protobuf connection would key every non-introducing mention under the empty
   string. That is why resolution lives in this assembly, ahead of any merge.
2. **The keyframe clear happens in the other order in the Go reference.**
   `messages.SnapshotState.Apply` resolves handles *before* clearing the table on
   a keyframe, so a handle-only entity on a keyframe resolves against the
   previous interval's bindings — the one path by which a handle can resolve to
   the wrong entity. The C# doc's rule 4 ("clear before applying") is the safe
   one and is what this client does.
3. **`MsgKick` is not on `develop`.** The kick/disconnect pair is implemented
   only on the unmerged branch `docs/wire-protocol-accuracy-and-kick`; on
   `develop` and `main`, `kickLocalUser` sends `disconnect{duplicate_login}`
   alone, and `main`'s `shared/messages` has no type 15 at all. The client
   handles both shapes.
4. **The game-server API doc omits the heartbeat.** Its message table stops at
   type 10, but the server implements ping/pong (11/12) and its
   `HeartbeatLoopAsync` closes a connection after 30 s without a pong. A client
   built strictly from that table is dropped every 30 s with no explanation.
5. **The game server does send a reason on shutdown.** The same doc lists
   `disconnect` as `{}`, and the gateway doc says shutdown "closes sockets
   without a frame" — true of the gateway, but the game server's
   `DrainClientsAsync` sends `disconnect{reason:"server_shutdown"}` to every
   connection before tearing them down.
6. **`boss` is not an entity type.** The doc gives `entities[].type` as
   `player | npc | mob | boss`. `GameServer/Net/EntityTypes.cs` and the schema
   enum know `player`, `mob`, `npc`, `item`, `projectile` — no `boss`. (The
   string survives in a `Shared.GameLogic` comment.) A `boss` would arrive in
   `type_name`, not `type`.
