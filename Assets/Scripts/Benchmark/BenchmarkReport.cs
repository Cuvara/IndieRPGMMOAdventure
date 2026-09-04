namespace Scripts.Benchmark
{
    using System;

    /// <summary>
    /// The whole run, as one <c>JsonUtility</c>-serializable object: environment, run
    /// parameters, overall aggregates, per-phase aggregates, and the per-second memory
    /// series. This is exactly what lands in the output file and on the
    /// <c>[BENCH-RESULT]</c> log line.
    /// </summary>
    [Serializable]
    public sealed class BenchmarkReport
    {
        // ---- run identity
        /// <summary>Caller-chosen run label (<c>-bench-label</c>); empty for scene-driven runs.</summary>
        public string Label;

        /// <summary>Rolling-window index (0-based); always 0 for a single-shot run.</summary>
        public int WindowIndex;

        // ---- environment: enough to never wonder "which phone was this?" again.
        public string Scene;
        public string DeviceModel;
        public string OperatingSystem;
        public string GraphicsDevice;
        public int SystemMemoryMb;
        public string UnityVersion;
        public string StartedAtUtc;
        public bool DevelopmentBuild;

        // ---- run parameters, echoed so a result file is self-describing.
        public float WarmupSeconds;
        public float SettleSeconds;

        /// <summary>True when the sample buffer filled before the run ended — the run outpaced
        /// <see cref="BenchmarkConfig.MaxExpectedFps"/>; aggregates then cover only the
        /// captured prefix and must be read with that caveat.</summary>
        public bool Truncated;

        public int TotalFrames;
        public float TotalSeconds;

        public BenchmarkAggregates Overall;
        public BenchmarkPhaseResult[] Phases;
        public MemorySample[] Memory;
    }
}
