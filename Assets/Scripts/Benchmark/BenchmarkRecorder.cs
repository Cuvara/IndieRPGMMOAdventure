namespace Scripts.Benchmark
{
    using System;
    using System.Globalization;
    using System.IO;
    using Unity.Entities;
    using Unity.Profiling;
    using UnityEngine;

    /// <summary>
    /// Records per-frame performance data unattended, aggregates it, writes one JSON file to
    /// <c>Application.persistentDataPath</c>, logs the same JSON on one line prefixed
    /// <c>[BENCH-RESULT]</c> (so <c>adb logcat -s Unity</c> captures it without a file pull),
    /// and quits the player. Drop it in any scene; a workload is optional.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Counters</b> (verified present in this editor version's player binary — see
    /// <c>docs/DEVICE-BENCHMARK.md</c>): "CPU Main Thread Frame Time" (ns), "GC Allocated In
    /// Frame", "GC Allocation In Frame Count", "System Used Memory", "Total Reserved Memory",
    /// "GC Used Memory", "GC Reserved Memory". Each read is guarded by
    /// <see cref="ProfilerRecorder.Valid"/>; frame time falls back to
    /// <c>Time.unscaledDeltaTime</c> where the counter is unavailable (release builds strip
    /// some profiler counters — the runbook prescribes a development build). GC collection
    /// counts come from <see cref="GC.CollectionCount(int)"/>, which needs no profiler at
    /// all; GPU time comes from <see cref="FrameTimingManager"/> and stays 0 unless "Frame
    /// Timing Stats" is enabled in Player Settings (it is off project-wide).
    /// </para>
    /// <para>
    /// <b>No steady-state managed allocation.</b> The recorder measures GC, so it must not
    /// feed it: sample buffers are preallocated up front, every per-frame write is an
    /// unmanaged struct store, and nothing per-frame creates a string, boxes, or invokes a
    /// delegate. Allocation happens at three moments only — start, phase boundaries (the
    /// <see cref="PhaseStarted"/> event), and the final report — and the per-phase settle
    /// window keeps boundary frames out of phase aggregates anyway.
    /// </para>
    /// <para>
    /// <b>Timeline.</b> Warm-up, then the configured phases back to back; the recorder owns
    /// the clock and raises <see cref="PhaseStarted"/> (index 0 fires on the first recorded
    /// frame so a workload can spawn its initial load inside the warm-up window). When the
    /// last phase ends the report is finalized and, outside the Editor and with
    /// <see cref="BenchmarkConfig.AutoQuit"/> on, <see cref="Application.Quit()"/> runs.
    /// </para>
    /// <para>
    /// Configuration: the serialized <see cref="BenchmarkConfig"/> (falling back to that
    /// type's defaults), overridden by <c>-bench*</c> command-line flags where a command
    /// line exists (see <see cref="BenchmarkArgs"/>).
    /// </para>
    /// </remarks>
    public sealed class BenchmarkRecorder : MonoBehaviour
    {
        [Tooltip("Run configuration. Optional; defaults to BenchmarkConfig's own defaults. " +
                 "Never mutated — values are copied into the run plan at start.")]
        [SerializeField] private BenchmarkConfig config;

        // ---- run plan (resolved once in OnEnable)
        private BenchmarkPhase[] phases = Array.Empty<BenchmarkPhase>();
        private float warmupSeconds;
        private float settleSeconds;
        private bool autoQuit;
        private bool rolling;
        private string runLabel = string.Empty;
        private float totalSeconds;
        private bool hasExternalPlan;
        private BenchmarkPhase[] externalPhases;
        private float externalWarmupSeconds;
        private float externalSettleSeconds;
        private bool externalAutoQuit;
        private bool externalRolling;
        private string externalLabel;
        private int windowIndex;

        // ---- preallocated sampling state
        private FrameSample[] frameSamples;
        private MemorySample[] memorySamples;
        private FrameTiming[] frameTimings;
        private float[] phaseStartTimes;
        private int frameCount;
        private int memoryCount;
        private int currentPhase = -1;
        private float nextMemorySampleTime;
        private double startTime;
        private bool running;
        private bool truncated;
        private string sceneName;
        private string startedAtUtc;

        // ---- profiler recorders
        private ProfilerRecorder mainThreadTimeRecorder;
        private ProfilerRecorder mainThreadTimeFallbackRecorder;
        private ProfilerRecorder gcAllocatedRecorder;
        private ProfilerRecorder gcAllocationCountRecorder;
        private ProfilerRecorder systemUsedRecorder;
        private ProfilerRecorder totalReservedRecorder;
        private ProfilerRecorder gcUsedRecorder;
        private ProfilerRecorder gcReservedRecorder;

        /// <summary>
        /// Raised when a ramp phase begins — index 0 on the first recorded frame, later
        /// indices at their boundaries. Raised outside the sampling store, so subscriber
        /// allocations land in boundary frames the settle window already excludes.
        /// </summary>
        public event Action<int, BenchmarkPhase> PhaseStarted;

        /// <summary>The resolved ramp; workloads size their prewarm off this.</summary>
        public BenchmarkPhase[] Phases => this.phases;

        /// <summary>Entity target of the running phase; 0 before the first phase.</summary>
        public int CurrentPhaseEntityCount =>
            this.currentPhase >= 0 && this.currentPhase < this.phases.Length
                ? this.phases[this.currentPhase].EntityCount
                : 0;

        /// <summary>True once at least one window's report has been written.</summary>
        public bool RunCompleted { get; private set; }

        /// <summary>The most recent window's report JSON; null until <see cref="RunCompleted"/>.</summary>
        public string LastJson { get; private set; }

        /// <summary>Where the report file landed; null until <see cref="RunCompleted"/>.</summary>
        public string LastFilePath { get; private set; }

        /// <summary>Rolling windows completed so far (0 during the first window).</summary>
        public int WindowIndex => this.windowIndex;

        /// <summary>
        /// Programs the run before the component enables, bypassing the config asset and the
        /// scene-recorder flags — the seam <see cref="BenchmarkBootstrap"/> (any-scene
        /// <c>-bench</c> activation) and the PlayMode tests use. Call it on a disabled
        /// component (add the component to an inactive GameObject, configure, activate);
        /// calling it after <c>OnEnable</c> has run is an error because the buffers are
        /// already sized to the old plan.
        /// </summary>
        /// <param name="rollingWindows">
        /// When true the run never ends: each time the timeline completes, the report is
        /// written and logged as usual (labeled with a window index) and sampling restarts
        /// for the next window — the first window's warm-up is not repeated. For a recorder
        /// riding along in a live scene (the netcode sample) whose process must stay up.
        /// </param>
        public void ApplyPlan(
            BenchmarkPhase[] planPhases,
            float planWarmupSeconds,
            float planSettleSeconds,
            bool planAutoQuit,
            bool rollingWindows,
            string label)
        {
            if (this.running)
            {
                throw new InvalidOperationException("[BenchmarkRecorder] ApplyPlan must run before the component enables.");
            }

            this.hasExternalPlan = true;
            this.externalPhases = planPhases ?? Array.Empty<BenchmarkPhase>();
            this.externalWarmupSeconds = planWarmupSeconds;
            this.externalSettleSeconds = planSettleSeconds;
            this.externalAutoQuit = planAutoQuit;
            this.externalRolling = rollingWindows;
            this.externalLabel = label ?? string.Empty;
        }

        private void OnEnable()
        {
            var args = Environment.GetCommandLineArgs();
            var defaults = this.config != null ? this.config : ScriptableObject.CreateInstance<BenchmarkConfig>();

            if (this.hasExternalPlan)
            {
                this.warmupSeconds = this.externalWarmupSeconds;
                this.settleSeconds = this.externalSettleSeconds;
                this.phases = this.externalPhases;
                this.autoQuit = this.externalAutoQuit;
                this.rolling = this.externalRolling;
                this.runLabel = this.externalLabel;
            }
            else
            {
                this.warmupSeconds = BenchmarkArgs.ResolveFloat(args, BenchmarkArgs.WarmupFlag, defaults.WarmupSeconds);
                this.settleSeconds = BenchmarkArgs.ResolveFloat(args, BenchmarkArgs.SettleFlag, defaults.SettleSeconds);
                this.phases = BenchmarkArgs.ResolvePhases(args, defaults.Phases ?? Array.Empty<BenchmarkPhase>());
                this.autoQuit = defaults.AutoQuit && !BenchmarkArgs.HasFlag(args, BenchmarkArgs.NoQuitFlag);
                this.rolling = false;
                this.runLabel = string.Empty;
            }

            this.totalSeconds = this.warmupSeconds;
            foreach (var phase in this.phases)
            {
                this.totalSeconds += phase.Seconds;
            }

            var capacity = (int)(this.totalSeconds * Mathf.Max(30, defaults.MaxExpectedFps)) + 256;
            this.frameSamples = new FrameSample[capacity];
            this.memorySamples = new MemorySample[(int)this.totalSeconds + 8];
            this.frameTimings = new FrameTiming[1];
            this.phaseStartTimes = new float[this.phases.Length];
            this.frameCount = 0;
            this.memoryCount = 0;
            this.currentPhase = -1;
            this.nextMemorySampleTime = 0f;
            this.truncated = false;
            this.windowIndex = 0;

            if (this.config == null)
            {
                Destroy(defaults); // the throwaway defaults instance, not a project asset
            }

            // Both frame-time counters exist in this version's player binary; whichever
            // resolves wins in RecordFrame, and unscaledDeltaTime backstops them both.
            this.mainThreadTimeRecorder = ProfilerRecorder.StartNew(ProfilerCategory.Internal, "CPU Main Thread Frame Time");
            this.mainThreadTimeFallbackRecorder = ProfilerRecorder.StartNew(ProfilerCategory.Internal, "Main Thread");
            this.gcAllocatedRecorder = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "GC Allocated In Frame");
            this.gcAllocationCountRecorder = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "GC Allocation In Frame Count");
            this.systemUsedRecorder = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "System Used Memory");
            this.totalReservedRecorder = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "Total Reserved Memory");
            this.gcUsedRecorder = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "GC Used Memory");
            this.gcReservedRecorder = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "GC Reserved Memory");

            this.sceneName = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
            this.startedAtUtc = DateTime.UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);
            this.startTime = Time.realtimeSinceStartupAsDouble;
            this.running = true;

            Debug.Log($"[BenchmarkRecorder] recording: warmup={this.warmupSeconds}s, " +
                      $"settle={this.settleSeconds}s, phases={this.phases.Length}, " +
                      $"total={this.totalSeconds}s, bufferCapacity={capacity}, " +
                      $"autoQuit={this.autoQuit}, rolling={this.rolling}, label='{this.runLabel}'");
        }

        private void Update()
        {
            if (!this.running)
            {
                return;
            }

            var elapsed = (float)(Time.realtimeSinceStartupAsDouble - this.startTime);

            // Phase boundaries first, so the frame recorded below is attributed to the phase
            // it ran in. Index 0 fires immediately: the workload's initial spawn burst then
            // happens inside the warm-up window, which exists to swallow exactly that.
            while (this.currentPhase + 1 < this.phases.Length &&
                   (this.currentPhase < 0 || elapsed >= this.PhaseEndTime(this.currentPhase)))
            {
                this.currentPhase++;
                this.phaseStartTimes[this.currentPhase] = elapsed;
                this.PhaseStarted?.Invoke(this.currentPhase, this.phases[this.currentPhase]);
            }

            this.RecordFrame(elapsed);

            if (elapsed >= this.nextMemorySampleTime && this.memoryCount < this.memorySamples.Length)
            {
                this.RecordMemory(elapsed);
                this.nextMemorySampleTime += 1f;
            }

            if (elapsed >= this.totalSeconds)
            {
                this.Finish(elapsed);
            }
        }

        private void OnDisable()
        {
            this.mainThreadTimeRecorder.Dispose();
            this.mainThreadTimeFallbackRecorder.Dispose();
            this.gcAllocatedRecorder.Dispose();
            this.gcAllocationCountRecorder.Dispose();
            this.systemUsedRecorder.Dispose();
            this.totalReservedRecorder.Dispose();
            this.gcUsedRecorder.Dispose();
            this.gcReservedRecorder.Dispose();
            this.running = false;
        }

        /// <summary>
        /// Aggregates whatever has been captured so far into a report without ending the run —
        /// the test seam. The unattended path goes through <see cref="Finish"/> instead.
        /// </summary>
        public BenchmarkReport BuildReportNow()
        {
            var elapsed = (float)(Time.realtimeSinceStartupAsDouble - this.startTime);
            return this.BuildReport(elapsed);
        }

        // ---------------------------------------------------------------- sampling

        private void RecordFrame(float elapsed)
        {
            if (this.frameCount >= this.frameSamples.Length)
            {
                this.truncated = true;
                return;
            }

            // LastValue is the previous frame's completed measurement — for a per-frame
            // benchmark that one-frame skew is irrelevant and it is the value the counter
            // actually has finished computing.
            float frameMs;
            if (this.mainThreadTimeRecorder.Valid && this.mainThreadTimeRecorder.LastValue > 0)
            {
                frameMs = this.mainThreadTimeRecorder.LastValue * (1f / 1_000_000f);
            }
            else if (this.mainThreadTimeFallbackRecorder.Valid && this.mainThreadTimeFallbackRecorder.LastValue > 0)
            {
                frameMs = this.mainThreadTimeFallbackRecorder.LastValue * (1f / 1_000_000f);
            }
            else
            {
                frameMs = Time.unscaledDeltaTime * 1000f;
            }

            var gpuMs = 0f;
            FrameTimingManager.CaptureFrameTimings();
            if (FrameTimingManager.GetLatestTimings(1, this.frameTimings) > 0)
            {
                gpuMs = (float)this.frameTimings[0].gpuFrameTime;
            }

            ref var sample = ref this.frameSamples[this.frameCount++];
            sample.TimeSeconds = elapsed;
            sample.FrameMs = frameMs;
            sample.GpuMs = gpuMs;
            sample.GcAllocatedBytes = this.gcAllocatedRecorder.Valid ? this.gcAllocatedRecorder.LastValue : 0;
            sample.GcAllocationCount = this.gcAllocationCountRecorder.Valid ? (int)this.gcAllocationCountRecorder.LastValue : 0;
            sample.GcCollectionsCumulative = GC.CollectionCount(0) + GC.CollectionCount(1) + GC.CollectionCount(2);
            sample.PhaseIndex = elapsed < this.warmupSeconds ? -1 : this.currentPhase;
        }

        private void RecordMemory(float elapsed)
        {
            ref var sample = ref this.memorySamples[this.memoryCount++];
            sample.TimeSeconds = elapsed;
            sample.TotalReservedBytes = this.totalReservedRecorder.Valid ? this.totalReservedRecorder.LastValue : 0;
            sample.SystemUsedBytes = this.systemUsedRecorder.Valid ? this.systemUsedRecorder.LastValue : 0;
            sample.GcUsedBytes = this.gcUsedRecorder.Valid ? this.gcUsedRecorder.LastValue : 0;
            sample.GcReservedBytes = this.gcReservedRecorder.Valid ? this.gcReservedRecorder.LastValue : 0;

            // CalculateEntityCount walks chunk headers, no managed allocation — cheap enough
            // once per second, and the one number that ties frame cost to workload size.
            var world = World.DefaultGameObjectInjectionWorld;
            sample.EntityCount = world is { IsCreated: true }
                ? world.EntityManager.UniversalQuery.CalculateEntityCount()
                : -1;
        }

        // ---------------------------------------------------------------- reporting

        private float PhaseEndTime(int phaseIndex)
        {
            var end = this.warmupSeconds;
            for (var i = 0; i <= phaseIndex; i++)
            {
                end += this.phases[i].Seconds;
            }

            return end;
        }

        private BenchmarkReport BuildReport(float elapsed)
        {
            BenchmarkAggregation.Aggregate(
                this.frameSamples,
                this.frameCount,
                this.phases,
                this.phaseStartTimes,
                this.settleSeconds,
                out var overall,
                out var phaseResults);

            var memory = new MemorySample[this.memoryCount];
            Array.Copy(this.memorySamples, memory, this.memoryCount);

            return new BenchmarkReport
            {
                Label = this.runLabel,
                WindowIndex = this.windowIndex,
                Scene = this.sceneName,
                DeviceModel = SystemInfo.deviceModel,
                OperatingSystem = SystemInfo.operatingSystem,
                GraphicsDevice = SystemInfo.graphicsDeviceName,
                SystemMemoryMb = SystemInfo.systemMemorySize,
                UnityVersion = Application.unityVersion,
                StartedAtUtc = this.startedAtUtc,
                DevelopmentBuild = Debug.isDebugBuild,
                WarmupSeconds = this.warmupSeconds,
                SettleSeconds = this.settleSeconds,
                Truncated = this.truncated,
                TotalFrames = this.frameCount,
                TotalSeconds = elapsed,
                Overall = overall,
                Phases = phaseResults,
                Memory = memory,
            };
        }

        private static string SanitizeForFileName(string value)
        {
            var invalid = Path.GetInvalidFileNameChars();
            var chars = value.ToCharArray();
            for (var i = 0; i < chars.Length; i++)
            {
                if (Array.IndexOf(invalid, chars[i]) >= 0)
                {
                    chars[i] = '_';
                }
            }

            return new string(chars);
        }

        private void Finish(float elapsed)
        {
            this.running = false;

            var report = this.BuildReport(elapsed);
            this.LastJson = JsonUtility.ToJson(report, prettyPrint: false);

            var timestamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
            var windowSuffix = this.rolling ? $"-w{this.windowIndex}" : string.Empty;

            // The label goes into the filename when present: several instances on one
            // machine share persistentDataPath, and same scene + same second would
            // otherwise clobber each other's reports.
            var labelPart = string.IsNullOrEmpty(this.runLabel)
                ? string.Empty
                : $"-{SanitizeForFileName(this.runLabel)}";
            this.LastFilePath = Path.Combine(
                Application.persistentDataPath,
                $"benchmark-{this.sceneName}{labelPart}-{timestamp}{windowSuffix}.json");
            try
            {
                File.WriteAllText(this.LastFilePath, this.LastJson);
                Debug.Log($"[BENCH-FILE] {this.LastFilePath}");
            }
            catch (Exception exception)
            {
                // The log line below still carries the full result; a read-only data path
                // (some CI shells) must not turn a finished measurement into a failure.
                Debug.LogWarning($"[BenchmarkRecorder] could not write {this.LastFilePath}: {exception.Message}");
                this.LastFilePath = null;
            }

            // One line, machine-readable, greppable from `adb logcat -s Unity`.
            Debug.Log($"[BENCH-RESULT] {this.LastJson}");

            this.RunCompleted = true;

            if (this.autoQuit && !Application.isEditor)
            {
                Application.Quit();
                return;
            }

            if (this.rolling)
            {
                this.BeginNextWindow();
            }
        }

        /// <summary>
        /// Rolls into the next measurement window: same buffers (already sized for the
        /// longest window — the first, the only one carrying warm-up), fresh counters, no
        /// repeated warm-up, and the scene name re-read because a DontDestroyOnLoad
        /// recorder outlives scene loads.
        /// </summary>
        private void BeginNextWindow()
        {
            this.windowIndex++;
            this.warmupSeconds = 0f;
            this.totalSeconds = 0f;
            foreach (var phase in this.phases)
            {
                this.totalSeconds += phase.Seconds;
            }

            this.frameCount = 0;
            this.memoryCount = 0;
            this.currentPhase = -1;
            this.nextMemorySampleTime = 0f;
            this.truncated = false;
            Array.Clear(this.phaseStartTimes, 0, this.phaseStartTimes.Length);
            this.sceneName = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
            this.startTime = Time.realtimeSinceStartupAsDouble;
            this.running = true;
        }
    }
}
