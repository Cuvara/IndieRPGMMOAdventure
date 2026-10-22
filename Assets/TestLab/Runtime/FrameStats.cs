namespace TestLab
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// Frame-time distribution of a run. Keeps every sample (a 10-minute run at 60 fps is 36k
    /// doubles), so percentiles are exact rather than estimated from buckets.
    /// </summary>
    public sealed class FrameStats
    {
        private readonly List<double> frameMs = new List<double>(8192);

        public int Count => this.frameMs.Count;

        public int LongFrames { get; private set; }

        /// <summary>Frame time above which a frame counts as long (two 60 Hz frames).</summary>
        public double LongFrameMs { get; set; } = 33.4;

        public void Add(double ms)
        {
            this.frameMs.Add(ms);
            if (ms > this.LongFrameMs)
            {
                this.LongFrames++;
            }
        }

        /// <summary>Nearest-rank percentile in milliseconds; NaN with no samples.</summary>
        public double Percentile(double p)
        {
            if (this.frameMs.Count == 0)
            {
                return double.NaN;
            }

            var sorted = new List<double>(this.frameMs);
            sorted.Sort();
            int rank = (int)Math.Ceiling(p / 100.0 * sorted.Count);
            return sorted[Math.Min(Math.Max(rank, 1), sorted.Count) - 1];
        }

        public double Max()
        {
            double max = double.NaN;
            foreach (double ms in this.frameMs)
            {
                if (double.IsNaN(max) || ms > max)
                {
                    max = ms;
                }
            }

            return max;
        }

        public double Mean()
        {
            if (this.frameMs.Count == 0)
            {
                return double.NaN;
            }

            double sum = 0;
            foreach (double ms in this.frameMs)
            {
                sum += ms;
            }

            return sum / this.frameMs.Count;
        }
    }
}
