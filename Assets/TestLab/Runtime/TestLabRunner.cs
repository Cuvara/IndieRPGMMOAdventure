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
    using UnityEngine.Rendering;
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
    /// <c>snapshots.ndjson</c>, <c>samples.ndjson</c>, <c>frames.csv</c>, <c>delivery.csv</c>,
    /// <c>render.csv</c> (<see cref="DeliveryRecorder"/>), <c>summary.json</c>
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
        private readonly FrameReadTap tap = new FrameReadTap();
        private readonly UserIdLatch userId = new UserIdLatch();
        private readonly BinderProbe binderProbe = new BinderProbe();
        private EntityLifecycle lifecycle;
        private bool[] oneShotFired;
        private long castsRead;
        private long stalls;
        private int reconnectsRequested;
        private int reconnectEvents;

        // The render clock of the positions drawn THIS frame (see BinderProbe remarks): the binder's
        // RenderTick captured in the previous LateUpdate, or the ECS clock read at the previous
        // render callback. Source is "binder", "ecs" or null (none readable).
        private double drawnRenderTick = double.NaN;
        private long drawnServerTick = -1;
        private string drawnClockSource;
        private double capturedRenderTick = double.NaN;
        private long capturedServerTick = -1;
        private bool capturedFromBinder;
        private double lastEcsRenderTick = double.NaN;
        private long lastEcsNewestTick = -1;

        private TestLabPlan plan;
        private TestLabInputPlanner planner;
        private RenderJumpDetector jumps;
        private string outDir;
        private string controlPath;

        private NetworkClient client;
        private LocalMovePredictor predictor;
        private EntityQuery renderedQuery;
        private bool queryCreated;
        private EntityQuery timelineQuery;
        private bool timelineQueryCreated;

        // Every stream writes on its own background thread (BackgroundFileWriter): the main thread
        // only copies characters, so a slow disk can no longer stall the frame being measured.
        private BackgroundFileWriter events;
        private BackgroundFileWriter inputs;
        private BackgroundFileWriter snapshots;
        private BackgroundFileWriter samples;
        private BackgroundFileWriter frameCsv;
        private BackgroundFileWriter deliveryCsv;
        private BackgroundFileWriter renderCsv;

        // Every output writer, kept after CloseAll so the summary can report how each one ended
        // (R6-1: a writer failure fails the run, it is never only a warning).
        private readonly List<KeyValuePair<string, BackgroundFileWriter>> writers = new List<KeyValuePair<string, BackgroundFileWriter>>(8);
        private string writerFailure;
        private DeliveryRecorder delivery;
        private string tapStatus = "not_installed";
        private long tapReadsConsumed;
        private int lastRenderRowFrame = -1;

        private string phase = "booting";
        private string error;
        private double inWorldAt = double.NaN;
        private double startRealtime = double.NaN;
        private double nextControlPoll;
        private double nextSampleAt;
        private double nextFlush;

        // The overlay's frame p95 over the last FrameStats.WindowFrames frames, recomputed at most
        // once a second. Its cost is fixed (FrameStats.RecentPercentile sorts a 600-frame window into
        // a preallocated buffer); it used to sort every frame of the run, which grew to a ~200 ms
        // stall per second over a one-hour soak (testlab soaks 20261022T100043Z-S09-zo1i and
        // 20261022T112220Z-S09-eeoa).
        private double overlayP95 = double.NaN;
        private double nextOverlayStats;
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
            // First, so every LabClock value written by this run is on the realtime axis.
            LabClock.Anchor(Time.realtimeSinceStartupAsDouble);
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
            // ~16 KB a line at 20 Hz: a deep pool (64 x 64K chars, about 25 s) rides out a slow disk.
            this.samples = Open("samples.ndjson", 64 * 1024, 64);
            this.frameCsv = Open("frames.csv");
            this.frameCsv.WriteLine("frame,t,dt_ms,scenario_t");
            this.deliveryCsv = Open(DeliveryRecorder.DeliveryFile);
            this.renderCsv = Open(DeliveryRecorder.RenderFile);
            this.delivery = new DeliveryRecorder(this.deliveryCsv, this.renderCsv);
            RenderPipelineManager.beginContextRendering += this.OnBeginContextRendering;

            this.source.MoveRead = this.OnMoveRead;
            this.source.CastRead = this.OnCastRead;
            this.lifecycle = new EntityLifecycle(this.Event);
            this.oneShotFired = new bool[plan.actions.Length];
            GameplayInput.Source = this.source;

            this.gcAllocRecorder = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "GC Allocated In Frame");
            this.usedMemoryRecorder = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "Total Used Memory");

            Application.runInBackground = true;
            this.ShowArenaBoxesFor(plan.map);
            this.Event("configured", "\"scenario\":" + Quote(plan.scenario_id) + ",\"client_index\":" + plan.client_index +
                                      ",\"run_id\":" + Quote(plan.run_id) +
                                      ",\"gc_recorder\":" + Bool(this.gcAllocRecorder.Valid) +
                                      ",\"memory_recorder\":" + Bool(this.usedMemoryRecorder.Valid) +
                                      ",\"delivery_schema\":" + Quote(DeliveryRecorder.Schema));
            this.WriteStatus();

            BackgroundFileWriter Open(string name, int chars = BackgroundFileWriter.DefaultBufferChars, int count = BackgroundFileWriter.DefaultBufferCount)
            {
                var writer = new BackgroundFileWriter(Path.Combine(outDir, name), chars, count);
                this.writers.Add(new KeyValuePair<string, BackgroundFileWriter>(name, writer));
                return writer;
            }
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
            this.userId.Observe(this.client?.UserId);
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

            this.RunOneShots(t);
            if (this.finished)
            {
                return;
            }

            this.CollectWorld(out float selfX, out float selfY, out bool selfKnown);
            var planned = this.planner.Plan(t, selfX, selfY, selfKnown, this.plannerEntities, this.clientPositions);
            this.source.Set(planned);
            if (planned.Cast)
            {
                this.castsPlanned++;
                this.inputs.WriteLine("{\"t\":" + Num(now) + ",\"st\":" + Num(t) + ",\"kind\":\"cast\",\"target\":" +
                                      Quote(planned.CastTarget) +
                                      (planned.CastKind != null ? ",\"action\":" + Quote(planned.CastKind) : string.Empty) +
                                      (planned.HasAim
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
            this.ResolveDrawnClock();
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
                    .Append(",\"rtt_ms\":").Append(this.client?.Session?.RoundTripMs ?? -1)
                    .Append(",\"render_tick\":").Append(Num(this.drawnRenderTick))
                    .Append(",\"render_clock\":").Append(Quote(this.drawnClockSource));
                if (this.predictor != null)
                {
                    var p = this.predictor.Position3;
                    this.line.Append(",\"pred\":{\"x\":").Append(Num(p.X)).Append(",\"y\":").Append(Num(p.Y))
                        .Append(",\"corr\":").Append(Num(this.predictor.LastCorrection))
                        .Append(",\"snaps\":").Append(this.predictor.Snaps)
                        .Append(",\"smoothed\":").Append(this.predictor.SmoothedCorrections)
                        .Append(",\"pending\":").Append(this.predictor.PendingCount)
                        .Append(",\"reconciles\":").Append(this.predictor.Reconciles)
                        .Append(",\"tick_hz\":").Append(this.predictor.TickRateHz).Append('}');
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
                if (this.CheckWriters())
                {
                    // An output that stopped being written fails the run now, not silently at the end.
                    this.Fail(this.writerFailure);
                    return;
                }
            }

            // The binder has ticked for this frame (DotsWorldBridge.Update); the drain applies what
            // it enqueued at the start of the next frame, so that frame is drawn at this RenderTick.
            this.capturedFromBinder = this.binderProbe.TryRead(now, out this.capturedRenderTick, out this.capturedServerTick);
        }

        /// <summary>Sets the drawn* fields for the frame now being recorded (see the field remarks).</summary>
        private void ResolveDrawnClock()
        {
            if (this.capturedFromBinder)
            {
                this.drawnRenderTick = this.capturedRenderTick;
                this.drawnServerTick = this.capturedServerTick;
                this.drawnClockSource = "binder";
            }
            else if (!double.IsNaN(this.lastEcsRenderTick))
            {
                this.drawnRenderTick = this.lastEcsRenderTick;
                this.drawnServerTick = this.lastEcsNewestTick;
                this.drawnClockSource = "ecs";
            }
            else
            {
                this.drawnRenderTick = double.NaN;
                this.drawnServerTick = -1;
                this.drawnClockSource = null;
            }
        }

        /// <summary>
        /// The one-shot plan actions (stall, reconnect, quit), each fired once at the first frame
        /// with <c>t &gt;= t0</c>. Inert unless the plan lists them.
        /// </summary>
        private void RunOneShots(double t)
        {
            var actions = this.plan.actions;
            for (int i = 0; i < actions.Length; i++)
            {
                var action = actions[i];
                if (this.oneShotFired[i] || !TestLabAction.IsOneShot(action.kind) || t < action.t0)
                {
                    continue;
                }

                this.oneShotFired[i] = true;
                switch (action.kind)
                {
                    case TestLabAction.Stall:
                    {
                        int ms = Math.Max(0, action.ms);
                        long start = LabClock.NowUs();
                        System.Threading.Thread.Sleep(ms);
                        long end = LabClock.NowUs();
                        this.stalls++;
                        this.Event("stall", "\"ms\":" + ms + ",\"start\":" + Num(start / 1e6) + ",\"end\":" + Num(end / 1e6) +
                                            ",\"start_us\":" + start + ",\"end_us\":" + end +
                                            ",\"actual_ms\":" + Num((end - start) / 1000.0));
                        break;
                    }

                    case TestLabAction.Reconnect:
                    {
                        bool dropped = this.tap.DropLink();
                        this.reconnectsRequested++;
                        this.Event("reconnect_requested", "\"dropped\":" + Bool(dropped) + ",\"tap\":" + Quote(this.tapStatus) +
                                                          ",\"state\":" + Quote(this.client.State.ToString()) +
                                                          ",\"generation\":" + this.client.Generation);
                        break;
                    }

                    case TestLabAction.Quit:
                        this.Event("quit_requested", "\"state\":" + Quote(this.client.State.ToString()));
                        // The player's own way out: a polite leave, no automatic reconnect.
                        this.userId.Observe(this.client.UserId);
                        this.client.Disconnect();
                        this.Event("disconnected", "\"state\":" + Quote(this.client.State.ToString()));
                        this.Finish("left");
                        return;
                }
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
                if (this.lifecycle.AwaitingRender)
                {
                    this.lifecycle.OnRendered(id, rx, ry, this.frameIndex);
                }

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
                this.client.ReconnectAttemptStarted += this.OnReconnectAttempt;
                this.client.Reconnected += this.OnReconnected;
                this.client.ReconnectFailed += this.OnReconnectFailed;
                this.Event("services", "\"predictor\":" + Bool(this.predictor != null));
                this.InstallTap();
            }
        }

        /// <summary>
        /// Wraps the gameplay transport so <c>delivery.csv</c> gets a receive time and a byte count
        /// per snapshot (<see cref="FrameReadTap"/>). A session that already exists was opened
        /// through the unwrapped factory, so its rows have empty <c>recv_us</c>/<c>bytes</c> until
        /// the next join; that is recorded as <c>late</c>, never papered over.
        /// </summary>
        private void InstallTap()
        {
            string why;
            try
            {
                why = this.tap.TryInstall(this.client);
            }
            catch (Exception ex)
            {
                why = ex.GetType().Name + ": " + ex.Message;
            }

            this.tapStatus = why != null ? "unavailable" : this.client.Session != null ? "late" : "installed";
            this.Event("delivery_tap", "\"status\":" + Quote(this.tapStatus) + ",\"reason\":" + Quote(why));
        }

        private void OnSnapshot(ResolvedSnapshot snapshot)
        {
            long appliedUs = LabClock.NowUs();
            long recvUs = -1;
            int bytes = -1;
            long reads = this.tap.Reads;
            if (reads != this.tapReadsConsumed)
            {
                // The frame read last is this snapshot's: netcode decodes, resolves and merges it
                // synchronously after the read, before the next read (FrameReadTap remarks).
                this.tapReadsConsumed = reads;
                recvUs = this.tap.LastReadUs;
                bytes = this.tap.LastBytes;
            }

            this.delivery?.OnSnapshot(snapshot.Tick, recvUs, appliedUs, bytes, snapshot.Full, snapshot.Entities.Count,
                this.frameIndex + 1);
            if (!this.finished)
            {
                this.lifecycle?.OnSnapshot(snapshot.Tick, snapshot.Full, snapshot.Entities, snapshot.Removed);
            }

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
            if (state != NetworkClientState.InWorld)
            {
                this.delivery?.ResetSession();
                int dropped = this.lifecycle?.Reset() ?? 0;
                if (dropped > 0)
                {
                    this.Event("entities_reset", "\"count\":" + dropped);
                }
            }

            this.Event("net_state", "\"state\":" + Quote(state.ToString()));
        }

        private void OnReconnectAttempt(int attempt)
        {
            this.reconnectEvents++;
            this.Event("reconnect_attempt", "\"attempt\":" + attempt);
        }

        private void OnReconnected()
        {
            this.reconnectEvents++;
            this.Event("reconnected", "\"generation\":" + this.client.Generation + ",\"tapped_sessions\":" + this.tap.TappedSessions);
        }

        private void OnReconnectFailed(Exception ex)
        {
            this.reconnectEvents++;
            this.Event("reconnect_failed", "\"error\":" + Quote(ex?.Message));
        }

        private void OnCastRead()
        {
            this.castsRead++;
            this.inputs?.WriteLine("{\"t\":" + Num(Time.realtimeSinceStartupAsDouble) + ",\"st\":" + Num(this.ScenarioTime) +
                                   ",\"kind\":\"cast_read\"}");
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
            this.FlushAll();
            this.CloseAll();

            // The files are complete only now; a writer that failed (even while closing) makes the
            // run an error, with the failure in summary.json and status.json.
            if (this.CheckWriters() && this.error == null)
            {
                this.error = this.writerFailure;
                outcome = "error";
                this.phase = outcome;
                Debug.LogError("[TestLab] " + this.writerFailure);
            }

            this.WriteSummary(outcome);
            this.WriteStatus();
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
                .Append(",\"user_id\":").Append(Quote(this.userId.Resolve(this.client?.UserId)))
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
                .Append(",\"long_frame_ms\":").Append(Num(this.frames.LongFrameMs))
                .Append(",\"percentile_bucket_ms\":").Append(Num(FrameStats.BucketMs)).Append('}')
                .Append(",\"writers\":{\"mode\":\"background\",\"backpressure_ms\":").Append(Num(this.WriterBackpressureMs()))
                .Append(",\"failed\":").Append(Bool(this.writerFailure != null))
                .Append(",\"failure\":").Append(Quote(this.writerFailure))
                .Append(",\"dropped_chars\":").Append(this.WriterDroppedChars()).Append('}')
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
                .Append(",\"delivery\":{\"schema\":").Append(Quote(DeliveryRecorder.Schema))
                .Append(",\"tap\":").Append(Quote(this.tapStatus))
                .Append(",\"snapshots\":").Append(this.delivery?.Snapshots ?? 0)
                .Append(",\"snapshots_with_bytes\":").Append(this.delivery?.SnapshotsWithBytes ?? 0)
                .Append(",\"render_rows\":").Append(this.delivery?.Frames ?? 0).Append('}')
                .Append(",\"input\":{\"moves_read\":").Append(this.movesRead)
                .Append(",\"casts_planned\":").Append(this.castsPlanned)
                .Append(",\"jumps_planned\":").Append(this.jumpsPlanned)
                .Append(",\"casts_read\":").Append(this.castsRead).Append('}')
                .Append(",\"actions\":{\"stalls\":").Append(this.stalls)
                .Append(",\"reconnects_requested\":").Append(this.reconnectsRequested)
                .Append(",\"reconnect_events\":").Append(this.reconnectEvents)
                .Append(",\"tapped_sessions\":").Append(this.tap.TappedSessions).Append('}')
                .Append(",\"entities\":{\"added\":").Append(this.lifecycle?.Added ?? 0)
                .Append(",\"removed\":").Append(this.lifecycle?.Removed ?? 0)
                .Append(",\"live\":").Append(this.lifecycle?.Live ?? 0).Append('}')
                .Append(",\"prediction\":{\"present\":").Append(Bool(this.predictor != null));
            if (this.predictor != null)
            {
                sb.Append(",\"snaps\":").Append(this.predictor.Snaps)
                    .Append(",\"smoothed\":").Append(this.predictor.SmoothedCorrections)
                    .Append(",\"reconciles\":").Append(this.predictor.Reconciles)
                    .Append(",\"max_correction\":").Append(Num(this.maxPredictionCorrection))
                    .Append(",\"tick_hz\":").Append(this.predictor.TickRateHz);
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

            string json = "{\"phase\":" + Quote(this.phase) + ",\"user_id\":" + Quote(this.userId.Resolve(this.client?.UserId)) +
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

        /// <summary>Sum over every output stream of the time the main thread waited for a free buffer.</summary>
        /// <summary>
        /// True when an output writer has failed; the first failure is kept in writerFailure. Checked
        /// once a second while running (the run fails at once) and again after the files close.
        /// </summary>
        internal bool CheckWriters()
        {
            if (this.writerFailure != null)
            {
                return true;
            }

            this.writerFailure = WriterFailure(this.writers);
            return this.writerFailure != null;
        }

        /// <summary>The first failed writer as "output NAME failed: TYPE: MESSAGE", or null.</summary>
        public static string WriterFailure(IReadOnlyList<KeyValuePair<string, BackgroundFileWriter>> writers)
        {
            foreach (var pair in writers)
            {
                var failure = pair.Value.Failure;
                if (failure != null)
                {
                    return "output " + pair.Key + " failed: " + failure.GetType().Name + ": " + failure.Message;
                }
            }

            return null;
        }

        private long WriterDroppedChars()
        {
            long dropped = 0;
            foreach (var pair in this.writers)
            {
                dropped += pair.Value.DroppedChars;
            }

            return dropped;
        }

        // Over every writer ever opened, so it still reads right after CloseAll (the summary is
        // written once the files are closed).
        private double WriterBackpressureMs()
        {
            double ms = 0;
            foreach (var pair in this.writers)
            {
                ms += pair.Value.BackpressureMs;
            }

            return ms;
        }

        private void FlushAll()
        {
            this.events?.Flush();
            this.inputs?.Flush();
            this.snapshots?.Flush();
            this.samples?.Flush();
            this.frameCsv?.Flush();
            this.delivery?.Flush();
        }

        private void CloseAll()
        {
            this.events?.Dispose();
            this.inputs?.Dispose();
            this.snapshots?.Dispose();
            this.samples?.Dispose();
            this.frameCsv?.Dispose();
            this.delivery?.Dispose();
            this.delivery = null;
            this.deliveryCsv = this.renderCsv = null;
            RenderPipelineManager.beginContextRendering -= this.OnBeginContextRendering;
            this.events = this.inputs = this.snapshots = this.samples = this.frameCsv = null;
        }

        /// <summary>
        /// One <c>render.csv</c> row per rendered frame, taken when the render pipeline starts the
        /// frame. <c>render_tick</c> is the clock the drawn positions were evaluated at: the
        /// binder's (captured one frame earlier, <see cref="BinderProbe"/>) in the shipping client,
        /// else the ECS clock, which <c>PresentationSystemGroup</c> has advanced by now - Entities
        /// appends that group to the end of <c>PreLateUpdate</c>, after <c>LateUpdate</c>.
        /// </summary>
        private void OnBeginContextRendering(ScriptableRenderContext context, List<Camera> cameras)
        {
            if (this.finished || this.delivery == null || this.frameIndex == this.lastRenderRowFrame)
            {
                return;
            }

            this.lastRenderRowFrame = this.frameIndex;
            try
            {
                // The ECS clock has just advanced for this frame; it is the drawn clock only when
                // the binder is not (a timed-path consumer). Kept for the next frame's samples.
                bool ecs = this.TryReadClock(out double ecsTick, out long ecsNewest);
                this.lastEcsRenderTick = ecs ? ecsTick : double.NaN;
                this.lastEcsNewestTick = ecs ? ecsNewest : -1;

                if (this.drawnClockSource == "binder")
                {
                    this.delivery.OnFrame(this.frameIndex, LabClock.NowUs(), true, this.drawnRenderTick, this.drawnServerTick, "binder");
                }
                else
                {
                    this.delivery.OnFrame(this.frameIndex, LabClock.NowUs(), ecs, ecsTick, ecsNewest, "ecs");
                }
            }
            catch (Exception ex)
            {
                this.Fail("render row: " + ex);
            }
        }

        private bool TryReadClock(out double renderTick, out long newestTick)
        {
            renderTick = double.NaN;
            newestTick = -1;
            var world = World.DefaultGameObjectInjectionWorld;
            if (world == null || !world.IsCreated)
            {
                return false;
            }

            if (!this.timelineQueryCreated)
            {
                this.timelineQuery = world.EntityManager.CreateEntityQuery(ComponentType.ReadOnly<InterpolationTimeline>());
                this.timelineQueryCreated = true;
            }

            if (!this.timelineQuery.TryGetSingleton<InterpolationTimeline>(out var timeline) || !timeline.Clock.HasSamples)
            {
                return false;
            }

            renderTick = timeline.Clock.RenderTick;
            newestTick = timeline.Clock.NewestTick;
            return true;
        }

        private void OnDestroy()
        {
            RenderPipelineManager.beginContextRendering -= this.OnBeginContextRendering;
            if (this.client != null)
            {
                this.client.SnapshotReceived -= this.OnSnapshot;
                this.client.StateChanged -= this.OnStateChanged;
                this.client.ReconnectAttemptStarted -= this.OnReconnectAttempt;
                this.client.Reconnected -= this.OnReconnected;
                this.client.ReconnectFailed -= this.OnReconnectFailed;
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

        private double OverlayP95()
        {
            double now = Time.realtimeSinceStartupAsDouble;
            if (now >= this.nextOverlayStats)
            {
                this.nextOverlayStats = now + 1.0;
                this.overlayP95 = this.frames.RecentPercentile(95);
            }

            return this.overlayP95;
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
                $"frame p95(last {FrameStats.WindowFrames}) {this.OverlayP95():0.0}ms  long {this.frames.LongFrames}  jumps {this.jumps.Jumps} (local {this.jumps.LocalJumps})\n" +
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
