namespace Scripts.Benchmark
{
    using System;

    /// <summary>
    /// One ramp phase's identity plus its <see cref="BenchmarkAggregates"/>, as it appears in
    /// the result JSON. The boundary time is recorded so a reader can correlate the phase
    /// against the per-second <see cref="MemorySample"/> series.
    /// </summary>
    [Serializable]
    public sealed class BenchmarkPhaseResult
    {
        public string Label;
        public int EntityCount;

        /// <summary>Seconds since recording started at which this phase began.</summary>
        public float StartTimeSeconds;

        /// <summary>Configured duration of the phase.</summary>
        public float ConfiguredSeconds;

        public BenchmarkAggregates Aggregates;
    }
}
