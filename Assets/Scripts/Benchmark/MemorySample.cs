namespace Scripts.Benchmark
{
    using System;

    /// <summary>
    /// A once-per-second snapshot of process memory and (when a DOTS world exists) the live
    /// entity count. Serialized into the result JSON — at one row per second the series stays
    /// small enough to carry whole, and a memory leak only shows in the series, never in an
    /// aggregate.
    /// </summary>
    [Serializable]
    public struct MemorySample
    {
        /// <summary>Seconds since recording started.</summary>
        public float TimeSeconds;

        /// <summary>"Total Reserved Memory" — all Unity-tracked reserved bytes.</summary>
        public long TotalReservedBytes;

        /// <summary>"System Used Memory" — the OS's view of the process; 0 where unsupported.</summary>
        public long SystemUsedBytes;

        /// <summary>"GC Used Memory" — live managed heap bytes.</summary>
        public long GcUsedBytes;

        /// <summary>"GC Reserved Memory" — managed heap reserved bytes.</summary>
        public long GcReservedBytes;

        /// <summary>Entities alive in the default world; -1 when no world exists.</summary>
        public int EntityCount;
    }
}
