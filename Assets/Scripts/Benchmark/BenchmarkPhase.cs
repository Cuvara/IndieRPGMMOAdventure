namespace Scripts.Benchmark
{
    using System;
    using UnityEngine;

    /// <summary>
    /// One step of the benchmark's ramp profile: hold <see cref="EntityCount"/> live entities
    /// for <see cref="Seconds"/>.
    /// </summary>
    /// <remarks>
    /// A plain serializable class rather than a struct so it round-trips through both a
    /// <see cref="BenchmarkConfig"/> asset and <c>JsonUtility</c> without custom code. The
    /// entity count is a request to whatever workload is listening
    /// (<c>BenchmarkWorkload</c> in the benchmark scene); the recorder itself only uses the
    /// label and duration, so the recorder stays usable in scenes with no workload at all.
    /// </remarks>
    [Serializable]
    public sealed class BenchmarkPhase
    {
        [Tooltip("Name of this phase as it appears in the result JSON.")]
        public string Label = "phase";

        [Tooltip("Entities the workload should keep alive during this phase.")]
        public int EntityCount;

        [Tooltip("How long this phase runs, in seconds.")]
        public float Seconds = 30f;

        public BenchmarkPhase()
        {
        }

        public BenchmarkPhase(string label, int entityCount, float seconds)
        {
            this.Label = label;
            this.EntityCount = entityCount;
            this.Seconds = seconds;
        }
    }
}
