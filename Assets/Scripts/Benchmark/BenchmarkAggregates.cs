namespace Scripts.Benchmark
{
    using System;

    /// <summary>
    /// The reduced statistics for one set of frames — the whole run, or one phase.
    /// Serializable for <c>UnityEngine.JsonUtility</c>; every field lands in the result JSON.
    /// </summary>
    [Serializable]
    public sealed class BenchmarkAggregates
    {
        /// <summary>Frames that entered the aggregate after warm-up/settle exclusion.</summary>
        public int FrameCount;

        public float MeanMs;
        public float MedianMs;
        public float P95Ms;
        public float P99Ms;
        public float MaxMs;

        /// <summary>Frames divided by wall-clock seconds spanned — the honest average.</summary>
        public float AverageFps;

        /// <summary>Mean GPU frame ms over frames that had timing data; 0 when none did.</summary>
        public float GpuMeanMs;

        /// <summary>Total managed bytes allocated across the aggregated frames.</summary>
        public long GcAllocatedTotalBytes;

        /// <summary>
        /// Median managed bytes allocated per frame — the steady-state figure. The median,
        /// not the mean: one loading hitch would drag a mean while the typical frame is the
        /// number that decides whether the game ever stops triggering collections.
        /// </summary>
        public long GcAllocatedPerFrameMedianBytes;

        /// <summary>Total managed allocation count across the aggregated frames.</summary>
        public long GcAllocationCountTotal;

        /// <summary>GC collections (all generations) that completed inside the aggregate.</summary>
        public int GcCollections;

        /// <summary>Frames during which at least one GC collection ran — the spike count.</summary>
        public int GcSpikeFrames;
    }
}
