#if CUVARA_DOTS
using System.IO;
using Scripts.DI.Dots;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEngine;
using UnityEngine.AddressableAssets;

/// <summary>
/// Authors the placeholder view library the real MainScene path needs, without anyone clicking:
/// three primitive prefabs, marked Addressable, and the <see cref="DotsViewLibraryAsset"/> in
/// <c>Resources/</c> that points at them. Idempotent — re-running updates rather than duplicates.
/// </summary>
/// <remarks>
/// Headless: <c>-executeMethod DotsViewLibraryAuthoring.CreatePlaceholderLibrary</c>. The
/// prefabs are stand-ins for art that does not exist yet; replacing them is editing the
/// library asset's entries, not this script. Everything created is validated with the same
/// <see cref="DotsViewLibraryValidation"/> the bridge and the build gate run, and the report is
/// logged (and thrown on errors, so a batchmode run fails loudly).
/// </remarks>
public static class DotsViewLibraryAuthoring
{
    private const string PrefabFolder = "Assets/DotsViews/Prefabs";
    private const string MaterialFolder = "Assets/DotsViews/Materials";
    private const string AddressPrefix = "dots/view/";

    private struct Placeholder
    {
        public string Archetype;
        public string FileName;
        public PrimitiveType Shape;
        public Color Colour;
        public float Scale;
        public float Lift;
        public int PoolSize;
    }

    private static readonly Placeholder[] Placeholders =
    {
        new Placeholder { Archetype = DotsViewArchetypes.PlayerLocal, FileName = "PlayerLocal", Shape = PrimitiveType.Capsule, Colour = new Color(0.2f, 0.5f, 1f), Scale = 1f, Lift = 1f, PoolSize = 4 },
        new Placeholder { Archetype = DotsViewArchetypes.PlayerRemote, FileName = "PlayerRemote", Shape = PrimitiveType.Capsule, Colour = new Color(0.2f, 0.85f, 0.3f), Scale = 1f, Lift = 1f, PoolSize = 32 },
        new Placeholder { Archetype = DotsViewArchetypes.Mob, FileName = "Mob", Shape = PrimitiveType.Sphere, Colour = new Color(0.9f, 0.15f, 0.1f), Scale = 0.8f, Lift = 0.4f, PoolSize = 64 },
    };

    [MenuItem("Cuvara/DOTS/Create Placeholder View Library")]
    public static void CreatePlaceholderLibrary()
    {
        EnsureFolder(PrefabFolder);
        EnsureFolder(MaterialFolder);
        EnsureFolder(Path.GetDirectoryName(DotsViewLibraryAsset.DefaultAssetPath).Replace('\\', '/'));

        var settings = AddressableAssetSettingsDefaultObject.GetSettings(create: true);
        var group = settings.DefaultGroup;

        var entries = new DotsViewLibraryAsset.Entry[Placeholders.Length];
        for (var i = 0; i < Placeholders.Length; i++)
        {
            var placeholder = Placeholders[i];
            var prefabPath = $"{PrefabFolder}/{placeholder.FileName}.prefab";
            var guid = EnsurePrefab(prefabPath, placeholder);

            var entry = settings.CreateOrMoveEntry(guid, group);
            entry.address = AddressPrefix + placeholder.Archetype;

            entries[i] = new DotsViewLibraryAsset.Entry
            {
                Archetype = placeholder.Archetype,
                ViewKey = null, // defaults to the archetype name
                Prefab = new AssetReferenceGameObject(guid),
                PoolSize = placeholder.PoolSize,
                Scale = placeholder.Scale,
                // The visual sits on the server's plane; only the art is lifted by its half-height.
                PositionOffset = new Vector3(0f, placeholder.Lift, 0f),
                RotationOffsetEuler = Vector3.zero,
            };
        }

        settings.SetDirty(AddressableAssetSettings.ModificationEvent.EntryMoved, null, true, true);

        var asset = AssetDatabase.LoadAssetAtPath<DotsViewLibraryAsset>(DotsViewLibraryAsset.DefaultAssetPath);
        var created = asset == null;
        if (created)
        {
            asset = ScriptableObject.CreateInstance<DotsViewLibraryAsset>();
            AssetDatabase.CreateAsset(asset, DotsViewLibraryAsset.DefaultAssetPath);
        }

        asset.Configure(entries);
        EditorUtility.SetDirty(asset);

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        var report = DotsViewLibraryValidation.Validate(
            asset,
            key => asset.TryGetPrefabReference(key, out var reference) && reference.editorAsset is GameObject);

        var summary = $"[DotsViewLibraryAuthoring] {(created ? "Created" : "Updated")} '{DotsViewLibraryAsset.DefaultAssetPath}' " +
                      $"with {entries.Length} archetype(s); prefabs in '{PrefabFolder}', addresses '{AddressPrefix}<archetype>' " +
                      $"in Addressables group '{group.Name}'.";

        if (report.HasErrors)
        {
            throw new System.InvalidOperationException(
                $"{summary}\nValidation FAILED ({report.ErrorCount} error(s)):\n{DotsViewLibraryValidation.Describe(report)}");
        }

        Debug.Log(report.Issues.Count == 0
            ? $"{summary}\nValidation passed."
            : $"{summary}\nValidation passed with warnings:\n{DotsViewLibraryValidation.Describe(report)}");
    }

    /// <summary>
    /// Ensures <c>Assets/Scenes/MainScene.unity</c> carries one <c>DotsWorldBridge</c> with its
    /// default settings (camera follow on, minimap off, library from Resources) and saves the
    /// scene. Headless: <c>-executeMethod DotsViewLibraryAuthoring.EnsureMainSceneBridge</c>.
    /// </summary>
    /// <remarks>
    /// The bridge is injected by <c>MainSceneScope</c>'s build callback, so the scene must also
    /// hold a <c>MainSceneScope</c>; this method reports rather than creates one, because a
    /// scene scope's parent wiring is a project decision (VContainer settings / scope prefab).
    /// </remarks>
    [MenuItem("Cuvara/DOTS/Ensure MainScene DotsWorldBridge")]
    public static void EnsureMainSceneBridge()
    {
        const string scenePath = "Assets/Scenes/MainScene.unity";
        var scene = UnityEditor.SceneManagement.EditorSceneManager.OpenScene(scenePath, UnityEditor.SceneManagement.OpenSceneMode.Single);

        var bridge = Object.FindAnyObjectByType<DotsWorldBridge>(FindObjectsInactive.Include);
        if (bridge == null)
        {
            var host = new GameObject("DotsWorldBridge");
            host.AddComponent<DotsWorldBridge>();
            Undo.RegisterCreatedObjectUndo(host, "Add DotsWorldBridge");
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(scene);
            UnityEditor.SceneManagement.EditorSceneManager.SaveScene(scene);
            Debug.Log($"[DotsViewLibraryAuthoring] Added DotsWorldBridge (defaults) to '{scenePath}' and saved.");
        }
        else
        {
            Debug.Log($"[DotsViewLibraryAuthoring] '{scenePath}' already has a DotsWorldBridge on '{bridge.gameObject.name}'.");
        }

        var scope = Object.FindAnyObjectByType<Scripts.DI.MainSceneScope>(FindObjectsInactive.Include);
        if (scope == null)
        {
            Debug.LogWarning(
                $"[DotsViewLibraryAuthoring] '{scenePath}' has no MainSceneScope; the bridge will not be injected " +
                "and stays inert. Add a MainSceneScope (VContainer LifetimeScope) to the scene.");
        }
    }

    /// <summary>Creates the prefab if missing; returns its asset guid either way.</summary>
    private static string EnsurePrefab(string prefabPath, in Placeholder placeholder)
    {
        var existing = AssetDatabase.AssetPathToGUID(prefabPath);
        if (!string.IsNullOrEmpty(existing) && AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath) != null)
        {
            return existing;
        }

        var material = EnsureMaterial($"{MaterialFolder}/{placeholder.FileName}.mat", placeholder.Colour);

        var instance = GameObject.CreatePrimitive(placeholder.Shape);
        try
        {
            instance.name = placeholder.FileName;
            instance.transform.localScale = Vector3.one * placeholder.Scale;

            // Views carry no physics; a pooled collider per entity is work the physics step does
            // for nothing.
            var collider = instance.GetComponent<Collider>();
            if (collider != null) Object.DestroyImmediate(collider);

            var renderer = instance.GetComponent<Renderer>();
            if (renderer != null) renderer.sharedMaterial = material;

            PrefabUtility.SaveAsPrefabAsset(instance, prefabPath);
        }
        finally
        {
            Object.DestroyImmediate(instance);
        }

        return AssetDatabase.AssetPathToGUID(prefabPath);
    }

    private static Material EnsureMaterial(string path, Color colour)
    {
        var material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (material != null) return material;

        // URP is the project's pipeline; Standard is the fallback for a project without it.
        var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
        material = new Material(shader) { color = colour };
        if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", colour);

        AssetDatabase.CreateAsset(material, path);
        return material;
    }

    private static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;

        var parent = Path.GetDirectoryName(path)?.Replace('\\', '/');
        var name = Path.GetFileName(path);
        if (!string.IsNullOrEmpty(parent) && !AssetDatabase.IsValidFolder(parent)) EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, name);
    }
}
#endif
