namespace Scripts.Gameplay.Presentation
{
    /// <summary>
    /// A remote entity's height (wire <c>z</c>) over the last few snapshot ticks, evaluated at
    /// the render tick the plane position is interpolated at — so a jumping remote player rises
    /// in step with its ground-plane motion instead of a full interpolation delay ahead of it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The ground-plane interpolation is the DOTS adapter's (com.cuvara.dots 0.29.0), which does
    /// not carry height. This is the height half, kept on the client until the adapter does: a
    /// four-sample ring of <c>(tick, z)</c>, linear between the samples bracketing the render
    /// tick, clamped (never extrapolated) outside them — the same rule the adapter applies to
    /// x/y, so the two halves agree.
    /// </para>
    /// <para>A struct with inline fields: no allocation per entity per frame.</para>
    /// </remarks>
    public struct HeightTrack
    {
        public const int Capacity = 4;

        private long t0, t1, t2, t3;
        private float z0, z1, z2, z3;
        private int count;

        /// <summary>Samples held, 0..<see cref="Capacity"/>.</summary>
        public int Count => this.count;

        /// <summary>The newest sample's tick, or long.MinValue when empty.</summary>
        public long NewestTick => this.count == 0 ? long.MinValue : this.TickAt(this.count - 1);

        /// <summary>
        /// Adds the height a snapshot at <paramref name="tick"/> reported. A tick not newer than the
        /// newest held replaces that sample's value only when it is the same tick (a re-delivery);
        /// an older one is ignored.
        /// </summary>
        public void Push(long tick, float z)
        {
            if (this.count > 0)
            {
                var newest = this.TickAt(this.count - 1);
                if (tick < newest) return;
                if (tick == newest)
                {
                    this.Set(this.count - 1, tick, z);
                    return;
                }
            }

            if (this.count == Capacity)
            {
                // Shift out the oldest.
                this.t0 = this.t1; this.z0 = this.z1;
                this.t1 = this.t2; this.z1 = this.z2;
                this.t2 = this.t3; this.z2 = this.z3;
                this.count--;
            }

            this.Set(this.count, tick, z);
            this.count++;
        }

        /// <summary>
        /// The height at <paramref name="renderTick"/> (a fractional server tick): linear between
        /// the bracketing samples, the oldest before them, the newest after them, 0 when empty.
        /// </summary>
        public float Evaluate(double renderTick)
        {
            if (this.count == 0) return 0f;
            if (this.count == 1 || renderTick <= this.TickAt(0)) return this.ZAt(0);

            for (var i = 1; i < this.count; i++)
            {
                var tb = this.TickAt(i);
                if (renderTick <= tb)
                {
                    var ta = this.TickAt(i - 1);
                    var za = this.ZAt(i - 1);
                    var zb = this.ZAt(i);
                    var span = (double)(tb - ta);
                    var t = span > 0d ? (renderTick - ta) / span : 1d;
                    return (float)(za + (zb - za) * t);
                }
            }

            return this.ZAt(this.count - 1);
        }

        /// <summary>Forgets every sample (a reconnect, a new session).</summary>
        public void Clear() => this.count = 0;

        private long TickAt(int i) => i == 0 ? this.t0 : i == 1 ? this.t1 : i == 2 ? this.t2 : this.t3;

        private float ZAt(int i) => i == 0 ? this.z0 : i == 1 ? this.z1 : i == 2 ? this.z2 : this.z3;

        private void Set(int i, long tick, float z)
        {
            switch (i)
            {
                case 0: this.t0 = tick; this.z0 = z; break;
                case 1: this.t1 = tick; this.z1 = z; break;
                case 2: this.t2 = tick; this.z2 = z; break;
                default: this.t3 = tick; this.z3 = z; break;
            }
        }
    }
}
