namespace TestLab
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.IO;
    using System.Text;
    using Cuvara.DOTS.Netcode;
    using Cuvara.Netcode.Client;
    using Cuvara.Netcode.Prediction;
    using Cuvara.Netcode.Snapshot;
    using Scripts.DI;
    using Unity.Collections;
    using Unity.Entities;
    using Unity.Profiling;
    using Unity.Transforms;
    using UnityEngine;
    using VContainer.Unity;

    /// <summary>
    /// Drives one real, rendered client through a testlab plan and records what it saw. Created
    /// by <see cref="TestLabBootstrap"/> only when the player was started with
    /// <c>-testlab-plan</c>; compiled only into players built with <c>CUVARA_TESTLAB</c>.
    /// </summary>
    /// <remarks>
    /// <para><b>What it touches.</b> It installs a <see cref="ScriptedInputSource"/> as
    /// <see cref="GameplayInput.Source"/> and otherwise only reads: the network client's merged
    /// world (what this client received), the predictor's counters, and the rendered
    /// <c>LocalTransform</c> of every replicated entity. It never writes a transform or calls
    /// <c>SendInput</c>; the game sends what the scripted source returns.</para>
    ///
    /// <para><b>Execution order.</b> Runs before <c>DotsWorldBridge.Update</c> (order -1000) so
    /// the controls planned for a frame are the ones the bridge samples in that frame, and records
    /// rendered positions in <c>LateUpdate</c>, after the presentation systems have written them
    /// for this frame.</para>
    ///
    /// <para><b>Output</b> (the directory given by <c>-testlab-out</c>): <c>status.json</c>
    /// (phase, user id; rewritten on every change), <c>events.ndjson</c>, <c>inputs.ndjson</c>,
    /// <c>snapshots.ndjson</c>, <c>samples.ndjson</c>, <c>frames.csv</c>, <c>summary.json</c>
    /// and <c>shots/*.png</c>. Times are seconds since process start
    /// (<c>Time.realtimeSinceStartupAsDouble</c>, monotonic) plus the scenario clock where one
    /// exists; never wall-clock deltas (this host's clock runs fast, rpg-mmo-server#153).</para>
    /// </remarks>
    [DefaultExecutionOrder(-1000)]
    public sealed class TestLabRunner : MonoBehaviour
    {
        private const string MobType = "mob";

        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        private readonly ScriptedInputSource source = new ScriptedInputSource();
        private readonly FrameStats frames = new FrameStats();
        private readonly List<PlannerEntity> plannerEntities = new List<PlannerEntity>(256);
        private readonly List<Vector2> clientPositions = new List<Vector2>(8);
        private readonly StringBuilder line = new StringBuilder(4096);

        private TestLabPlan plan;
        private TestLabInputPlanner planner;
        private RenderJumpDetector jumps;
        private string outDir;
        private string controlPath;

        private NetworkClient client;
        private LocalMovePredictor predictor;
        private EntityQuery renderedQuery;
        private bool queryCreated;

        private StreamWriter events;
        private StreamWriter inputs;
        private StreamWriter snapshots;
        private StreamWriter samples;
        private StreamWriter frameCsv;

        private string phase = "booting";
        private string error;
        private double inWorldAt = double.NaN;
        private double startRealtime = double.NaN;
        private double nextControlPoll;
        private double nextSampleAt;
        private double nextFlush;
        private int nextScreenshot;
        private int anomalyShots;
        private double lastAnomalyShot = double.NegativeInfinity;
        private int frameIndex;
        private TestLabControl control;
        private bool finished;

        private long snapshotsReceived;
        private long keyframes;
        private double lastSnapshotAt = double.NaN;
        private double maxSnapshotGapS;
        private long castsPlanned;
        private long jumpsPlanned;
        private long movesRead;
        private float maxPredictionCorrection;
        private int stateChanges;

        private ProfilerRecorder gcAllocRecorder;
        private ProfilerRecorder usedMemoryRecorder;
        private long gcAllocTotal;
        private long gcAllocFrames;
        private long maxUsedMemory;

        /// <summary>Scenario clock, seconds; negative before the shared start instant.</summary>
        private double ScenarioTime =>
            double.IsNaN(this.startRealtime) ? -1 : Time.realtimeSinceStartupAsDouble - this.startRealtime;

        internal void Configure(TestLabPlan plan, string outDir, string controlPath)
        {
            this.plan = plan;
            this.planner = new TestLabInputPlanner(plan);
            this.jumps = new RenderJumpDetector(plan.max_speed, plan.jump_factor, plan.jump_slack);
            this.outDir = outDir;
            this.controlPath = controlPath;

            Directory.CreateDirectory(outDir);
            Directory.CreateDirectory(Path.Combine(outDir, "shots"));
            this.events = Open("events.ndjson");
            this.inputs = Open("inputs.ndjson");
            this.snapshots = Open("snapshots.ndjson");
            this.samples = Open("samples.ndjson");
            this.frameCsv = Open("frames.csv");
            this.frameCsv.WriteLine("frame,t,dt_ms,scenario_t");

            this.source.MoveRead = this.OnMoveRead;
            GameplayInput.Source = this.source;

            this.gcAllocRecorder = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "GC Allocated In Frame");
            this.usedMemoryRecorder = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "Total Used Memory");

            Application.runInBackground = true;
            this.ShowArenaBoxesFor(plan.map);
            this.Event("configured", "\"scenario\":" + Quote(plan.scenario_id) + ",\"client_index\":" + plan.client_index +
                                      ",\"run_id\":" + Quote(plan.run_id) +
                                      ",\"gc_recorder\":" + Bool(this.gcAllocRecorder.Valid) +
                                      ",\"memory_recorder\":" + Bool(this.usedMemoryRecorder.Valid));
            this.WriteStatus();

            StreamWriter Open(string name) =>
                new StreamWriter(Path.Combine(outDir, name), false, new UTF8Encoding(false)) { AutoFlush = false };
        }

        /// <summary>
        /// The test scene carries one mirror of server collision boxes per map, named
        /// <c>ServerCollisionBoxes[&lt;map&gt;]</c>; only the running map's is shown, so a box drawn on
        /// screen is always one the server actually collides with.
        /// </summary>
        private void ShowArenaBoxesFor(string map)
        {
            var arena = GameObject.Find("TestLabArena");
            if (arena == null)
            {
                return;
            }

            foreach (Transform child in arena.transform)
            {
                if (child.name.StartsWith("ServerCollisionBoxes[", StringComparison.Ordinal))
                {
                    child.gameObject.SetActive(child.name == "ServerCollisionBoxes[" + map + "]");
                }
            }
        }

        private void Update()
        {
            if (this.plan == null || this.finished)
            {
                return;
            }

            try
            {
                this.UpdateCore();
            }
            catch (Exception ex)
            {
                this.Fail("update: " + ex);
            }
        }

        private void UpdateCore()
        {
            double now = Time.realtimeSinceStartupAsDouble;
            this.TryResolveServices();
            this.PollControl(now);

            if (this.client != null && this.client.State == NetworkClientState.InWorld && double.IsNaN(this.inWorldAt))
            {
                this.inWorldAt = now;
                this.phase = "in_world";
                this.Event("in_world", "\"user_id\":" + Quote(this.client.UserId));
                this.WriteStatus();
            }

            double t = this.ScenarioTime;
            if (t < 0 || this.client == null)
            {
                this.source.Clear();
                return;
            }

            if (this.phase == "in_world")
            {
                this.phase = "running";
                this.Event("scenario_start", "\"start_realtime\":" + Num(this.startRealtime));
                this.WriteStatus();
            }

            this.CollectWorld(out float selfX, out float selfY, out bool selfKnown);
            var planned = this.planner.Plan(t, selfX, selfY, selfKnown, this.plannerEntities, this.clientPositions);
            this.source.Set(planned);
            if (planned.Cast)
            {
                this.castsPlanned++;
                this.inputs.WriteLine("{\"t\":" + Num(now) + ",\"st\":" + Num(t) + ",\"kind\":\"cast\",\"target\":" +
                                      Quote(planned.CastTarget) + (planned.HasAim
                                          ? ",\"aim\":[" + Num(planned.Aim.x) + "," + Num(planned.Aim.y) + "]"
                                          : string.Empty) + "}");
            }

            if (planned.Jump)
            {
                this.jumpsPlanned++;
                this.inputs.WriteLine("{\"t\":" + Num(now) + ",\"st\":" + Num(t) + ",\"kind\":\"jump\"}");
            }

            if (this.nextScreenshot < this.plan.screenshots_s.Length && t >= this.plan.screenshots_s[this.nextScreenshot])
            {
                this.Screenshot("t" + this.plan.screenshots_s[this.nextScreenshot].ToString("0.0", Inv));
                this.nextScreenshot++;
            }

            if (t >= this.plan.duration_s)
            {
                this.Finish("completed");
            }
        }

        private void LateUpdate()
        {
            if (this.plan == null || this.finished)
            {
                return;
            }

            try
            {
                this.LateUpdateCore();
            }
            catch (Exception ex)
            {
                this.Fail("late update: " + ex);
            }
        }

        private void LateUpdateCore()
        {
            double now = Time.realtimeSinceStartupAsDouble;
            double dt = Time.unscaledDeltaTime;
            this.frameIndex++;
            double t = this.ScenarioTime;

            if (t >= 0)
            {
                this.frames.Add(dt * 1000.0);
                if (this.gcAllocRecorder.Valid)
                {
                    this.gcAllocTotal += this.gcAllocRecorder.LastValue;
                    this.gcAllocFrames++;
                }

                if (this.usedMemoryRecorder.Valid)
                {
                    this.maxUsedMemory = Math.Max(this.maxUsedMemory, this.usedMemoryRecorder.LastValue);
                }
            }

            this.frameCsv.WriteLine(this.frameIndex.ToString(Inv) + "," + Num(now) + "," + Num(dt * 1000.0) + "," + Num(t));

            if (this.predictor != null)
            {
                this.maxPredictionCorrection = Math.Max(this.maxPredictionCorrection, this.predictor.LastCorrection);
            }

            bool sample = t >= 0 && now >= this.nextSampleAt;
            if (sample)
            {
                this.nextSampleAt = now + (1.0 / Math.Max(1f, this.plan.sample_hz));
                this.line.Clear();
                this.line.Append("{\"t\":").Append(Num(now)).Append(",\"st\":").Append(Num(t))
                    .Append(",\"frame\":").Append(this.frameIndex)
                    .Append(",\"world_tick\":").Append(this.client != null ? this.client.World.Tick : -1)
                    .Append(",\"rtt_ms\":").Append(this.client?.Session?.RoundTripMs ?? -1);
                if (this.predictor != null)
                {
                    var p = this.predictor.Position3;
                    this.line.Append(",\"pred\":{\"x\":").Append(Num(p.X)).Append(",\"y\":").Append(Num(p.Y))
                        .Append(",\"corr\":").Append(Num(this.predictor.LastCorrection))
                        .Append(",\"snaps\":").Append(this.predictor.Snaps)
                        .Append(",\"smoothed\":").Append(this.predictor.SmoothedCorrections)
                        .Append(",\"pending\":").Append(this.predictor.PendingCount)
                        .Append(",\"reconciles\":").Append(this.predictor.Reconciles).Append('}');
                }

                this.line.Append(",\"ents\":[");
            }

            int written = this.ObserveRendered(now, dt, sample);
            if (sample)
            {
                this.line.Append("],\"n\":").Append(written).Append('}');
                this.samples.WriteLine(this.line.ToString());
            }

            if (now >= this.nextFlush)
            {
                this.nextFlush = now + 1.0;
                this.FlushAll();
            }
        }

        /// <summary>
        /// Every replicated entity's rendered ground position: jump detection each frame, and on
        /// sample frames one <c>[id, type, local, rendered x, rendered y, received x, received y]</c>
        /// row per entity (received = the newest snapshot this client merged).
        /// </summary>
        private int ObserveRendered(double now, double dt, bool sample)
        {
            var world = World.DefaultGameObjectInjectionWorld;
            if (world == null || !world.IsCreated)
            {
                return 0;
            }

            if (!this.queryCreated)
            {
                this.renderedQuery = world.EntityManager.CreateEntityQuery(
                    ComponentType.ReadOnly<NetworkEntity>(), ComponentType.ReadOnly<LocalTransform>());
                this.queryCreated = true;
            }

            using var ids = this.renderedQuery.ToComponentDataArray<NetworkEntity>(Allocator.Temp);
            using var transforms = this.renderedQuery.ToComponentDataArray<LocalTransform>(Allocator.Temp);
            int written = 0;
            for (int i = 0; i < ids.Length; i++)
            {
                string id = ids[i].Id.ToString();
                bool isLocal = ids[i].IsLocal;
                // Snapshot space is the XZ plane: wire (x, y) is Unity (x, z).
                float rx = transforms[i].Position.x;
                float ry = transforms[i].Position.z;

                if (this.ScenarioTime >= 0 &&
                    this.jumps.Observe(id, isLocal, rx, ry, this.frameIndex, dt, out var jump))
                {
                    this.Event("render_jump",
                        "\"id\":" + Quote(id) + ",\"local\":" + Bool(isLocal) + ",\"type\":" + Quote(ids[i].Type.ToString()) +
                        ",\"step\":" + Num(jump.Step) + ",\"allowed\":" + Num(jump.Allowed) + ",\"dt_ms\":" + Num(jump.Dt * 1000.0) +
                        ",\"x\":" + Num(rx) + ",\"y\":" + Num(ry));
                    if (this.anomalyShots < this.plan.anomaly_screenshots && now - this.lastAnomalyShot > 1.0)
                    {
                        this.anomalyShots++;
                        this.lastAnomalyShot = now;
                        this.Screenshot("anomaly" + this.anomalyShots);
                    }
                }

                if (sample)
                {
                    if (written > 0)
                    {
                        this.line.Append(',');
                    }

                    this.line.Append("[").Append(Quote(id)).Append(',').Append(Quote(ids[i].Type.ToString()))
                        .Append(',').Append(isLocal ? '1' : '0')
                        .Append(',').Append(Num(rx)).Append(',').Append(Num(ry));
                    if (this.client != null && this.client.World.TryGet(id, out var received))
                    {
                        this.line.Append(',').Append(Num(received.X)).Append(',').Append(Num(received.Y));
                    }

                    this.line.Append(']');
                    written++;
                }
            }

            return written;
        }

        private void CollectWorld(out float selfX, out float selfY, out bool selfKnown)
        {
            this.plannerEntities.Clear();
            selfX = 0f;
            selfY = 0f;
            selfKnown = false;

            string self = this.client.UserId;
            foreach (var pair in this.client.World.EntityMap)
            {
                var e = pair.Value;
                if (e.Id == self)
                {
                    selfX = e.X;
                    selfY = e.Y;
                    selfKnown = true;
                }

                this.plannerEntities.Add(new PlannerEntity(e.Id, e.X, e.Y, e.Type == MobType && e.Hp > 0));
            }

            // Prefer the predicted position for the local player: it is what the player sees and
            // steers by, and it leads the server by the input latency.
            if (this.predictor != null && this.predictor.IsEnabled && this.predictor.Reconciles > 0)
            {
                var p = this.predictor.Position3;
                selfX = p.X;
                selfY = p.Y;
                selfKnown = true;
            }

            this.clientPositions.Clear();
            if (this.control != null)
            {
                foreach (string userId in this.control.client_user_ids)
                {
                    if (!string.IsNullOrEmpty(userId) && this.client.World.TryGet(userId, out var other))
                    {
                        this.clientPositions.Add(new Vector2(other.X, other.Y));
                    }
                    else
                    {
                        this.clientPositions.Add(new Vector2(float.NaN, float.NaN));
                    }
                }
            }
        }

        private void TryResolveServices()
        {
            if (this.client != null)
            {
                return;
            }

            // The root scope owns the network client and the predictor (GameLifetimeScope,
            // DotsRegistration). Test-only code resolving from it is the one place a lookup is
            // acceptable: the runner is not composed by VContainer, by design, so that removing
            // the testlab define removes it without touching a scope.
            var scope = FindAnyObjectByType<GameLifetimeScope>();
            if (scope == null || scope.Container == null)
            {
                return;
            }

            this.client = scope.Container.Resolve(typeof(NetworkClient)) as NetworkClient;
            try
            {
                this.predictor = scope.Container.Resolve(typeof(LocalMovePredictor)) as LocalMovePredictor;
            }
            catch (VContainer.VContainerException)
            {
                this.predictor = null;
            }

            if (this.client != null)
            {
                this.client.SnapshotReceived += this.OnSnapshot;
                this.client.StateChanged += this.OnStateChanged;
                this.Event("services", "\"predictor\":" + Bool(this.predictor != null));
            }
        }

        private void OnSnapshot(ResolvedSnapshot snapshot)
        {
            double now = Time.realtimeSinceStartupAsDouble;
            this.snapshotsReceived++;
            if (snapshot.Full)
            {
                this.keyframes++;
            }

            if (!double.IsNaN(this.lastSnapshotAt) && this.ScenarioTime >= 0)
            {
                this.maxSnapshotGapS = Math.Max(this.maxSnapshotGapS, now - this.lastSnapshotAt);
            }

            this.lastSnapshotAt = now;
            this.snapshots?.WriteLine("{\"t\":" + Num(now) + ",\"tick\":" + snapshot.Tick + ",\"ack\":" + snapshot.AckTick +
                                      ",\"full\":" + Bool(snapshot.Full) + ",\"n\":" + snapshot.Entities.Count +
                                      ",\"removed\":" + (snapshot.Removed?.Count ?? 0) + "}");
        }

        private void OnStateChanged(NetworkClientState state)
        {
            this.stateChanges++;
            this.Event("net_state", "\"state\":" + Quote(state.ToString()));
        }

        private void OnMoveRead(float x, float y)
        {
            this.movesRead++;
            this.inputs?.WriteLine("{\"t\":" + Num(Time.realtimeSinceStartupAsDouble) + ",\"st\":" + Num(this.ScenarioTime) +
                                   ",\"kind\":\"move\",\"x\":" + Num(x) + ",\"y\":" + Num(y) + "}");
        }

        private void PollControl(double now)
        {
            if (now < this.nextControlPoll || string.IsNullOrEmpty(this.controlPath))
            {
                return;
            }

            this.nextControlPoll = now + 0.2;
            if (!File.Exists(this.controlPath))
            {
                return;
            }

            string json;
            try
            {
                json = File.ReadAllText(this.controlPath);
            }
            catch (IOException)
            {
                return;
            }

            if (!TestLabControl.TryParse(json, out var parsed))
            {
                return;
            }

            this.control = parsed;
            if (parsed.abort)
            {
                this.Finish("aborted");
                return;
            }

            if (double.IsNaN(this.startRealtime) && parsed.start_at_unix_ms > 0)
            {
                // Map the shared wall-clock instant onto this process's monotonic clock ONCE.
                // Every client does the same against the same host clock, so their scenario
                // clocks agree to within a poll; after this, only the monotonic clock is used.
                long unixNow = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                this.startRealtime = now + ((parsed.start_at_unix_ms - unixNow) / 1000.0);
                this.Event("start_scheduled", "\"in_s\":" + Num((parsed.start_at_unix_ms - unixNow) / 1000.0));
            }
        }

        private void Screenshot(string label)
        {
            string name = $"c{this.plan.client_index}-{this.frameIndex:D6}-{label}.png";
            string path = Path.Combine(this.outDir, "shots", name);
            ScreenCapture.CaptureScreenshot(path);
            this.Event("screenshot", "\"file\":" + Quote("shots/" + name) + ",\"label\":" + Quote(label));
        }

        private void Finish(string outcome)
        {
            if (this.finished)
            {
                return;
            }

            this.finished = true;
            this.phase = outcome == "completed" ? "done" : outcome;
            this.source.Clear();
            this.Event("finish", "\"outcome\":" + Quote(outcome));
            this.WriteSummary(outcome);
            this.WriteStatus();
            this.FlushAll();
            this.CloseAll();
            GameplayInput.Source = null;

            if (this.plan.quit_on_done)
            {
                // A frame of grace so a screenshot requested this frame is written.
                this.Invoke(nameof(this.Quit), 0.5f);
            }
        }

        private void Quit() => Application.Quit(this.error == null ? 0 : 3);

        private void Fail(string message)
        {
            if (this.error != null)
            {
                return;
            }

            this.error = message;
            Debug.LogError("[TestLab] " + message);
            this.Event("error", "\"message\":" + Quote(message));
            this.Finish("error");
        }

        private void WriteSummary(string outcome)
        {
            var sb = new StringBuilder(2048);
            sb.Append("{\"schema\":\"rpg.testlab.client-summary.v1\"")
                .Append(",\"run_id\":").Append(Quote(this.plan.run_id))
                .Append(",\"scenario\":").Append(Quote(this.plan.scenario_id))
                .Append(",\"client_index\":").Append(this.plan.client_index)
                .Append(",\"outcome\":").Append(Quote(outcome))
                .Append(",\"error\":").Append(Quote(this.error))
                .Append(",\"user_id\":").Append(Quote(this.client?.UserId))
                .Append(",\"in_world_at\":").Append(Num(this.inWorldAt))
                .Append(",\"start_realtime\":").Append(Num(this.startRealtime))
                .Append(",\"scenario_seconds\":").Append(Num(Math.Max(0, this.ScenarioTime)))
                .Append(",\"unity_version\":").Append(Quote(Application.version + "/" + Application.unityVersion))
                .Append(",\"debug_build\":").Append(Bool(Debug.isDebugBuild))
                .Append(",\"screen\":[").Append(Screen.width).Append(',').Append(Screen.height).Append(']')
                .Append(",\"frames\":{\"count\":").Append(this.frames.Count)
                .Append(",\"mean_ms\":").Append(Num(this.frames.Mean()))
                .Append(",\"p50_ms\":").Append(Num(this.frames.Percentile(50)))
                .Append(",\"p95_ms\":").Append(Num(this.frames.Percentile(95)))
                .Append(",\"p99_ms\":").Append(Num(this.frames.Percentile(99)))
                .Append(",\"max_ms\":").Append(Num(this.frames.Max()))
                .Append(",\"long_frames\":").Append(this.frames.LongFrames)
                .Append(",\"long_frame_ms\":").Append(Num(this.frames.LongFrameMs)).Append('}')
                .Append(",\"gc\":{\"valid\":").Append(Bool(this.gcAllocRecorder.Valid))
                .Append(",\"alloc_bytes_total\":").Append(this.gcAllocTotal)
                .Append(",\"alloc_bytes_per_frame\":").Append(Num(this.gcAllocFrames > 0 ? (double)this.gcAllocTotal / this.gcAllocFrames : double.NaN))
                .Append(",\"max_used_memory_bytes\":").Append(this.maxUsedMemory)
                .Append(",\"memory_valid\":").Append(Bool(this.usedMemoryRecorder.Valid)).Append('}')
                .Append(",\"net\":{\"snapshots\":").Append(this.snapshotsReceived)
                .Append(",\"keyframes\":").Append(this.keyframes)
                .Append(",\"max_snapshot_gap_ms\":").Append(Num(this.maxSnapshotGapS * 1000.0))
                .Append(",\"rtt_ms\":").Append(this.client?.Session?.RoundTripMs ?? -1)
                .Append(",\"state_changes\":").Append(this.stateChanges)
                .Append(",\"final_state\":").Append(Quote(this.client?.State.ToString())).Append('}')
                .Append(",\"input\":{\"moves_read\":").Append(this.movesRead)
                .Append(",\"casts_planned\":").Append(this.castsPlanned)
                .Append(",\"jumps_planned\":").Append(this.jumpsPlanned).Append('}')
                .Append(",\"prediction\":{\"present\":").Append(Bool(this.predictor != null));
            if (this.predictor != null)
            {
                sb.Append(",\"snaps\":").Append(this.predictor.Snaps)
                    .Append(",\"smoothed\":").Append(this.predictor.SmoothedCorrections)
                    .Append(",\"reconciles\":").Append(this.predictor.Reconciles)
                    .Append(",\"max_correction\":").Append(Num(this.maxPredictionCorrection));
            }

            sb.Append('}')
                .Append(",\"render\":{\"jumps\":").Append(this.jumps.Jumps)
                .Append(",\"local_jumps\":").Append(this.jumps.LocalJumps)
                .Append(",\"max_step\":").Append(Num(this.jumps.MaxStep))
                .Append(",\"max_step_speed\":").Append(Num(this.jumps.MaxStepSpeed))
                .Append(",\"threshold\":{\"max_speed\":").Append(Num(this.plan.max_speed))
                .Append(",\"factor\":").Append(Num(this.plan.jump_factor))
                .Append(",\"slack\":").Append(Num(this.plan.jump_slack)).Append("}}")
                .Append('}');
            File.WriteAllText(Path.Combine(this.outDir, "summary.json"), sb.ToString());
        }

        private void WriteStatus()
        {
            if (this.outDir == null)
            {
                return;
            }

            string json = "{\"phase\":" + Quote(this.phase) + ",\"user_id\":" + Quote(this.client?.UserId) +
                          ",\"client_index\":" + this.plan.client_index + ",\"realtime\":" + Num(Time.realtimeSinceStartupAsDouble) +
                          ",\"error\":" + Quote(this.error) + "}";
            string path = Path.Combine(this.outDir, "status.json");
            string tmp = path + ".tmp";
            File.WriteAllText(tmp, json);

            // The orchestrator polls this file. On Windows a reader holding it open makes
            // File.Replace fail ("Unable to remove the file to be replaced"), so retry briefly,
            // then fall back to a direct write. Status is advisory: a failed write must never
            // fail the run (it did, once, in sweep S07 level 25).
            for (int attempt = 0; attempt < 5; attempt++)
            {
                try
                {
                    if (File.Exists(path))
                    {
                        File.Replace(tmp, path, null);
                    }
                    else
                    {
                        File.Move(tmp, path);
                    }

                    return;
                }
                catch (IOException)
                {
                    System.Threading.Thread.Sleep(20);
                }
                catch (UnauthorizedAccessException)
                {
                    System.Threading.Thread.Sleep(20);
                }
            }

            try
            {
                File.WriteAllText(path, json);
            }
            catch (IOException ex)
            {
                Debug.LogWarning("[TestLab] status.json not written this time: " + ex.Message);
            }
        }

        private void Event(string kind, string fields)
        {
            this.events?.WriteLine("{\"t\":" + Num(Time.realtimeSinceStartupAsDouble) + ",\"st\":" + Num(this.ScenarioTime) +
                                   ",\"event\":" + Quote(kind) + (string.IsNullOrEmpty(fields) ? string.Empty : "," + fields) + "}");
        }

        private void FlushAll()
        {
            this.events?.Flush();
            this.inputs?.Flush();
            this.snapshots?.Flush();
            this.samples?.Flush();
            this.frameCsv?.Flush();
        }

        private void CloseAll()
        {
            this.events?.Dispose();
            this.inputs?.Dispose();
            this.snapshots?.Dispose();
            this.samples?.Dispose();
            this.frameCsv?.Dispose();
            this.events = this.inputs = this.snapshots = this.samples = this.frameCsv = null;
        }

        private void OnDestroy()
        {
            if (this.client != null)
            {
                this.client.SnapshotReceived -= this.OnSnapshot;
                this.client.StateChanged -= this.OnStateChanged;
            }

            if (!this.finished && this.plan != null)
            {
                this.Finish("destroyed");
            }

            this.gcAllocRecorder.Dispose();
            this.usedMemoryRecorder.Dispose();
        }

        private void OnApplicationQuit()
        {
            if (!this.finished && this.plan != null)
            {
                this.Finish("quit");
            }
        }

        private void OnGUI()
        {
            if (this.plan == null || !this.plan.overlay)
            {
                return;
            }

            var p = this.predictor;
            string text =
                $"TESTLAB {this.plan.scenario_id} c{this.plan.client_index}  phase {this.phase}  t {Math.Max(0, this.ScenarioTime):0.0}/{this.plan.duration_s:0}s\n" +
                $"user {this.client?.UserId}  tick {this.client?.World.Tick}  rtt {this.client?.Session?.RoundTripMs}ms  ents {this.client?.World.EntityMap.Count}\n" +
                $"frame p95 {this.frames.Percentile(95):0.0}ms  long {this.frames.LongFrames}  jumps {this.jumps.Jumps} (local {this.jumps.LocalJumps})\n" +
                (p != null ? $"predict err {p.LastCorrection:0.000} snaps {p.Snaps} pending {p.PendingCount}" : "predict n/a");
            GUI.color = Color.black;
            GUI.Label(new Rect(11, 11, 900, 80), text);
            GUI.color = Color.yellow;
            GUI.Label(new Rect(10, 10, 900, 80), text);
        }

        private static string Num(double v) =>
            double.IsNaN(v) || double.IsInfinity(v) ? "null" : v.ToString("R", Inv);

        private static string Num(float v) =>
            float.IsNaN(v) || float.IsInfinity(v) ? "null" : v.ToString("R", Inv);

        private static string Bool(bool v) => v ? "true" : "false";

        private static string Quote(string s)
        {
            if (s == null)
            {
                return "null";
            }

            var sb = new StringBuilder(s.Length + 2);
            sb.Append('"');
            foreach (char c in s)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < 0x20)
                        {
                            sb.Append("\\u").Append(((int)c).ToString("x4", Inv));
                        }
                        else
                        {
                            sb.Append(c);
                        }

                        break;
                }
            }

            return sb.Append('"').ToString();
        }
    }
}
