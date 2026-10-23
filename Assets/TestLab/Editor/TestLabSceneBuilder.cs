namespace TestLab.Editor
{
    using System;
    using System.IO;
    using UnityEditor;
    using UnityEditor.SceneManagement;
    using UnityEngine;
    using UnityEngine.SceneManagement;

    /// <summary>
    /// Builds and validates <c>Assets/Scenes/Tests/MultiplayerCombatTest.unity</c>, the scene the
    /// rpg-factory testlab boots. The scene is <b>MainScene plus visual reference geometry</b>, and
    /// is regenerated from MainScene rather than edited by hand, so the game path under test is the
    /// production one and cannot drift from it.
    /// </summary>
    /// <remarks>
    /// <para><b>What is added.</b> One root object, <c>TestLabArena</c>: a ground grid every 5
    /// units with a marker at the origin, and, when <c>-testlabMap &lt;map.json&gt;</c> is given,
    /// a translucent mirror of the map's collision boxes (the client does not draw them; the
    /// server collides with them). It carries no scripts, so the scene opens in any build of the
    /// project, with or without <c>CUVARA_TESTLAB</c>. Everything a scenario controls - spawn
    /// points, enemy density, obstacles - is server-authoritative and set by the scenario's
    /// server configuration (map id, enemy knobs, seed), not by this scene.</para>
    ///
    /// <para><b>Batch use.</b>
    /// <c>Unity -batchmode -quit -projectPath . -executeMethod TestLab.Editor.TestLabSceneBuilder.Build
    /// [-testlabMap ../rpg-mmo-server/backend/content/maps/dev_arena.json]</c>, then
    /// <c>-executeMethod TestLab.Editor.TestLabSceneBuilder.Validate</c>, which exits non-zero on
    /// a missing script, a missing MainScene root or a missing arena.</para>
    /// </remarks>
    public static class TestLabSceneBuilder
    {
        public const string ScenePath = "Assets/Scenes/Tests/MultiplayerCombatTest.unity";
        public const string SourceScenePath = "Assets/Scenes/MainScene.unity";
        public const string ArenaName = "TestLabArena";
        public const float GridHalfExtent = 30f;
        public const float GridSpacing = 5f;

        [MenuItem("Tools/TestLab/Rebuild MultiplayerCombatTest scene")]
        public static void BuildFromMenu() => BuildScene(null);

        /// <summary>Batch entry point; see the class remarks.</summary>
        public static void Build()
        {
            string map = ReadArg("-testlabMap");
            try
            {
                BuildScene(map);
                Debug.Log("[TestLabScene] BUILD OK " + ScenePath);
            }
            catch (Exception ex)
            {
                Debug.LogError("[TestLabScene] BUILD FAILED: " + ex);
                if (Application.isBatchMode)
                {
                    EditorApplication.Exit(1);
                }
            }
        }

        /// <summary>Batch entry point: open the scene and check it; exits 0 only when every check passes.</summary>
        public static void Validate()
        {
            int failures = ValidateScene(out string report);
            Debug.Log("[TestLabScene] " + report.Replace("\n", "\n[TestLabScene] "));
            Debug.Log(failures == 0 ? "[TestLabScene] VALIDATE PASS" : $"[TestLabScene] VALIDATE FAIL ({failures})");
            if (Application.isBatchMode)
            {
                EditorApplication.Exit(failures == 0 ? 0 : 1);
            }
        }

        public static void BuildScene(string mapJsonPath)
        {
            var scene = EditorSceneManager.OpenScene(SourceScenePath, OpenSceneMode.Single);
            var arena = new GameObject(ArenaName);
            BuildGrid(arena.transform);
            if (!string.IsNullOrEmpty(mapJsonPath))
            {
                BuildBoxes(arena.transform, Path.GetFileNameWithoutExtension(mapJsonPath), File.ReadAllText(mapJsonPath));
            }

            Directory.CreateDirectory(Path.GetDirectoryName(ScenePath));
            if (!EditorSceneManager.SaveScene(scene, ScenePath, saveAsCopy: false))
            {
                throw new InvalidOperationException("SaveScene returned false for " + ScenePath);
            }

            AssetDatabase.Refresh();
        }

        /// <summary>Returns the number of failed checks; <paramref name="report"/> lists every check.</summary>
        public static int ValidateScene(out string report)
        {
            var lines = new System.Text.StringBuilder();
            int failures = 0;

            void Check(bool ok, string what)
            {
                lines.Append(ok ? "PASS " : "FAIL ").Append(what).Append('\n');
                if (!ok)
                {
                    failures++;
                }
            }

            Check(File.Exists(ScenePath), "scene file exists: " + ScenePath);
            if (failures > 0)
            {
                report = lines.ToString();
                return failures;
            }

            Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            Check(scene.IsValid() && scene.isLoaded, "scene opens");

            var source = AssetDatabase.LoadAssetAtPath<SceneAsset>(SourceScenePath);
            Check(source != null, "source scene exists: " + SourceScenePath);

            int missing = 0;
            bool hasArena = false;
            foreach (var root in scene.GetRootGameObjects())
            {
                foreach (var t in root.GetComponentsInChildren<Transform>(true))
                {
                    missing += GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(t.gameObject);
                }

                hasArena |= root.name == ArenaName;
            }

            Check(missing == 0, $"no missing scripts (found {missing})");
            Check(hasArena, "arena root present: " + ArenaName);
            Check(HasComponentNamed(scene, "MainSceneScope"), "MainSceneScope present (production composition)");
            Check(HasComponentNamed(scene, "DotsWorldBridge"), "DotsWorldBridge present (production input/send/predict path)");

            report = lines.ToString().TrimEnd('\n');
            return failures;
        }

        private static bool HasComponentNamed(Scene scene, string typeName)
        {
            foreach (var root in scene.GetRootGameObjects())
            {
                foreach (var behaviour in root.GetComponentsInChildren<MonoBehaviour>(true))
                {
                    if (behaviour != null && behaviour.GetType().Name == typeName)
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private static void BuildGrid(Transform parent)
        {
            var grid = new GameObject("Grid").transform;
            grid.SetParent(parent, false);
            int lines = Mathf.RoundToInt(GridHalfExtent / GridSpacing);
            for (int i = -lines; i <= lines; i++)
            {
                float at = i * GridSpacing;
                float width = i == 0 ? 0.12f : 0.04f;
                Strip(grid, $"X{at:+0;-0;0}", new Vector3(at, 0.01f, 0f), new Vector3(width, 0.01f, GridHalfExtent * 2f));
                Strip(grid, $"Z{at:+0;-0;0}", new Vector3(0f, 0.01f, at), new Vector3(GridHalfExtent * 2f, 0.01f, width));
            }

            var origin = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            origin.name = "Origin";
            origin.transform.SetParent(parent, false);
            origin.transform.localScale = new Vector3(0.4f, 0.02f, 0.4f);
            StripCollider(origin);
        }

        private static void Strip(Transform parent, string name, Vector3 position, Vector3 scale)
        {
            var strip = GameObject.CreatePrimitive(PrimitiveType.Cube);
            strip.name = name;
            strip.transform.SetParent(parent, false);
            strip.transform.localPosition = position;
            strip.transform.localScale = scale;
            StripCollider(strip);
        }

        private static void BuildBoxes(Transform parent, string mapId, string mapJson)
        {
            var map = JsonUtility.FromJson<MapBoxes>(mapJson);
            if (map?.boxes == null)
            {
                return;
            }

            // Named for its map: the testlab runner shows only the running map's boxes.
            var holder = new GameObject("ServerCollisionBoxes[" + mapId + "]").transform;
            holder.SetParent(parent, false);
            for (int i = 0; i < map.boxes.Length; i++)
            {
                var b = map.boxes[i];
                // Wire space: x/y ground, z up. Unity: x/z ground, y up (SnapshotSpaceMapping.XZPlane).
                var box = GameObject.CreatePrimitive(PrimitiveType.Cube);
                box.name = $"Box{i}";
                box.transform.SetParent(holder, false);
                box.transform.localPosition = new Vector3(
                    (b.minX + b.maxX) * 0.5f, (b.minZ + b.maxZ) * 0.5f, (b.minY + b.maxY) * 0.5f);
                box.transform.localScale = new Vector3(
                    Mathf.Max(0.01f, b.maxX - b.minX), Mathf.Max(0.01f, b.maxZ - b.minZ), Mathf.Max(0.01f, b.maxY - b.minY));
                StripCollider(box);
            }
        }

        private static void StripCollider(GameObject go)
        {
            var collider = go.GetComponent<Collider>();
            if (collider != null)
            {
                UnityEngine.Object.DestroyImmediate(collider);
            }
        }

        private static string ReadArg(string name)
        {
            string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (args[i] == name)
                {
                    return args[i + 1];
                }
            }

            return null;
        }

        [Serializable]
        private sealed class MapBoxes
        {
            public MapBox[] boxes;
        }

        [Serializable]
        private sealed class MapBox
        {
            public float minX;
            public float minY;
            public float minZ;
            public float maxX;
            public float maxY;
            public float maxZ;
        }
    }
}
