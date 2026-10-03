# Core v3 — the client leg (wire protocol 3)

How the client uses the protocol 3 features of the server (ADR-28..31,
`rpg-mmo-server/backend/docs/ARCHITECTURE-DECISIONS.md`) through `com.cuvara.netcode` 0.46.0
and `com.rpgmmo.shared-gamelogic` 0.7.0. Everything here degrades to the protocol 2 behaviour
against a protocol 2 server.

## The session, in order

`MainSessionFlow` (pure, tested) driven by `MainSessionDriver`:

```
Authenticating device=…  ->  Auth OK, user_id=…  ->  character: <name> (id=…)  ->  [party]  ->  IN WORLD as …
```

The `Auth OK` / `IN WORLD` harness markers are unchanged; `character:` is a new line.

### Character selection (ADR-31) — headless

There is **no character-select screen yet**: selection is automatic and logged. The pure rule
is `Scripts.Session.CharacterSelection.Decide` (first match wins):

1. `-cuvara-character <id-or-name>` / `CUVARA_CHARACTER`: a roster character by id, then by
   name (case-insensitive);
2. the same value spelling a valid name not on the roster: **create** it (so a harness passing
   `-cuvara-character Scout1` gets the same character on every run);
3. anything else requested: **fail** the session (no silent substitute);
4. empty roster: create `Hero_<first letters/digits of the user id>`;
5. the last character this account played on this device (`PlayerPrefs`
   `character.last_used.<user_id>`), if still on the roster;
6. the lowest slot.

Names follow the server's PLACEHOLDER rule (3-16 of `[A-Za-z0-9_]`). The roster RPCs are
`Scripts.Nakama.Characters.CharacterService` (`character_list` / `character_create` /
`character_delete`). The chosen id goes to two places that must agree, or the gateway answers
`character_mismatch`:

- `CharacterSelectionState.CharacterId` → `NakamaAuthProvider` sends
  `{"character_id":"…"}` in the `gateway_token` payload (the `cid` claim), for every token
  including a reconnect's;
- `NetworkClient.CharacterId` → `EnterWorldRequest.character_id`.

A Nakama without the roster RPCs (pre-ADR-31) logs a warning and plays the account's default
character (no `cid`) — unless a character was explicitly requested, which then fails.

## Prediction against the server's world (ADR-28)

On every new session (join, reconnect, transfer) `DotsWorldBridge.EnsureServerProtocolApplied`:

1. `LocalMovePredictor.UseServerProtocol(client.ServerProtocolVersion)` — 3D `CharacterMotor`
   for 3, planar for 2;
2. `SetMapGeometry(MapGeometrySource.LoadOrNull(client.CurrentMapId))`;
3. creates a `ProjectilePredictor` (protocol 3 only) at `client.TickRate` over the same geometry.

It logs one line: `[DotsWorldBridge] server protocol 3: 3D CharacterMotor prediction, map 'dev_arena' geometry Resources/Maps/dev_arena.json (…)`.

### Where the map geometry comes from

**A client-side copy: `Assets/Resources/Maps/<map_id>.json`.** The server's `/content`
document does not carry maps (`backend/content/README.md`, "Served at /content: no"); maps are
authored in the client's scenes and baked by the Editor exporter, which writes the server file
and this copy together. The copy is parsed by `Scripts.Gameplay.Maps.MapGeometryJson` (built on
the netcode package's reflection-free `JsonParser`, IL2CPP-safe) and validated with the shared
`MapGeometryValidation`. No file → `null` → the predictor's flat world, exactly what the server
runs for a map without a file. An invalid file is logged with every problem and also falls back
to flat.

Today only `dev_arena.json` exists (a verbatim copy of the server's example).

## Input (one owner: `DotsWorldBridge`)

| Key | Sends |
|---|---|
| WASD / arrows | `MoveX`/`MoveY`, as before |
| Space | `Jump = true` on that one input (edge-triggered) |
| Q or right mouse | the content's lowest-id `projectile` ability: `AbilityId`, `AimX/Y/Z`, `SpawnSeq` |

Every input carries `SetRenderTime(binder.RenderTick)` (lag compensation, ADR-29). An input is
sent only when something happened (move, jump or cast), as before. The aim is the pointer ray
(`Pointer.current`: mouse, pen or touch) intersected with the horizontal plane at the player's
own height; with no pointer it is 10 units along wire +y.

The projectile is predicted from **feet + 1.0** toward **aim + 1.0** (`AimPoint`), the server's
own launch rule. `ProjectilePredictor.Fire` returns the `spawn_seq`; 0 means "refused" and the
cast is not sent at all.

## Presentation

- **Axes**: wire `(x, y, z)` is Unity `(x, z, y)` — `Scripts.Gameplay.WireAxes`, everywhere.
- **Height on DOTS mirrors**: `com.cuvara.dots` 0.29.0 maps only the ground plane, so the client
  adds `EntityElevationSystem` (Presentation, after `ViewInterpolationGroup`, before the view
  lifecycle). It sets Unity `y` absolutely from a table `EntityElevationFeeder` fills each frame:
  the local player's predicted height (`Position3.Z`), and for everyone else snapshot `z`
  interpolated at the binder's render tick (`HeightTrack`).
- **Projectiles** (archetype `projectile`) are drawn at last position + velocity × time since
  that snapshot, capped at 0.25 s (`ProjectileExtrapolation`), so they fly at the present like
  the owner's prediction rather than an interpolation delay behind.
- **Own projectiles** are pooled spheres (`PredictedProjectileViews`) until the snapshot carries
  an entity with the same `spawn_seq` (sent only to the owner): the feeder calls
  `ProjectilePredictor.TryHandOver`, the sphere goes back to the pool, and the authoritative
  entity's view takes over. An unclaimed prediction is dropped after 1 s.
- **Items** (archetype `item`) are placeholder cubes. Both new archetypes are in
  `DotsViewArchetypes.All` / `ServerKindMappings`, the primitive provider, the bridge's
  placeholder library, and `Resources/DotsViews/DotsViewLibrary.asset` (re-authored by
  `Cuvara/DOTS/Create Placeholder View Library`, so `DotsViewLibraryBuildCheck` passes).

## Content, stats, statuses, inventory (ADR-30)

`GameplayPanelDriver` (entry point of `MainSceneScope`):

- **Content** is downloaded once per process by `GameContentService` from
  `-cuvara-content-url` / `CUVARA_CONTENT_URL`, else the origin of `-cuvara-status-url`
  (`/status` and `/content` share the game server's metrics port). Not fatal: without it the HUD
  shows `—` and the cast key logs why it does nothing. No bundled fallback
  (`docs/CONTENT-PIPELINE.md`: content is never shipped).
- **Stat block**: `PlayerStatsSnapshot` resolves the ids of the stats keyed `"level"` and
  `"mana"` through the content (`GameContentCatalog.TryGetStatId`) — never hard-coded numbers —
  and names active statuses by their content key with stacks and remaining seconds.
- **Inventory**: `InventoryService` over `NetworkClient.SendCommandAsync` /
  `ServerPushReceived`, payloads encoded with `Shared.GameLogic.Gameplay` (opcodes 1-5, push
  100). Opcode 1 on every new session; the server's complete view replaces the client's on
  every answer or push; nothing is edited optimistically. State is set under a lock and
  announced by `Pump()` on the main thread.
- **Panel** (UI Toolkit, MVP): `Assets/Scripts/UI/Gameplay/` — `GameplayPanelView` (+ enrolled
  `Resources/GameplayPanelView.uxml` and its generated bindings), `GameplayPanelViewModel`,
  `GameplayPanelPresenter`. Hosted at runtime on a `UIDocument` the driver creates with
  `Resources/GameplayPanelSettings.asset` (authored by `Cuvara/UI/Create Gameplay Panel
  Settings`), because MainScene has no UIDocument and scenes are Editor-owned. Without the
  layout or panel settings it runs headless and logs inventory changes.
  Keys: **I** toggles the inventory, **E** picks up the nearest `item` entity within 6 units
  (the server judges reach). Rows offer Equip (wearables, in their content slot), Unequip
  (equipped) and Use (everything else in the bag).

## Editor map exporter

`Cuvara/Maps/Export Open Scene To Map JSON...` (`Assets/BuildScripts/Editor/MapGeometryExporter.cs`),
headless `-executeMethod MapGeometryExporter.ExportFromCommandLine -mapScene <scene> [-mapId id]
[-mapExportPath path]`. Unity `(x, y, z)` → wire `(x, z, y)`.

| Scene object | Becomes |
|---|---|
| enabled, non-trigger `BoxCollider` with identity world rotation | `boxes[]` (world AABB). Rotated → **skipped with a warning** |
| first active `Terrain` | `heightfield` (square cell, ≤ 1025 × 1025 samples) |
| `spawn_<name>` or tag `Respawn` | `spawns[]` (`spawn_default` = where players appear) |
| `portal_<name>@<targetMap>/<targetSpawn>` | `portals[]`: radius = ½ max(scale x, z), height = 2 × scale y, base at the cylinder bottom |
| `map_bounds` with a `BoxCollider` | `bounds` (else the extent of everything above, with a warning) |

The result is validated with `MapGeometryValidation` before anything is written; it is written
to the chosen path (default `../rpg-mmo-server/backend/content/maps/<map>.json`) **and** to
`Assets/Resources/Maps/<map>.json`.

## Known gaps

- `com.cuvara.dots` `LocalPredictionSystem` reconciles with the `Vec2` overload, so on the DOTS
  path the local player's **height is predicted but not reconciled** against snapshot `z` (the
  motor and geometry are the server's, so they agree unless the server corrects). Fix belongs
  in the package (call `Reconcile(Vec3, velZ, ack, tick)` under the motor) — then the height
  path here can read it unchanged.
- `EntityElevationSystem` exists only because the DOTS adapter carries no `z`; remove it when it does.
- `ContentJsonReader` (netcode 0.46.0) reads items only; stats/statuses/abilities are read by
  `GameContentCatalog` until the package reads them into `ContentDatabase`.
- No character-select screen (create/delete/choose by hand) — headless selection only.
