# Content pipeline — the client half

Game content (item stats, and the content types that follow) is **not** shipped in the
client build and **not** carried by the `Shared.GameLogic` package. It lives as JSON on the
game server and the client downloads it. The decision and its reasoning:
[ADR-19](https://github.com/Cuvara/rpg-mmo-server/blob/develop/backend/docs/ARCHITECTURE-DECISIONS.md).

## Why the client downloads it

The only channel between the two repos is `Shared.GameLogic`, pinned by exact commit in
`packages-lock.json`. Putting item stats in it means one balance tweak costs a commit, an
`sgl-v` tag, a bump of **both** `manifest.json` and `packages-lock.json`, and a Unity
re-resolve.

That is right for simulation rules — they must change on both sides at once or prediction
diverges. It is wrong for content, which changes constantly and whose value is iteration
speed. So content takes a different road: server files, HTTP, no client build involved.

## Using it

```csharp
var content = new ContentClient();
await content.FetchAsync("http://127.0.0.1:9100");   // game server metrics origin

if (content.Database.TryGetItem("iron_sword", out var sword))
{
    Debug.Log($"{sword.Name}: atk {sword.Attack}, slot {sword.Slot}");
}
```

`ContentClient` lives in `com.cuvara.netcode` (`Cuvara.Netcode.Content`).
`ContentClient.Database` is a `Shared.GameLogic.Content.ContentDatabase` — the **same type
the server simulates against**, so there is no client-side mirror of the schema to keep in
sync.

## Caching is by hash, never by time

The client stores the document and its hash, and sends `?hash=` on every fetch. An unchanged
set answers `304 Not Modified` with no body.

There is no TTL because content does not expire — it changes when a server restarts with
different files, and the hash is how that is detected. A time-based cache would either
re-download content that had not changed or serve content that had.

`ContentClient.Source` reports where the current content came from: `Network`, `Cache`, or
`Local`. On a second run against an unchanged server it reads **`Cache`**.

### The hash header, and why two of them

The server returns the hash in both `ETag` and `X-Content-Hash`. The client prefers the
second. `UnityWebRequest` and several proxies rewrite or strip `ETag`, and a client that
cannot read back the hash it was just given can never send `?hash=` — so every launch
silently re-downloads the whole set while appearing to work perfectly.

## The client validates what it downloads

`ContentJsonReader` runs `ContentValidation` — the same rules the server ran before serving
it. This is not distrust of the server. A truncated or half-written response is
indistinguishable from a valid one until something checks it, and without the check the
client meets the problem as a null reference several screens later.

**It grants the client nothing.** Validation answers "is this content coherent", never "may
this player have this item". The server owns every gameplay decision; a client that edits
its own copy changes only what it draws.

## Parser is per-side, schema is not

The server parses with source-generated `System.Text.Json`. The client parses with the
netcode package's hand-written reader. Both construct the **same** `ItemDefinition` type and
run the **same** validator.

The split is forced, not preferred: Unity compiles `Shared.GameLogic` as source and has no
`System.Text.Json`, while the server publishes NativeAOT and cannot use reflection. No
single parser satisfies both runtimes. Sharing the schema and the validator keeps both sides
agreeing on what content *means*, which is the part that matters.

## Sample scene

The sample ships in the netcode package: **Package Manager → Cuvara Netcode → Samples →
Content Pipeline → Import**, then open `Scenes/ContentPipeline.unity` and press play.

```bash
# in rpg-mmo-server
cd backend/gameserver-dotnet
JOIN_TOKEN_SECRET=dev JWT_SECRET=dev dotnet run --project GameServer -- \
  --addr=:9000 --metrics-addr=:9100 --content-dir=../content
```

Then play the scene. What to look for:

| Run | Expect | Meaning |
|---|---|---|
| First | `Source: Network`, 8 items | Full download |
| Second | `Source: Cache` | Server answered 304, no body crossed the wire |
| After **Clear cache** | `Source: Network` again | Cache key removed, full download |
| With no server | `Source: Local` and a fetch-failed message | Offline fallback — scene only, see below |

The sample falls back to an inline stub when no server answers, so it still demonstrates
something. **A real client must not do that**: content it invented is content the server
never validated, and every number in it would be a guess shown to a player as fact.

## Failure modes

| Situation | What happens |
|---|---|
| Server unreachable | `ContentException`. Fatal to a join — a client with no item definitions cannot render inventory or loot |
| Response has no hash header | `ContentException`. Without it caching is impossible and every launch re-downloads |
| 304 but the local cache is empty | Cached hash is cleared and the next fetch downloads in full, rather than looping on a 304 it cannot satisfy |
| Cached document no longer parses | Cache cleared, same recovery |
| Content fails validation | `ContentException` naming every failed rule |
