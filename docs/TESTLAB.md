# Testlab hooks in the client

The rpg-factory testlab (`rpg-factory/testlab/README.md`) drives real, rendered players of this game through scripted scenarios: no keyboard, no mouse, no window focus. It grades each run from what the clients rendered and what the server reported. This page describes the client side of that contract.

## Input seam (production code)

`Assets/Scripts/DI/GameplayInput.cs`:

- **`IGameplayInputSource`** has these members:
  - `ReadMove`
  - `JumpPressed` / `CastPressed` / `PickupPressed` / `InventoryTogglePressed`
  - `TryPointerPosition`
  - `TryAimPoint`, an explicit wire-space aim for a cast.
- **`GameplayInput.Source`** is the active source:
  - It is `DeviceGameplayInputSource` (keyboard, mouse, pointer) unless something replaces it.
  - It resets to devices on every play (`SubsystemRegistration`).
- **Readers:** `DotsWorldBridge.Update` (move, jump, cast, aim) and `GameplayPanelDriver` (pickup, inventory) read through it.

The send schedule, `SendInput`, `LocalMovePredictor.RecordInput`, clock steering and projectile prediction are unchanged. One component still owns input (`docs/DOTS-WIRING.md`); only where the values come from is pluggable.

## Test runtime (`Assets/TestLab/`, assembly `NDC.TestLab`)

**When it compiles.**
- `defineConstraints: CUVARA_TESTLAB || UNITY_INCLUDE_TESTS`.
- In the Editor it compiles, so EditMode tests can reach it.
- In a player it compiles only when built with `PlayerBuilder -extraDefines CUVARA_TESTLAB`.
- A normal build contains no `NDC.TestLab.dll`.

**When it runs.**
- `TestLabBootstrap` (`AfterSceneLoad`) does nothing unless the player was started with `-testlab-plan <plan.json> -testlab-out <dir> [-testlab-control <control.json>]`.

**Components.**
- `TestLabRunner`:
  - installs `ScriptedInputSource`;
  - waits for the shared start instant in `control.json`;
  - plans the controls each frame with `TestLabInputPlanner` (pure, EditMode-tested);
  - records the run. Recorded data:
    - snapshot arrivals;
    - received and rendered positions of every replicated entity at the plan's `sample_hz`;
    - per-frame render discontinuities (`RenderJumpDetector`);
    - frame times;
    - prediction counters;
    - screenshots (`ScreenCapture`, no focus needed).
- It never writes a transform and never calls `SendInput`.

## Delivery instrumentation (contract C2)

Schema `rpg.testlab.delivery.v1`, named in the `configured` event (`delivery_schema`) and in `summary.json` (`delivery.schema`). Two CSV files in the `-testlab-out` directory, each with a header row, comma-separated, invariant culture, `\n` line ends. **An empty field means "not known"**, never zero.

**Clock.** Every `*_us` column is microseconds since player start, read from `System.Diagnostics.Stopwatch` (monotonic) and anchored once to `Time.realtimeSinceStartupAsDouble` when the runner starts (`LabClock`). So `us / 1e6` is on the same axis as the `t` column of `events.ndjson`, `snapshots.ndjson`, `samples.ndjson` and `frames.csv`. Never wall-clock.

### `delivery.csv`: one row per received snapshot

| Column | Meaning |
|---|---|
| `world_tick` | Server tick of the snapshot (`ResolvedSnapshot.Tick`). |
| `recv_us` | When its frame body left the KCP transport (`ITransport.ReadFrameAsync` returned). Empty when the tap is not active. |
| `applied_us` | When the snapshot was merged into the client's world state (`NetworkClient.SnapshotReceived`, which fires after `WorldState.Apply`). The ECS drain (`NetworkViewCommandSystem`) runs later in the same or the next frame. |
| `bytes` | Length of that frame body: after KCP reassembly and the transport key's AES-CFB, before the sealed session is opened and before decoding; without the 4-byte length prefix and without KCP/UDP headers. Empty when the tap is not active. |
| `full` | `1` keyframe, `0` delta. |
| `entities` | Entities in the snapshot after resolve. |
| `frame` | The `render.csv` / `frames.csv` frame that can first show it. |

`applied_us - recv_us` is decode + resolve + merge only: netcode processes a frame synchronously after reading it. The receive time is bounded by the frame rate, because the KCP pump runs on the PlayerLoop (`UniTask.Delay(1, ..., PlayerLoopTiming.Update)`).

### `render.csv`: one row per rendered frame

Written from `RenderPipelineManager.beginContextRendering`, once per frame, after `PresentationSystemGroup` has advanced the interpolation clock. So the row describes the frame actually drawn. A player without rendering (`-nographics`) writes no rows.

| Column | Meaning |
|---|---|
| `frame` | Frame index. It is the same index as `frames.csv`. |
| `t_us` | When the frame's rendering started. |
| `render_tick` | `InterpolationTimeline.Clock.RenderTick`: the fractional server tick remote entities are drawn at. Empty until the clock has a sample. |
| `clock_newest_tick` | `InterpolationTimeline.Clock.NewestTick`: the newest tick drained into ECS. Empty with `render_tick`. |
| `newest_tick` | Newest `world_tick` received in this session (the maximum, so a late older tick never lowers it). |
| `newest_recv_us` | `recv_us` of that snapshot, or its `applied_us` when the tap is not active. |
| `snapshot_age_ms` | `(t_us - newest_recv_us) / 1000`: the age of the newest snapshot when this frame was drawn. |

`newest_tick` and `newest_recv_us` restart when the network client leaves `InWorld` (a reconnect is a new tick sequence). Interpolation delay in ticks is `newest_tick - render_tick`.

### The transport tap, and when it is absent

`FrameReadTap` replaces the `ITransportFactory` that `NetworkClient` holds (a private field, found by type through reflection) with a factory that wraps the KCP transport. This is test-only code and no production assembly changes. The `delivery_tap` event and `summary.json` `delivery.tap` say what happened:

- `installed`: every gameplay session of the run reads through the tap.
- `late`: a session already existed when the runner found the client. That session's rows have empty `recv_us` and `bytes`; the next join (a reconnect) is tapped.
- `unavailable`: the field was not found (a netcode change). `reason` says why. All rows have empty `recv_us` and `bytes`.

One side effect: `GameSessionClient.DescribeJoinTimeout` recognises a `KcpTransport` by type. Through the wrapper, a join timeout uses its generic message, not "no UDP datagram came back".

### Cost

Each snapshot writes one row and each frame writes one row. Nothing is retained per snapshot or per frame, and no call does work proportional to the run's length. Files are flushed once a second with the other outputs. At 60 fps and 20 snapshots/s a one-hour soak is about 216k render rows and 72k delivery rows.

### Disconnect and reconnect

The runner has no way to drop and restore a connection inside a running process. `control.json` carries only `start_at_unix_ms`, `client_user_ids` and `abort`, and `abort` ends the run. Netcode's own reconnect path (`ReconnectPolicy`) runs only when the server or the network closes the session. A reconnect test today has to break the link from outside (for example, kill the process, as S08 does, or pause the game-server container).

## Test scene

`Assets/Scenes/Tests/MultiplayerCombatTest.unity` is generated by `TestLab.Editor.TestLabSceneBuilder.Build`:
- It is a copy of `MainScene`, plus a script-free `TestLabArena`.
- The arena has a 5-unit grid and `ServerCollisionBoxes[<map>]`, a mirror of a map file's boxes, shown only when that map runs.

`TestLabSceneBuilder.Validate` checks that the scene opens, has no missing scripts, and still has `MainSceneScope` and `DotsWorldBridge`. The scene is not in `EditorBuildSettings`; builds reach it through `-bootScene`. Regenerate it after any MainScene change (`Tools > TestLab > Rebuild MultiplayerCombatTest scene`, or `testlab.py build-client`).

## Build

```
Unity -batchmode -quit -projectPath . -buildTarget Win64 -executeMethod PlayerBuilder.Build \
  -buildOutput <dir> -bootScene Assets/Scenes/Tests/MultiplayerCombatTest.unity -extraDefines CUVARA_TESTLAB
```

`-extraDefines` applies to this build only (`BuildPlayerOptions.extraScriptingDefines`); nothing is written to ProjectSettings.
