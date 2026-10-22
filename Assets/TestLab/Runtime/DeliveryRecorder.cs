namespace TestLab
{
    using System;
    using System.Globalization;
    using System.IO;

    /// <summary>
    /// Snapshot delivery and render age, streamed to two CSV files (contract C2,
    /// <c>docs/TESTLAB.md</c> "Delivery instrumentation"): one row per received snapshot in
    /// <see cref="DeliveryFile"/>, one row per rendered frame in <see cref="RenderFile"/>.
    /// </summary>
    /// <remarks>
    /// Bounded for a soak of any length: each call writes one row and updates a fixed number of
    /// fields; nothing is retained per snapshot or per frame, and no call does work proportional
    /// to the run's history (a one-hour soak was once contaminated by an overlay that re-sorted
    /// every frame time, rpg-factory testlab run 20261022T100043Z-S09-zo1i). Unknown values are
    /// written as empty fields, never as zero.
    /// </remarks>
    public sealed class DeliveryRecorder : IDisposable
    {
        public const string Schema = "rpg.testlab.delivery.v2";
        public const string DeliveryFile = "delivery.csv";
        public const string RenderFile = "render.csv";
        public const string DeliveryHeader = "world_tick,recv_us,applied_us,bytes,full,entities,frame";
        public const string RenderHeader = "frame,t_us,render_tick,clock_newest_tick,newest_tick,newest_recv_us,snapshot_age_ms,clock_source";

        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        private TextWriter delivery;
        private TextWriter render;

        private long newestTick = -1;
        private long newestRecvUs = -1;

        public DeliveryRecorder(TextWriter delivery, TextWriter render)
        {
            this.delivery = delivery ?? throw new ArgumentNullException(nameof(delivery));
            this.render = render ?? throw new ArgumentNullException(nameof(render));
            this.delivery.WriteLine(DeliveryHeader);
            this.render.WriteLine(RenderHeader);
        }

        public long Snapshots { get; private set; }

        public long SnapshotsWithBytes { get; private set; }

        public long Frames { get; private set; }

        /// <summary>Newest server tick received in this session, or -1.</summary>
        public long NewestTick => this.newestTick;

        /// <summary>
        /// One received snapshot, after it was merged into the client's world state.
        /// </summary>
        /// <param name="recvUs">When its frame left the transport, or -1 when the tap is absent.</param>
        /// <param name="appliedUs">When <c>WorldState.Apply</c> had merged it.</param>
        /// <param name="bytes">Frame body length, or -1 when the tap is absent.</param>
        /// <param name="frame">The <see cref="RenderFile"/> frame index that can first show it.</param>
        public void OnSnapshot(long worldTick, long recvUs, long appliedUs, int bytes, bool full, int entities, int frame)
        {
            this.Snapshots++;
            if (bytes >= 0)
            {
                this.SnapshotsWithBytes++;
            }

            if (worldTick >= this.newestTick)
            {
                this.newestTick = worldTick;
                this.newestRecvUs = recvUs >= 0 ? recvUs : appliedUs;
            }

            var w = this.delivery;
            if (w == null)
            {
                return;
            }

            w.Write(worldTick.ToString(Inv));
            w.Write(',');
            WriteOptional(w, recvUs);
            w.Write(',');
            w.Write(appliedUs.ToString(Inv));
            w.Write(',');
            WriteOptional(w, bytes);
            w.Write(',');
            w.Write(full ? '1' : '0');
            w.Write(',');
            w.Write(entities.ToString(Inv));
            w.Write(',');
            w.WriteLine(frame.ToString(Inv));
        }

        /// <summary>
        /// One rendered frame. <paramref name="hasClock"/> is false until a render clock is
        /// readable; <c>render_tick</c>, <c>clock_newest_tick</c> and <c>clock_source</c> are then
        /// empty. <paramref name="clockSource"/> names the clock: <c>binder</c> (the shipping
        /// client's <c>WorldViewBinder</c>) or <c>ecs</c> (the ECS <c>InterpolationTimeline</c>).
        /// </summary>
        public void OnFrame(int frame, long nowUs, bool hasClock, double renderTick, long clockNewestTick, string clockSource)
        {
            this.Frames++;
            var w = this.render;
            if (w == null)
            {
                return;
            }

            w.Write(frame.ToString(Inv));
            w.Write(',');
            w.Write(nowUs.ToString(Inv));
            w.Write(',');
            if (hasClock && !double.IsNaN(renderTick) && !double.IsInfinity(renderTick))
            {
                w.Write(renderTick.ToString("R", Inv));
            }

            w.Write(',');
            if (hasClock)
            {
                w.Write(clockNewestTick.ToString(Inv));
            }

            w.Write(',');
            WriteOptional(w, this.newestTick);
            w.Write(',');
            WriteOptional(w, this.newestRecvUs);
            w.Write(',');
            if (this.newestRecvUs >= 0)
            {
                w.Write(((nowUs - this.newestRecvUs) / 1000.0).ToString("0.###", Inv));
            }

            w.Write(',');
            if (hasClock)
            {
                w.Write(clockSource);
            }

            w.WriteLine();
        }

        /// <summary>
        /// A new session (reconnect or a different server) starts its own tick sequence, so the
        /// newest-tick bookkeeping restarts; rows already written are untouched.
        /// </summary>
        public void ResetSession()
        {
            this.newestTick = -1;
            this.newestRecvUs = -1;
        }

        public void Flush()
        {
            this.delivery?.Flush();
            this.render?.Flush();
        }

        public void Dispose()
        {
            this.delivery?.Dispose();
            this.render?.Dispose();
            this.delivery = null;
            this.render = null;
        }

        private static void WriteOptional(TextWriter w, long value)
        {
            if (value >= 0)
            {
                w.Write(value.ToString(Inv));
            }
        }
    }
}
