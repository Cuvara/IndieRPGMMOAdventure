namespace TestLab
{
    using System.Globalization;

    /// <summary>
    /// The <c>frames.csv</c> row. The first four columns are the original schema and keep their order
    /// and names; the four after them are appended and are empty when their source is unavailable.
    /// </summary>
    /// <remarks>
    /// <c>gc_alloc_b</c> is the Profiler "GC Allocated In Frame" value for the frame; <c>gc_count</c> is the
    /// cumulative <c>GC.CollectionCount(0)</c> (the analyzer takes deltas, so a collection shows as a step);
    /// <c>main_ms</c> is the Profiler "Main Thread" frame time; <c>used_mb</c> is "Total Used Memory" in MiB.
    /// A negative argument means "no value" and writes an empty field.
    /// </remarks>
    public static class FrameCsv
    {
        public const string Header = "frame,t,dt_ms,scenario_t,gc_alloc_b,gc_count,main_ms,used_mb";

        private const double BytesPerMb = 1024.0 * 1024.0;
        private const double NanosPerMs = 1e6;

        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        public static string Row(int frame, string t, string dtMs, string scenarioT, long gcAllocBytes, int gcCount,
            long mainThreadNs, long usedBytes)
        {
            return frame.ToString(Inv) + "," + t + "," + dtMs + "," + scenarioT + "," +
                   (gcAllocBytes >= 0 ? gcAllocBytes.ToString(Inv) : string.Empty) + "," +
                   (gcCount >= 0 ? gcCount.ToString(Inv) : string.Empty) + "," +
                   (mainThreadNs > 0 ? (mainThreadNs / NanosPerMs).ToString("0.###", Inv) : string.Empty) + "," +
                   (usedBytes >= 0 ? (usedBytes / BytesPerMb).ToString("0.##", Inv) : string.Empty);
        }
    }
}
