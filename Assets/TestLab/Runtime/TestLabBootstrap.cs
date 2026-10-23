namespace TestLab
{
    using System;
    using System.IO;
    using UnityEngine;

    /// <summary>
    /// Starts a <see cref="TestLabRunner"/> when, and only when, the player was launched with
    /// <c>-testlab-plan &lt;plan.json&gt; -testlab-out &lt;dir&gt;</c> (and optionally
    /// <c>-testlab-control &lt;control.json&gt;</c>). Without those arguments it does nothing, so a
    /// testlab build played by hand behaves like the normal game.
    /// </summary>
    public static class TestLabBootstrap
    {
        public const string PlanArg = "-testlab-plan";
        public const string OutArg = "-testlab-out";
        public const string ControlArg = "-testlab-control";

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Start()
        {
            string[] args = Environment.GetCommandLineArgs();
            string planPath = ReadArg(args, PlanArg);
            if (string.IsNullOrEmpty(planPath))
            {
                return;
            }

            string outDir = ReadArg(args, OutArg) ?? Path.Combine(Application.persistentDataPath, "testlab");
            string controlPath = ReadArg(args, ControlArg);

            TestLabPlan plan;
            try
            {
                plan = TestLabPlan.Parse(File.ReadAllText(planPath));
            }
            catch (Exception ex)
            {
                // Refuse loudly: a runner that silently fell back to an idle client would make
                // every scenario read as "nothing moved, nothing went wrong".
                Debug.LogError($"[TestLab] cannot load plan '{planPath}': {ex.Message}");
                Directory.CreateDirectory(outDir);
                File.WriteAllText(Path.Combine(outDir, "status.json"),
                    "{\"phase\":\"error\",\"error\":\"plan: " + ex.Message.Replace("\"", "'") + "\"}");
                Application.Quit(3);
                return;
            }

            var go = new GameObject("TestLabRunner");
            UnityEngine.Object.DontDestroyOnLoad(go);
            go.AddComponent<TestLabRunner>().Configure(plan, outDir, controlPath);
            Debug.Log($"[TestLab] runner started: scenario={plan.scenario_id} client={plan.client_index} out={outDir}");
        }

        internal static string ReadArg(string[] args, string name)
        {
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (string.Equals(args[i], name, StringComparison.Ordinal))
                {
                    return args[i + 1];
                }
            }

            return null;
        }
    }
}
