namespace Scripts.Benchmark
{
    /// <summary>
    /// One frame's raw measurements, written into a preallocated buffer by
    /// <see cref="BenchmarkRecorder"/> — an unmanaged struct so recording a frame allocates
    /// nothing (the recorder measures GC; it must not feed it).
    /// </summary>
    /// <remarks>
    /// Not serialized into the result JSON — thousands of per-frame rows would dwarf the
    /// aggregates the JSON exists to carry. <see cref="BenchmarkAggregation"/> reduces the
    /// buffer at the end of the run.
    /// </remarks>
    public struct FrameSample
    {
        /// <summary>Seconds since recording started (unscaled).</summary>
        public float TimeSeconds;

        /// <summary>Main-thread CPU frame time in milliseconds.</summary>
        public float FrameMs;

        /// <summary>GPU frame time in milliseconds; 0 when FrameTimingManager has no data.</summary>
        public float GpuMs;

        /// <summary>Managed bytes allocated during this frame ("GC Allocated In Frame").</summary>
        public long GcAllocatedBytes;

        /// <summary>Managed allocation count during this frame ("GC Allocation In Frame Count").</summary>
        public int GcAllocationCount;

        /// <summary>Cumulative GC collection count (all generations) at this frame.</summary>
        public int GcCollectionsCumulative;

        /// <summary>Index into the run's phases; -1 while inside the warm-up window.</summary>
        public int PhaseIndex;
    }
}
