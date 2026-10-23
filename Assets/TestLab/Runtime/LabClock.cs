namespace TestLab
{
    using System.Diagnostics;

    /// <summary>
    /// Microseconds since player start on a monotonic clock, callable from any thread.
    /// </summary>
    /// <remarks>
    /// <para><c>Time.realtimeSinceStartupAsDouble</c> is the clock every other testlab file uses,
    /// but it is main-thread only and its resolution is not documented. This reads
    /// <see cref="Stopwatch"/> and is anchored to that clock once, by <see cref="Anchor"/>, so a
    /// value here divided by 1e6 lands on the same axis as the <c>t</c> columns of
    /// <c>events.ndjson</c>, <c>samples.ndjson</c> and <c>frames.csv</c> (to within the anchor
    /// call's own duration).</para>
    /// <para>Never wall-clock: this host's clock runs fast (rpg-mmo-server#153).</para>
    /// </remarks>
    public static class LabClock
    {
        private static long originTimestamp = Stopwatch.GetTimestamp();
        private static long originUs;

        /// <summary>
        /// Pins "now" to <paramref name="realtimeSinceStartupSeconds"/>. Call once, on the main
        /// thread, before the first <see cref="NowUs"/> that is written anywhere.
        /// </summary>
        public static void Anchor(double realtimeSinceStartupSeconds)
        {
            originTimestamp = Stopwatch.GetTimestamp();
            originUs = (long)(realtimeSinceStartupSeconds * 1_000_000.0);
        }

        /// <summary>Microseconds since player start.</summary>
        public static long NowUs() => originUs + ToUs(Stopwatch.GetTimestamp() - originTimestamp);

        internal static long ToUs(long stopwatchTicks) =>
            (long)(stopwatchTicks * (1_000_000.0 / Stopwatch.Frequency));
    }
}
