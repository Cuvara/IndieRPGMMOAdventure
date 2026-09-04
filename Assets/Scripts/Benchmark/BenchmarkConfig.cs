namespace Scripts.Benchmark
{
    using UnityEngine;

    /// <summary>
    /// Authoring-time knobs for a benchmark run: warm-up, settle window, the ramp profile,
    /// and whether the player quits itself when the run completes.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A ScriptableObject rather than serialized fields on the recorder because command-line
    /// arguments do not reach an Android activity the way they reach a desktop player — on a
    /// device the asset (baked into the scene) IS the configuration, so it must be editable
    /// without touching a scene file. On desktop the <c>-bench*</c> arguments override it
    /// (see <see cref="BenchmarkArgs"/>); the asset is never mutated at runtime — the
    /// recorder copies the values into a run plan.
    /// </para>
    /// <para>Documented in <c>docs/DEVICE-BENCHMARK.md</c>.</para>
    /// </remarks>
    [CreateAssetMenu(fileName = "BenchmarkConfig", menuName = "Benchmark/Benchmark Config")]
    public sealed class BenchmarkConfig : ScriptableObject
    {
        [Tooltip("Seconds excluded from every aggregate at the start of the run — shader " +
                 "compilation, JIT/first-run warmup, pool prewarm all land here.")]
        [Min(0f)]
        public float WarmupSeconds = 10f;

        [Tooltip("Seconds excluded from a phase's aggregates after its boundary — the spawn " +
                 "burst at a ramp step is real work but not that phase's steady state.")]
        [Min(0f)]
        public float SettleSeconds = 2f;

        [Tooltip("Upper bound used to size the preallocated sample buffer. Not a cap on the " +
                 "actual frame rate; a run that outpaces it is truncated and flagged, never " +
                 "grown mid-run (growing would allocate while measuring GC).")]
        [Min(30)]
        public int MaxExpectedFps = 240;

        [Tooltip("Quit the player when the run completes. Ignored in the Editor. Turn off " +
                 "for interactive inspection; adb-driven runs want it on.")]
        public bool AutoQuit = true;

        [Tooltip("The ramp profile, run in order after the warm-up window.")]
        public BenchmarkPhase[] Phases =
        {
            new BenchmarkPhase("ramp-250", 250, 30f),
            new BenchmarkPhase("ramp-500", 500, 30f),
            new BenchmarkPhase("ramp-1000", 1000, 30f),
        };
    }
}
