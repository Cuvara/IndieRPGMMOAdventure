namespace TestLab
{
    using System;
    using System.Globalization;
    using System.IO;
    using System.Reflection;
    using System.Text;
    using Cuvara.Netcode.Prediction;

    /// <summary>
    /// Writes the netcode predictor's call trace (netcode &gt;= 8986908) to <see cref="FileName"/>:
    /// every call into <c>LocalMovePredictor</c> and <c>PredictionClockSteering</c>, plus the
    /// settings and per-frame timing a headless <c>PredictorTraceReplayer</c> needs to reproduce the
    /// live session exactly. Test-only; enabled by the plan's <c>predictor_trace</c>.
    /// </summary>
    /// <remarks>
    /// <para><b>Line kinds</b> (one JSON object per line, <c>docs/TESTLAB.md</c> "Predictor call
    /// trace"): a <c>"kind":"header"</c> line with the predictor's construction settings, a
    /// <c>"kind":"session"</c> line per join, a frame marker <c>{"frame":..}</c> at the start of every
    /// frame, and the events themselves in netcode's own form (<c>PredictorTraceEvent.AppendJson</c>,
    /// key <c>"c"</c>).</para>
    /// <para><b>Cost.</b> Lines go to a <see cref="BackgroundFileWriter"/>, so the main thread never
    /// touches the disk. The formatting reuses one <see cref="StringBuilder"/> and one char buffer;
    /// what still allocates is netcode's number formatting inside <c>AppendJson</c>, a constant per
    /// event, never growing with the run.</para>
    /// </remarks>
    public sealed class PredictorTraceRecorder
    {
        public const string FileName = "trace.ndjson";
        public const string Schema = "rpg.testlab.predictor-trace.v1";

        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        private readonly TextWriter writer;
        private readonly StringBuilder sb = new StringBuilder(512);
        private char[] chars = new char[512];

        public PredictorTraceRecorder(TextWriter writer)
        {
            this.writer = writer ?? throw new ArgumentNullException(nameof(writer));
            this.Sink = this.OnEvent;
        }

        /// <summary>Assign to <c>LocalMovePredictor.Trace</c> and <c>PredictionClockSteering.Trace</c>.</summary>
        public PredictorTraceSink Sink { get; }

        public long Events { get; private set; }

        public long Frames { get; private set; }

        /// <summary>Stops writing (the file is about to close); a call still traced afterwards is dropped.</summary>
        public bool Closed { get; set; }

        /// <summary>The predictor's construction settings, read from its private field (null if not found).</summary>
        public static bool TryReadSettings(LocalMovePredictor predictor, out PredictionSettings settings)
        {
            settings = default;
            if (predictor == null)
            {
                return false;
            }

            foreach (var f in typeof(LocalMovePredictor).GetFields(BindingFlags.Instance | BindingFlags.NonPublic))
            {
                if (f.FieldType == typeof(PredictionSettings))
                {
                    settings = (PredictionSettings)f.GetValue(predictor);
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// The line a replay starts from: <c>new LocalMovePredictor(new PredictionSettings(tick_rate,
        /// speed, bounds))</c> on map <paramref name="mapId"/>. Calls made later (protocol, tick rate,
        /// geometry, motor params) are in the trace itself.
        /// </summary>
        public void WriteHeader(string mapId, bool hasSettings, in PredictionSettings s, bool motor3D)
        {
            this.sb.Clear();
            this.sb.Append("{\"kind\":\"header\",\"schema\":\"").Append(Schema).Append('"')
                .Append(",\"map\":").Append(EntityLifecycle.Quote(mapId))
                .Append(",\"settings_read\":").Append(hasSettings ? "true" : "false");
            if (hasSettings)
            {
                this.sb.Append(",\"tick_rate\":").Append(s.TickRate.ToString(Inv))
                    .Append(",\"speed\":").Append(s.Speed.ToString("R", Inv))
                    .Append(",\"bounds\":[").Append(s.Bounds.MinX.ToString("R", Inv))
                    .Append(',').Append(s.Bounds.MinY.ToString("R", Inv))
                    .Append(',').Append(s.Bounds.MaxX.ToString("R", Inv))
                    .Append(',').Append(s.Bounds.MaxY.ToString("R", Inv)).Append(']')
                    .Append(",\"tick_rate_is_fallback\":").Append(s.TickRateIsFallback ? "true" : "false");
            }

            this.sb.Append(",\"motor_3d\":").Append(motor3D ? "true" : "false").Append('}');
            this.Emit();
        }

        /// <summary>One line per join: what the server selected for this session.</summary>
        public void WriteSession(string mapId, uint protocol, uint serverTickRate, bool motor3D, int predictorTickRate)
        {
            this.sb.Clear();
            this.sb.Append("{\"kind\":\"session\",\"map\":").Append(EntityLifecycle.Quote(mapId))
                .Append(",\"protocol\":").Append(protocol.ToString(Inv))
                .Append(",\"server_tick_rate\":").Append(serverTickRate.ToString(Inv))
                .Append(",\"motor_3d\":").Append(motor3D ? "true" : "false")
                .Append(",\"predictor_tick_rate\":").Append(predictorTickRate.ToString(Inv)).Append('}');
            this.Emit();
        }

        /// <summary>
        /// The frame marker, written before any of the frame's predictor calls: realtime, the unscaled
        /// delta and the <c>Time.deltaTime</c> the DOTS world advances by.
        /// </summary>
        public void WriteFrame(int frame, double realtime, double unscaledDeltaTime, double deltaTime)
        {
            this.Frames++;
            this.sb.Clear();
            this.sb.Append("{\"frame\":").Append(frame)
                .Append(",\"t\":").Append(realtime.ToString("R", Inv))
                .Append(",\"udt\":").Append(unscaledDeltaTime.ToString("R", Inv))
                .Append(",\"dt\":").Append(deltaTime.ToString("R", Inv)).Append('}');
            this.Emit();
        }

        private void OnEvent(in PredictorTraceEvent e)
        {
            if (this.Closed)
            {
                return;
            }

            this.Events++;
            this.sb.Clear();
            e.AppendJson(this.sb);
            this.Emit();
        }

        private void Emit()
        {
            if (this.Closed)
            {
                return;
            }

            int n = this.sb.Length;
            if (n > this.chars.Length)
            {
                this.chars = new char[Math.Max(n, this.chars.Length * 2)];
            }

            this.sb.CopyTo(0, this.chars, 0, n);
            this.writer.Write(this.chars, 0, n);
            this.writer.WriteLine();
        }
    }
}
