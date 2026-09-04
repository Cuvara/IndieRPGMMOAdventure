namespace Scripts.Benchmark
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;

    /// <summary>
    /// Command-line overrides for a benchmark run. Desktop only in practice: arguments given
    /// to <c>adb shell am start</c> never reach <c>Environment.GetCommandLineArgs()</c>, so
    /// on Android the <see cref="BenchmarkConfig"/> asset is the configuration.
    /// </summary>
    /// <remarks>
    /// Pure static functions over a caller-supplied <c>string[]</c> — the same shape as
    /// <c>FrameRateCap.ResolveFromCommandLine</c>, and testable without launching a player.
    /// Malformed values fall back rather than throw: a bad launch argument must not stop a
    /// measurement run on a machine nobody is watching.
    /// <para>Flags:</para>
    /// <list type="bullet">
    /// <item><c>-benchWarmup 15</c> — warm-up seconds.</item>
    /// <item><c>-benchSettle 3</c> — per-phase settle seconds.</item>
    /// <item><c>-benchPhases 250:30,500:30,1000:30</c> — ramp as count:seconds pairs.</item>
    /// <item><c>-benchNoQuit</c> — keep the player alive after the run.</item>
    /// </list>
    /// </remarks>
    public static class BenchmarkArgs
    {
        public const string WarmupFlag = "-benchWarmup";
        public const string SettleFlag = "-benchSettle";
        public const string PhasesFlag = "-benchPhases";
        public const string NoQuitFlag = "-benchNoQuit";

        /// <summary>Reads <c>flag value</c> as a float; <paramref name="fallback"/> when absent or malformed.</summary>
        public static float ResolveFloat(string[] args, string flag, float fallback)
        {
            var raw = ResolveValue(args, flag);
            return raw != null &&
                   float.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) &&
                   parsed >= 0f
                ? parsed
                : fallback;
        }

        /// <summary>True when <paramref name="flag"/> appears anywhere in <paramref name="args"/>.</summary>
        public static bool HasFlag(string[] args, string flag)
        {
            if (args == null)
            {
                return false;
            }

            for (var i = 0; i < args.Length; i++)
            {
                if (string.Equals(args[i], flag, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Parses a ramp spec like <c>"250:30,500:30,1000:30"</c> (entityCount:seconds per
        /// step). Returns <paramref name="fallback"/> when the flag is absent or ANY step is
        /// malformed — a half-parsed ramp would silently measure the wrong workload, which is
        /// worse than measuring the configured one.
        /// </summary>
        public static BenchmarkPhase[] ResolvePhases(string[] args, BenchmarkPhase[] fallback)
        {
            var raw = ResolveValue(args, PhasesFlag);
            if (raw == null)
            {
                return fallback;
            }

            var steps = raw.Split(',');
            var phases = new List<BenchmarkPhase>(steps.Length);
            foreach (var step in steps)
            {
                var parts = step.Split(':');
                if (parts.Length != 2 ||
                    !int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var count) ||
                    !float.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds) ||
                    count < 0 || seconds <= 0f)
                {
                    return fallback;
                }

                phases.Add(new BenchmarkPhase($"ramp-{count}", count, seconds));
            }

            return phases.Count > 0 ? phases.ToArray() : fallback;
        }

        private static string ResolveValue(string[] args, string flag)
        {
            if (args == null)
            {
                return null;
            }

            for (var i = 0; i < args.Length - 1; i++)
            {
                if (string.Equals(args[i], flag, StringComparison.OrdinalIgnoreCase) &&
                    !string.IsNullOrEmpty(args[i + 1]))
                {
                    return args[i + 1];
                }
            }

            return null;
        }
    }
}
