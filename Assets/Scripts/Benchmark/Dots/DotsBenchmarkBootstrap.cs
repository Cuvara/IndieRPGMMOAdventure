#if CUVARA_DOTS
namespace Scripts.Benchmark.Dots
{
    using System;
    using UnityEngine;

    /// <summary>
    /// Any-scene activation for DOTS stress benchmark.
    /// <c>-stress-pure</c> or <c>-stress-hybrid</c>.
    /// </summary>
    public static class DotsBenchmarkBootstrap
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Activate()
        {
            var args = Environment.GetCommandLineArgs();
            var pure = Has(args, "-stress-pure");
            var hybrid = Has(args, "-stress-hybrid");
            if (!pure && !hybrid) return;

            if (UnityEngine.Object.FindAnyObjectByType<DotsStressBenchmark>() != null)
            {
                Debug.Log("[DotsBenchmarkBootstrap] Scene already hosts a benchmark.");
                return;
            }

            var noPhysics = Has(args, "-stress-no-physics");
            var quick = Has(args, "-stress-quick");
            var warmup = Float(args, "-stress-warmup", 3f);
            var tierSec = Float(args, "-stress-tier-seconds", 10f);
            var viewCap = (int)Float(args, "-stress-view-cap", 50000f);

            var host = new GameObject("DotsStressBenchmark");
            UnityEngine.Object.DontDestroyOnLoad(host);
            var bench = host.AddComponent<DotsStressBenchmark>();
            bench.SetPlan(tierSec, warmup, !noPhysics, hybrid, viewCap, quick);

            Debug.Log($"[DotsBenchmarkBootstrap] mode={(pure ? "pure" : "hybrid")} " +
                      $"physics={!noPhysics} quick={quick} warmup={warmup}s " +
                      $"tierSec={tierSec}s viewCap={viewCap}");
        }

        private static bool Has(string[] args, string flag)
        {
            for (int i = 0; i < args.Length; i++)
                if (string.Equals(args[i], flag, StringComparison.OrdinalIgnoreCase))
                    return true;
            return false;
        }

        private static float Float(string[] args, string flag, float fallback)
        {
            for (int i = 0; i < args.Length - 1; i++)
                if (string.Equals(args[i], flag, StringComparison.OrdinalIgnoreCase) &&
                    float.TryParse(args[i + 1], out var v))
                    return v;
            return fallback;
        }
    }
}
#endif
