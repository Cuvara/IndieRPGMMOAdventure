using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.PackageManager.UI;
using UnityEngine;

/// <summary>
/// Headless import of a UPM package sample, so a package feature's sample scene can be
/// exercised in this project from batch mode without a human clicking Package Manager.
/// </summary>
/// <remarks>
/// <code>
/// Unity.exe -quit -batchmode -projectPath ... -executeMethod SampleImporter.Import
///   -importPackage com.cuvara.netcode -importSample "Reconnect Policy Demo" [-addToBuild 1]
/// </code>
/// Imports (overriding a previous import of the same sample) into
/// <c>Assets/Samples/&lt;package displayName&gt;/&lt;version&gt;/&lt;sample&gt;/</c>, and with
/// <c>-addToBuild 1</c> appends every <c>.unity</c> in the imported folder to
/// <c>EditorBuildSettings.scenes</c> (enabled) so <c>PlayerBuilder -bootScene</c> can boot it.
/// The build-settings change is meant for a test run, not for committing.
/// </remarks>
public static class SampleImporter
{
    public static void Import()
    {
        var args = Environment.GetCommandLineArgs();
        var package = Arg(args, "-importPackage");
        var sampleName = Arg(args, "-importSample");
        var addToBuild = Arg(args, "-addToBuild") == "1";
        if (string.IsNullOrEmpty(package) || string.IsNullOrEmpty(sampleName))
            throw new InvalidOperationException("SampleImporter.Import needs -importPackage <name> -importSample <displayName>.");

        var samples = Sample.FindByPackage(package, string.Empty).ToList();
        var sample = samples.FirstOrDefault(s => string.Equals(s.displayName, sampleName, StringComparison.Ordinal));
        if (sample.displayName == null)
            throw new InvalidOperationException(
                $"Sample '{sampleName}' not found in {package}. Available: {string.Join(", ", samples.Select(s => s.displayName))}");

        if (!sample.Import(Sample.ImportOptions.OverridePreviousImports))
            throw new InvalidOperationException($"Sample.Import failed for '{sampleName}' ({package}).");

        AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        Debug.Log($"[SampleImporter] Imported '{sampleName}' from {package} into '{sample.importPath}'.");

        if (!addToBuild) return;

        var projectRoot = Path.GetDirectoryName(Application.dataPath) ?? string.Empty;
        var scenes = Directory.GetFiles(sample.importPath, "*.unity", SearchOption.AllDirectories)
            .Select(p => Path.GetRelativePath(projectRoot, p).Replace('\\', '/'))
            .OrderBy(p => p, StringComparer.Ordinal)
            .ToList();
        var list = EditorBuildSettings.scenes.ToList();
        var added = 0;
        foreach (var scene in scenes)
        {
            if (list.Any(s => string.Equals(s.path, scene, StringComparison.OrdinalIgnoreCase))) continue;
            list.Add(new EditorBuildSettingsScene(scene, true));
            added++;
        }

        EditorBuildSettings.scenes = list.ToArray();
        Debug.Log($"[SampleImporter] Build settings: +{added} scene(s) from the sample ({string.Join(", ", scenes)}).");
    }

    private static string Arg(string[] args, string name)
    {
        for (var i = 0; i < args.Length - 1; i++)
            if (string.Equals(args[i], name, StringComparison.Ordinal)) return args[i + 1];
        return null;
    }
}
