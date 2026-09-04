# Device benchmark

How to measure client performance on real hardware. Built 2026-09-04 because the project
had **zero** client performance numbers that were not an Editor guess. The instrument is a
self-contained scene + recorder that runs unattended on a device, writes one JSON result,
and exits — no backend, no server, no human.

## What it is

| Piece | Where | What it does |
|---|---|---|
| `BenchmarkRecorder` | `Assets/Scripts/Benchmark/` (`NDC.Scripts.Benchmark`) | Per-frame CPU frame time, GC bytes/allocs per frame, GC collections, per-second memory + entity count; aggregates; writes/logs JSON; quits. Works in **any** scene — no netcode, no workload required. |
| `DeviceBenchmark.unity` | `Assets/Scenes/` | The workload scene: recorder + entity ramp + HUD, everything code-driven at load. **Not** in the enabled Build Settings list — it never ships; `-bootScene` adds it per build. |
| `BenchmarkWorkload` + `BenchmarkLifetimeScope` | `Assets/Scripts/Benchmark/Workload/` | The game's own `RegisterDots()` container; entities spawned HybridViews-sample-style (`LocalTransform` + `LocalToWorld` + `EntityViewRequest`), moved by the dots package's Burst `MoveBounceSystem`/`SpinSystem`, rendered as pooled primitives. Half "mob", half "player-remote". Deterministic seed. |
| `BenchmarkHudDriver` | same | The real `HudView` over the committed UXML, bound to a synthetic `HudViewModel` rewritten once per second — the #71 binding path under load. |
| `DeviceBenchmarkConfig.asset` | `Assets/Scripts/Benchmark/` | Warm-up 10s, settle 2s, ramp **250 → 500 → 1000** entities × 30s, auto-quit on. |

Total run: 10 + 3×30 = **100 seconds**.

## Configuring a run

Two surfaces, deliberately:

- **`BenchmarkConfig` asset** (`DeviceBenchmarkConfig.asset`, or create another via
  `Assets > Create > Benchmark > Benchmark Config` and point the scene's recorder at it).
  This is the only surface that works on **Android** — extras on `am start` never reach
  `Environment.GetCommandLineArgs()`, so what is baked into the scene is the run.
- **Command line** (desktop players and the Editor), overriding the asset:
  `-benchWarmup 15`, `-benchSettle 3`, `-benchPhases 250:30,500:30,1000:30`,
  `-benchNoQuit`. Malformed values fall back to the asset; a partially malformed
  `-benchPhases` falls back **whole** (a half-parsed ramp would measure the wrong workload).

## Building the Android APK

From WSL, Windows paths, per the CLAUDE.md build notes (`-buildOutput` because WSL env vars
do not cross into `Unity.exe`):

```bash
"/mnt/c/Program Files/Unity/Hub/Editor/6000.3.9f1/Editor/Unity.exe" \
  -quit -batchmode -nographics \
  -projectPath 'E:\SecretProject\IndieRPGMMOAdventure' \
  -buildTarget Android \
  -executeMethod PlayerBuilder.Build \
  -development \
  -buildOutput 'E:\SecretProject\IndieRPGMMOAdventure\Builds\DeviceBenchmark' \
  -bootScene 'Assets/Scenes/DeviceBenchmark.unity' \
  -logFile 'E:\SecretProject\IndieRPGMMOAdventure\Builds\devicebenchmark-build.log'
```

The APK lands in `Builds/DeviceBenchmark/Android/IndieRPGMMOAdventure.apk`.

Two flags are new and both matter:

- **`-development`** → `BuildOptions.Development`. The frame-time and GC profiler counters
  the recorder reads are only guaranteed in development players; in a release player the
  recorder degrades to `Time.unscaledDeltaTime` and zeros rather than failing, but that is
  a worse instrument. (It also means the numbers carry development-build overhead — see
  caveats.)
- **`-bootScene` with a scene that is NOT in Build Settings**: `PlayerBuilder` now prepends
  a boot scene that exists on disk but is not in the enabled set, for that build only —
  which is what keeps `DeviceBenchmark.unity` out of every release build while still being
  buildable. A path matching neither the enabled set nor a file on disk is still a hard
  error.

`ANDROID_KEYSTORE` etc. are irrelevant here — a development APK debug-signs itself.

## Running on a device

```bash
adb install -r Builds/DeviceBenchmark/Android/IndieRPGMMOAdventure.apk

# Capture the result from logcat while it runs (the run is ~100 s + startup):
adb logcat -c
adb shell am start -n com.UnityTechnologies.com.unity.template.urpblank/com.unity3d.player.UnityPlayerActivity
adb logcat -s Unity | grep --line-buffered -m1 "BENCH-RESULT" > result-line.txt
```

The player quits itself when the run completes (`AutoQuit`). The same JSON is also on the
device; the log line right before `[BENCH-RESULT]` is `[BENCH-FILE] <path>`, and the pull
is:

```bash
adb shell "ls /storage/emulated/0/Android/data/com.UnityTechnologies.com.unity.template.urpblank/files/benchmark-*.json"
adb pull /storage/emulated/0/Android/data/com.UnityTechnologies.com.unity.template.urpblank/files/<file>.json
```

Strip the logcat prefix from `result-line.txt` (everything up to and including
`[BENCH-RESULT] `) and what remains is the identical JSON.

## Reading the JSON

One object; the aggregates are the product, the memory series is the leak detector.

- **Environment**: `Scene`, `DeviceModel`, `OperatingSystem`, `GraphicsDevice`,
  `SystemMemoryMb`, `UnityVersion`, `StartedAtUtc`, `DevelopmentBuild` — a result file
  answers "which phone, which build" by itself. `DevelopmentBuild` should read `true`;
  if it does not, the counters were degraded (see `-development` above).
- **`Overall`** — every post-warm-up frame, phase-boundary spawn bursts included: the
  run's true totals.
- **`Phases[]`** — one entry per ramp step (`Label`, `EntityCount`, `StartTimeSeconds`),
  each with its own aggregates that additionally exclude the first `SettleSeconds` after
  the boundary, so a step's spawn burst does not pollute its steady state. **The
  comparison that matters is across phases**: frame-ms percentiles at 250 vs 500 vs 1000
  entities is the scaling curve this instrument exists to draw.
- **Aggregates**, per set: `MeanMs`/`MedianMs`/`P95Ms`/`P99Ms`/`MaxMs` (main-thread CPU
  frame ms — p99 is the stutter number, median the throughput number), `AverageFps`
  (frames over wall-clock span), `GpuMeanMs` (0 unless Frame Timing Stats is enabled —
  see caveats), `GcAllocatedTotalBytes`, `GcAllocatedPerFrameMedianBytes` (**the
  steady-state allocation figure; the target is 0**), `GcAllocationCountTotal`,
  `GcCollections`, `GcSpikeFrames` (frames in which a collection ran — each one is a
  potential visible hitch).
- **`Memory[]`** — per second: `TotalReservedBytes`, `SystemUsedBytes`, `GcUsedBytes`,
  `GcReservedBytes`, `EntityCount`. Reserved memory climbing across a constant-entity
  phase is a leak; `EntityCount` confirms the ramp actually happened (expect roughly
  250/500/1000 plus a handful of system entities).
- **`Truncated`** — `true` means the device outran the preallocated sample buffer
  (`MaxExpectedFps`, default 240) and aggregates cover only the captured prefix. Raise
  `MaxExpectedFps` and rerun.

The recorder itself allocates nothing per frame in steady state (preallocated struct
buffers — it measures GC, it must not feed it), so `GcAllocatedPerFrameMedianBytes` is the
workload's number, not the instrument's. What it cannot subtract is what development-build
instrumentation and the HUD's once-per-second caption strings cost — both are constants of
the harness, and both are documented right here so nobody chases them as regressions.

## Caveats — read before trusting a number

| Caveat | Consequence |
|---|---|
| **Development build** | Profiler counters cost a little CPU and the build skips optimizations a release APK gets. Numbers are comparable **run-to-run**, not to a release build. Treat them as relative (250 vs 1000 entities), not absolute. |
| **Minimal stripping ≠ High** | Android ships Minimal managed stripping (see CLAUDE.md's backend table). This benchmark validates performance, **not** aggressive-stripping correctness — a green run here says nothing about `link.xml` under High. |
| **Thermal throttling** | A warm phone clocks down mid-run and the last phase pays for the first. Airplane mode, fixed screen brightness (auto-brightness responds to heat), a **2-minute cooldown between runs**, and never benchmark while charging (charging heats the SoC). Repeat runs that disagree by more than ~10% at p95 are a throttling symptom, not noise. |
| **GPU time is 0 by default** | `enableFrameTimingStats` is off project-wide; the recorder guards it. To measure GPU frame time, enable Frame Timing Stats in Player Settings for the benchmark build (and expect its own small overhead). CPU numbers are unaffected either way. |
| **First run after install** | Shader warm-up and OS install bookkeeping land in the first run. The 10 s warm-up window absorbs most of it; discard the first run after a fresh install anyway. |
| **Screen off / backgrounded** | Android pauses the player; the run must stay foreground with the screen on (`adb shell svc power stayon true` for a rig, and turn it back off after). |

## Tests

- EditMode (`Assets/Tests/Editor`): `BenchmarkAggregationTests` — percentile
  interpolation, warm-up exclusion, phase attribution, settle-window rules, GC
  spike/collection counting, buffer-count clamping, fencepost FPS;
  `BenchmarkArgsTests` — override parsing and the all-or-nothing ramp rule.
- PlayMode (`Assets/Tests/Runtime`): `BenchmarkRecorderPlayModeTests` — the recorder over
  a real player loop samples frames, honors config, and its JSON round-trips.

The aggregation math is pure C# with no Unity dependency, verified additionally against a
plain `dotnet` compile of the same sources during development.

## Verified counter names

`ProfilerRecorder` counter names fail silently (an invalid recorder, not an exception), so
every name the recorder uses was verified present as a string in this editor version's own
player binary (`Editor/Data/PlaybackEngines/windowsstandalonesupport/.../UnityPlayer.dll`,
6000.3.9f1): `CPU Main Thread Frame Time` (with `Main Thread` as an in-recorder fallback,
also verified), `GC Allocated In Frame`,
`GC Allocation In Frame Count`, `System Used Memory`, `Total Reserved Memory`,
`GC Used Memory`, `GC Reserved Memory`. A `GC Collect Count` counter does **not** exist in
this version (it was considered and rejected); collection counts come from
`System.GC.CollectionCount` instead, which needs no profiler at all.
