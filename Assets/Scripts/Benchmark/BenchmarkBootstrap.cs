namespace Scripts.Benchmark
{
    using System;
    using UnityEngine;

    /// <summary>
    /// Any-scene activation: launch the player with <c>-bench</c> and a
    /// <see cref="BenchmarkRecorder"/> is spawned into whatever scene boots — the netcode
    /// DOTS sample included — with no scene edit and no benchmark-specific build.
    /// </summary>
    /// <remarks>
    /// <para>Flags (all read once, at startup):</para>
    /// <list type="bullet">
    /// <item><c>-bench</c> — activate. Absent, this class does nothing at all.</item>
    /// <item><c>-bench-duration N</c> — seconds per measurement window (default 60).</item>
    /// <item><c>-bench-warmup N</c> — warm-up before the first window (default 10).</item>
    /// <item><c>-bench-label text</c> — run identification, echoed as <c>Label</c> in the
    /// JSON; how three simultaneous instances stay distinguishable.</item>
    /// <item><c>-bench-quit</c> — write ONE window and quit. Without it the recorder rolls:
    /// it writes/logs a report at every window boundary (each tagged with
    /// <c>WindowIndex</c>) and the player keeps running — a netcode client must stay
    /// connected while the numbers are read.</item>
    /// </list>
    /// <para>The recorder is the same instrument either way: engine/profiler counters only,
    /// no netcode reference — it merely happens to be running in a networked scene. The
    /// host object is <c>DontDestroyOnLoad</c>, so it survives whatever scene flow the game
    /// performs after boot (window reports re-read the active scene's name).</para>
    /// <para><c>AfterSceneLoad</c> so the boot scene is the one whose name lands in the
    /// first report. A scene that already hosts a recorder (the DeviceBenchmark scene) wins:
    /// two recorders would double-sample, so <c>-bench</c> then defers with a log line.</para>
    /// </remarks>
    public static class BenchmarkBootstrap
    {
        private const float DefaultDurationSeconds = 60f;
        private const float DefaultWarmupSeconds = 10f;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Activate()
        {
            var args = Environment.GetCommandLineArgs();
            if (!BenchmarkArgs.HasFlag(args, BenchmarkArgs.BenchFlag))
            {
                return;
            }

            if (UnityEngine.Object.FindAnyObjectByType<BenchmarkRecorder>() != null)
            {
                Debug.Log("[BenchmarkBootstrap] -bench given but the scene already hosts a " +
                          "BenchmarkRecorder — deferring to the scene's recorder.");
                return;
            }

            var duration = BenchmarkArgs.ResolveFloat(args, BenchmarkArgs.DurationFlag, DefaultDurationSeconds);
            var warmup = BenchmarkArgs.ResolveFloat(args, BenchmarkArgs.BootWarmupFlag, DefaultWarmupSeconds);
            var label = BenchmarkArgs.ResolveString(args, BenchmarkArgs.LabelFlag, string.Empty);
            var quit = BenchmarkArgs.HasFlag(args, BenchmarkArgs.QuitFlag);

            // Inactive first so ApplyPlan lands before OnEnable sizes the buffers — the
            // same activation order the recorder's own contract documents.
            var host = new GameObject("BenchmarkRecorder (-bench)");
            host.SetActive(false);
            UnityEngine.Object.DontDestroyOnLoad(host);

            var recorder = host.AddComponent<BenchmarkRecorder>();
            recorder.ApplyPlan(
                new[] { new BenchmarkPhase(string.IsNullOrEmpty(label) ? "window" : label, 0, duration) },
                warmup,
                planSettleSeconds: 0f,
                planAutoQuit: quit,
                rollingWindows: !quit,
                label);

            host.SetActive(true);

            Debug.Log($"[BenchmarkBootstrap] -bench active: duration={duration}s, warmup={warmup}s, " +
                      $"label='{label}', mode={(quit ? "single window then quit" : "rolling windows")}");
        }
    }
}
