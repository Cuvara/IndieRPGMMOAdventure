#if CUVARA_DOTS
using System.Linq;
using Scripts.DI.Dots;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

/// <summary>
/// Build-time gate for the DOTS view library: the same validation <c>DotsWorldBridge</c> runs at
/// session start, plus an editor-side check that every prefab reference resolves to a real
/// <c>GameObject</c>. A missing or mismatched key fails the build here, with the offending entry
/// named, instead of producing a player whose entities never appear.
/// </summary>
/// <remarks>
/// Runs twice on a CI build on purpose: <see cref="PlayerBuilder"/> calls <see cref="Run"/> before
/// <c>BuildPipeline.BuildPlayer</c> so the message precedes the expensive part, and
/// <see cref="IPreprocessBuildWithReport"/> covers builds started any other way (the Build
/// Settings window, another script). Both paths are cheap.
///
/// A project with no library asset at all is a warning, not a failure: the runtime falls back to
/// primitive views and says so, and the netcode-sample/benchmark players are built from this same
/// pipeline without any authored art.
/// </remarks>
public sealed class DotsViewLibraryBuildCheck : IPreprocessBuildWithReport
{
    public int callbackOrder => 0;

    public void OnPreprocessBuild(BuildReport report) => Run();

    /// <summary>Validates the library asset; throws <see cref="BuildFailedException"/> on any error.</summary>
    public static void Run()
    {
        var asset = FindLibrary(out var path);
        if (asset == null)
        {
            Debug.LogWarning(
                $"[DotsViewLibraryBuildCheck] No DotsViewLibrary asset found (expected at '{DotsViewLibraryAsset.DefaultAssetPath}'). " +
                "The player will fall back to primitive views. Create one via Assets > Create > Cuvara > DOTS View Library.");
            return;
        }

        // Stricter than the runtime check: the reference must point at an actual prefab in the
        // project, not merely carry a well-formed guid.
        bool PrefabExists(string key) =>
            asset.TryGetPrefabReference(key, out var reference) && reference.editorAsset is GameObject;

        var report = DotsViewLibraryValidation.Validate(asset, PrefabExists);
        if (report.HasErrors)
        {
            throw new BuildFailedException(
                $"[DotsViewLibraryBuildCheck] '{path}' has {report.ErrorCount} error(s):\n" +
                DotsViewLibraryValidation.Describe(report));
        }

        if (report.WarningCount > 0)
        {
            Debug.LogWarning($"[DotsViewLibraryBuildCheck] '{path}':\n{DotsViewLibraryValidation.Describe(report)}");
        }

        Debug.Log($"[DotsViewLibraryBuildCheck] '{path}' valid: {asset.Entries.Count} archetype(s).");
    }

    private static DotsViewLibraryAsset FindLibrary(out string path)
    {
        path = DotsViewLibraryAsset.DefaultAssetPath;
        var asset = AssetDatabase.LoadAssetAtPath<DotsViewLibraryAsset>(path);
        if (asset != null) return asset;

        var guid = AssetDatabase.FindAssets("t:DotsViewLibraryAsset").FirstOrDefault();
        if (guid == null) return null;

        path = AssetDatabase.GUIDToAssetPath(guid);
        return AssetDatabase.LoadAssetAtPath<DotsViewLibraryAsset>(path);
    }
}
#endif
