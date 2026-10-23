namespace TestLab
{
    using System;

    /// <summary>
    /// Frame-time distribution of a run in fixed memory: a histogram for the whole run and a ring
    /// of the most recent frames for the overlay. Nothing grows with the run's length.
    /// </summary>
    /// <remarks>
    /// <para><b>Why not every sample.</b> This class used to keep every frame time and sort a copy
    /// for each percentile. Once a second that was a main-thread stall that grew with the run - about
    /// 200 ms per second by the end of a one-hour soak (rpg-factory testlab soak
    /// 20261022T112220Z-S09-eeoa) - which contaminated the very frame times it reported.</para>
    /// <para><b>Whole-run percentiles</b> (<see cref="Percentile"/>, for <c>summary.json</c>) are
    /// nearest-rank over <see cref="BucketMs"/>-wide buckets up to <see cref="RangeMs"/>, plus
    /// one overflow bucket. Each bucket remembers its largest sample, and that is what is returned:
    /// the reported value is a real frame time, never below the exact nearest-rank value and at most
    /// one bucket (0.1 ms) above it. In the overflow bucket (frames of 1 s or more) it is the
    /// largest such frame. <see cref="Mean"/>, <see cref="Max"/>, <see cref="Count"/> and
    /// <see cref="LongFrames"/> stay exact.</para>
    /// <para><b>Recent percentiles</b> (<see cref="RecentPercentile"/>, for the overlay) are exact
    /// over the last <see cref="WindowFrames"/> frames: a fixed copy and an in-place selection in a
    /// preallocated buffer, so its cost is the same in minute 1 and minute 60 and it allocates nothing.</para>
    /// </remarks>
    public sealed class FrameStats
    {
        public const double BucketMs = 0.1;
        public const double RangeMs = 1000.0;
        public const int WindowFrames = 600;

        private static readonly int Buckets = (int)Math.Round(RangeMs / BucketMs);

        private readonly long[] counts = new long[Buckets + 1];
        private readonly double[] bucketMax = new double[Buckets + 1];
        private readonly double[] ring = new double[WindowFrames];
        private readonly double[] scratch = new double[WindowFrames];
        private int ringCount;
        private int ringNext;
        private double sum;
        private double max = double.NaN;

        public long Count { get; private set; }

        public int LongFrames { get; private set; }

        /// <summary>Frame time above which a frame counts as long (two 60 Hz frames).</summary>
        public double LongFrameMs { get; set; } = 33.4;

        /// <summary>
        /// Doubles and longs this instance holds. A constant: the test that guards against
        /// growth with run length reads it.
        /// </summary>
        public int RetainedValues => this.counts.Length + this.bucketMax.Length + this.ring.Length + this.scratch.Length;

        public void Add(double ms)
        {
            if (double.IsNaN(ms))
            {
                return;
            }

            this.Count++;
            this.sum += ms;
            if (double.IsNaN(this.max) || ms > this.max)
            {
                this.max = ms;
            }

            if (ms > this.LongFrameMs)
            {
                this.LongFrames++;
            }

            int bucket = BucketOf(ms);
            if (this.counts[bucket] == 0 || ms > this.bucketMax[bucket])
            {
                this.bucketMax[bucket] = ms;
            }

            this.counts[bucket]++;

            this.ring[this.ringNext] = ms;
            this.ringNext = (this.ringNext + 1) % WindowFrames;
            if (this.ringCount < WindowFrames)
            {
                this.ringCount++;
            }
        }

        /// <summary>
        /// Whole-run nearest-rank percentile in milliseconds, at bucket resolution (see remarks);
        /// NaN with no samples. Constant cost: one pass over the fixed bucket array.
        /// </summary>
        public double Percentile(double p)
        {
            if (this.Count == 0)
            {
                return double.NaN;
            }

            long rank = Rank(p, this.Count);
            long seen = 0;
            for (int i = 0; i < this.counts.Length; i++)
            {
                seen += this.counts[i];
                if (seen >= rank)
                {
                    return this.bucketMax[i];
                }
            }

            return this.max;
        }

        /// <summary>Exact nearest-rank percentile of the last <see cref="WindowFrames"/> frames; NaN with none.</summary>
        public double RecentPercentile(double p)
        {
            if (this.ringCount == 0)
            {
                return double.NaN;
            }

            Array.Copy(this.ring, this.scratch, this.ringCount);
            return Select(this.scratch, this.ringCount, (int)Rank(p, this.ringCount) - 1);
        }

        /// <summary>
        /// The k-th smallest of <c>a[0..n)</c> (0-based), reordering <paramref name="a"/> in place.
        /// Hand-written quickselect rather than <c>Array.Sort</c>: the library sort may allocate a
        /// comparer on some runtimes, and this path must allocate nothing.
        /// </summary>
        private static double Select(double[] a, int n, int k)
        {
            int lo = 0;
            int hi = n - 1;
            while (lo < hi)
            {
                double pivot = a[lo + ((hi - lo) / 2)];
                int i = lo;
                int j = hi;
                while (i <= j)
                {
                    while (a[i] < pivot)
                    {
                        i++;
                    }

                    while (a[j] > pivot)
                    {
                        j--;
                    }

                    if (i <= j)
                    {
                        double tmp = a[i];
                        a[i] = a[j];
                        a[j] = tmp;
                        i++;
                        j--;
                    }
                }

                if (k <= j)
                {
                    hi = j;
                }
                else if (k >= i)
                {
                    lo = i;
                }
                else
                {
                    return a[k];
                }
            }

            return a[k];
        }

        public double Max() => this.max;

        public double Mean() => this.Count == 0 ? double.NaN : this.sum / this.Count;

        private static int BucketOf(double ms)
        {
            if (ms <= 0)
            {
                return 0;
            }

            double index = ms / BucketMs;
            return index >= Buckets ? Buckets : (int)index;
        }

        private static long Rank(double p, long n)
        {
            long rank = (long)Math.Ceiling(p / 100.0 * n);
            return Math.Min(Math.Max(rank, 1), n);
        }
    }
}
