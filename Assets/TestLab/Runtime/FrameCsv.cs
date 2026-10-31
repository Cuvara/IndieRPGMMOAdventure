namespace TestLab
{
    using System;
    using System.Globalization;
    using System.Text;

    /// <summary>
    /// The <c>frames.csv</c> row. The first four columns are the original schema and keep their order
    /// and names; the four after them are appended and are empty when their source is unavailable.
    /// </summary>
    /// <remarks>
    /// <c>gc_alloc_b</c> is the Profiler "GC Allocated In Frame" value for the frame; <c>gc_count</c> is the
    /// cumulative <c>GC.CollectionCount(0)</c> (the analyzer takes deltas, so a collection shows as a step);
    /// <c>main_ms</c> is the Profiler "Main Thread" frame time; <c>used_mb</c> is "Total Used Memory" in MiB.
    /// A negative argument means "no value" and writes an empty field. One reusable builder and number buffer
    /// keep each call to the single result-string allocation handed to <see cref="BackgroundFileWriter"/>.
    /// </remarks>
    public sealed class FrameCsv
    {
        public const string Header = "frame,t,dt_ms,scenario_t,gc_alloc_b,gc_count,main_ms,used_mb";

        private const double BytesPerMb = 1024.0 * 1024.0;
        private const double NanosPerMs = 1e6;

        private readonly StringBuilder builder = new StringBuilder(128);
        private readonly char[] numberBuffer = new char[32];

        public string Row(int frame, string t, string dtMs, string scenarioT, long gcAllocBytes, int gcCount,
            long mainThreadNs, long usedBytes)
        {
            this.builder.Clear();
            this.builder.Append(frame).Append(',').Append(t).Append(',').Append(dtMs).Append(',').Append(scenarioT).Append(',');

            if (gcAllocBytes >= 0)
            {
                this.builder.Append(gcAllocBytes);
            }

            this.builder.Append(',');
            if (gcCount >= 0)
            {
                this.builder.Append(gcCount);
            }

            this.builder.Append(',');
            if (mainThreadNs > 0)
            {
                this.AppendFormatted(mainThreadNs / NanosPerMs, "0.###");
            }

            this.builder.Append(',');
            if (usedBytes >= 0)
            {
                this.AppendFormatted(usedBytes / BytesPerMb, "0.##");
            }

            return this.builder.ToString();
        }

        private void AppendFormatted(double value, string format)
        {
            if (!value.TryFormat(this.numberBuffer.AsSpan(), out int charsWritten, format, CultureInfo.InvariantCulture))
            {
                throw new FormatException("Frame CSV numeric buffer was too small.");
            }

            this.builder.Append(this.numberBuffer, 0, charsWritten);
        }
    }
}
