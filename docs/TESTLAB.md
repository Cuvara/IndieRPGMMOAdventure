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

Schema `rpg.testlab.delivery.v2` (v1 until the T14 change below), named in the `configured` event (`delivery_schema`) and in `summary.json` (`delivery.schema`). Two CSV files in the `-testlab-out` directory, each with a header row, comma-separated, invariant culture, `\n` line ends. **An empty field means "not known"**, never zero.

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

Written from `RenderPipelineManager.beginContextRendering`, once per frame. A player without rendering (`-nographics`) writes no rows.

**Which render clock.** The shipping client draws remote entities through netcode's managed `WorldViewBinder`. `DotsWorldBridge` feeds `DotsEntityView` untimed `SetState` commands (tick 0), so the drain never calls `InterpolationClock.NoteSnapshot`. The ECS `InterpolationTimeline` clock therefore never gets a sample in this client, and in v1 `render_tick` was always empty. v2 reads the binder (`BinderProbe`, reflection on `DotsWorldBridge`'s private binder field) and names the source in `clock_source`:

- `binder`: `WorldViewBinder.RenderTick` and `LastServerTick`. The binder ticks in `DotsWorldBridge.Update`, and `NetworkViewCommandSystem` drains its states in `InitializationSystemGroup` at the start of the **next** frame. So the value in frame N's row is the one captured in frame N-1's `LateUpdate`, which is the clock the positions drawn in frame N were evaluated at.
- `ecs`: `InterpolationTimeline.Clock` (`RenderTick`, `NewestTick`), read at this frame's render callback, after `PresentationSystemGroup` advanced it. Used only when no binder is readable (a timed-path `SetStateAtTick` consumer).
- empty: neither clock is readable yet.

| Column | Meaning |
|---|---|
| `frame` | Frame index. It is the same index as `frames.csv`. |
| `t_us` | When the frame's rendering started. |
| `render_tick` | The fractional server tick remote entities in this frame are drawn at (see `clock_source`). Empty until a clock is readable. |
| `clock_newest_tick` | The newest server tick that clock has consumed: `WorldViewBinder.LastServerTick` (`binder`) or `InterpolationTimeline.Clock.NewestTick` (`ecs`). Empty with `render_tick`. |
| `newest_tick` | Newest `world_tick` received in this session (the maximum, so a late older tick never lowers it). |
| `newest_recv_us` | `recv_us` of that snapshot, or its `applied_us` when the tap is not active. |
| `snapshot_age_ms` | `(t_us - newest_recv_us) / 1000`: the age of the newest snapshot when this frame was drawn. |
| `clock_source` | v2: `binder` or `ecs`, or empty with `render_tick`. Appended last, so a parser that reads by header name reads v1 and v2 alike. |

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

`control.json` still carries only `start_at_unix_ms`, `client_user_ids` and `abort`. A plan can now leave or drop the link from inside the process: see the `quit` and `reconnect` actions below.

## Coverage hooks (T14)

All test-only (`NDC.TestLab`), all inert unless a plan lists the action. Field names are exact.

### C1: predictor tick rate

- `samples.ndjson`: the `pred` object gains `tick_hz`, which is `LocalMovePredictor.TickRateHz`. That is the rate the predictor steps at, as read at that sample.
- `summary.json`: `prediction.tick_hz`, read at the end of the run.

### C2: stall (`{"kind":"stall","t0":T,"ms":N}`)

- **When it fires:** once, at the first frame with scenario `t >= t0`, inside the runner's `Update`.
- **What it does:** blocks the main thread with `Thread.Sleep(N)`.
- **Event:** `events.ndjson` gets `stall` with:
  - `ms`: the requested duration.
  - `start`, `end`: seconds on the realtime axis.
  - `start_us`, `end_us`: `LabClock` microseconds.
  - `actual_ms`: the measured duration.
- **Summary:** the count is in `summary.json` `actions.stalls`.
- **Fields:** `t1` is ignored.

### C3: entity lifecycle (`events.ndjson`)

- **`entity_added`:** `id`, `type`, `tick` (the snapshot tick it first appeared in), `x`, `y` (that snapshot's wire position), `rx`, `ry` (wire-space position at its first rendered frame) and `frame`.
  - **When:** it is written at the first rendered frame, not at arrival.
  - **Never rendered:** an entity removed before it was ever drawn still gets one, right before its `entity_removed`. In that case `rx`, `ry` and `frame` are null.
- **`entity_removed`:** `id`, `type`, `tick`, `added_tick`, `rendered` (bool) and `reason`.
  - `removed`: the id was in the snapshot's removed list.
  - `keyframe`: a live id was absent from a full snapshot.
- **`entities_reset`:** `count`. Written when the client leaves `InWorld` (reconnect or leave). It forgets the session's ids without one event per entity.
- **Not covered:** a live entity that was never rendered by the end of the run has no `entity_added`.
- **Summary:** `summary.json` `entities`: `added`, `removed`, `live`.
- **Cost:** memory holds one entry per live entity. A keyframe costs one pass over its entities.

### C4: per-sample render tick

Each `samples.ndjson` row gains two fields:
- `render_tick`: the clock the rendered positions in that row were drawn at. It is the same value and the same rule as `render.csv`.
- `render_clock`: `binder`, `ecs` or null.

Compare two clients at the same server tick with `render_tick`, not `world_tick`. `world_tick` is the newest snapshot received.

### C5: negative-path casts

- **`{"kind":"cast_at_point","t0":..,"t1":..,"interval_s":..,"x":X,"y":Y}`:**
  - Plans a cast aimed at wire-space (X, Y).
  - No range check: an out-of-range point is the point.
- **`{"kind":"cast_spam","t0":..,"t1":..,"interval_ms":M,"range":R}`:**
  - Plans a cast every M ms (floor 1 ms, at most one per frame).
  - It aims at the nearest enemy within R, else casts forward.
- **Both** go through the real path: `ScriptedInputSource` latches the press, and `DotsWorldBridge` consumes it and sends it with the next input (`TryAddProjectileCast`).
- **Logging in `inputs.ndjson`:**
  - Every planned cast: `kind: "cast"` with `action` (`cast_at_point` / `cast_spam`), `target` and `aim`.
  - Every press the game actually consumed: `kind: "cast_read"`.
- **Rate limit:** presses planned faster than the bridge reads them merge into one. Sends happen at the input send rate. So `casts_planned >= casts_read >= casts sent`. `summary.json` `input.casts_read` is the middle number.
- **Server side:** rejections are counted by the server (S2), not here.

### C6: leave and reconnect

- **`{"kind":"quit","t0":T}`: leave.**
  - **Sequence:** at T it writes `quit_requested`, calls `NetworkClient.Disconnect()` (the player's own way out: a polite leave, `ReconnectPolicy` never fires), then writes `disconnected`.
  - **Finishing:** it finishes the run with outcome `left`. That writes `summary.json` and `status.json` (`phase: "left"`), then quits the process if `quit_on_done`.
  - **Not a process kill.**
- **`{"kind":"reconnect","t0":T}`: drop the link.**
  - **Mechanism:** at T, `FrameReadTap.DropLink()` closes the current gameplay KCP transport and fails its pending read with a `TransportException`.
  - **What netcode does:** it sees a `TransportError` close, and `ReconnectPolicy` answers with its own automatic reconnect: back through the gateway with a fresh token, and `DotsWorldBridge.OnReconnected`.
  - **No production change:** the production recovery path runs unchanged, with no change to production code.
  - **Events:** `reconnect_requested` (`dropped`, `tap`, `state`, `generation`), then netcode's `reconnect_attempt` (`attempt`), `reconnected` (`generation`, `tapped_sessions`) or `reconnect_failed` (`error`).
  - **Requires** the tap on the current session (`delivery.tap` = `installed`). Otherwise `dropped` is false and nothing happens.
  - **Summary:** `summary.json` `actions`: `reconnects_requested`, `reconnect_events`, `tapped_sessions`.
- **Not done: a manual disconnect-then-`ConnectAsync`.** `DotsWorldBridge` resets its view, binder and predictor only on `NetworkClient.Reconnected`, which an explicit `ConnectAsync` does not raise. A manual rejoin would exercise a path the game never takes, and making it safe would need a production change.

## Frame statistics: fixed cost

`FrameStats` (overlay and `summary.json` `frames`) holds a fixed amount of memory and does a fixed amount of work per frame and per second, whatever the run length:

- **Whole-run percentiles** (`p50_ms`, `p95_ms`, `p99_ms`):
  - **Method:** nearest rank over 0.1 ms buckets up to 1000 ms, plus one overflow bucket. Each bucket reports its largest sample.
  - **Precision:** the reported value is a real frame time. It is never below the exact percentile and at most 0.1 ms above it. In the overflow bucket (frames of 1 s or more) it is the largest such frame.
  - **Summary:** `summary.json` `frames.percentile_bucket_ms` states the resolution.
  - **What did not change:** `mean_ms`, `max_ms`, `count` and `long_frames` stay exact.
  - **Before T14:** the percentiles were exact, from a list of every frame, sorted on demand.
- **Overlay p95:** exact over the last 600 frames (`FrameStats.WindowFrames`), recomputed once a second by an in-place selection in a preallocated buffer. It allocates nothing.
- **Why it changed:** the old code sorted every frame of the run once a second. That was a main-thread stall that grew to about 200 ms per second over a one-hour soak (testlab soak `20261022T112220Z-S09-eeoa`), and it contaminated the frame times it reported.
- **Tests:** `TestLabHooksTests` feeds 10^6 frames and asserts that retained memory is unchanged and that `Add`, `RecentPercentile` and `Percentile` allocate 0 bytes (with a control proving the allocation probe works).
- **Jump detector:** `RenderJumpDetector` keeps only the previous and current frame's positions. Before T14 it kept every id the run had ever seen.

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
