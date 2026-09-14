using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

/// <summary>
/// Builds ONLY the UI Toolkit ScreenFlow sample scene into a standalone player.
/// </summary>
/// <remarks>
/// Deliberately does not touch EditorBuildSettings. The documented -bootScene route requires
/// the scene to already be in the enabled set, so using it would mean editing a project
/// setting and remembering to put it back — and this project has already lost its MainScene
/// entry from EditorBuildSettings once. Passing the scene straight to BuildPipeline avoids
/// the whole class of problem: nothing project-wide changes, so there is nothing to restore.
/// </remarks>
public static class SampleBuilder
{
    public static void Build()
    {
        // The Package Manager imports samples as "Assets/Samples/<displayName>/<version>/<sample
        // displayName>", so this path is what an actual "Import" produces. An earlier copy of the
        // same scene sat at "Assets/Samples/Cuvara UIToolkit/ScreenFlow/" with no .asmdef, which
        // put its scripts in Assembly-CSharp next to the same types in the sample assembly; it has
        // been removed, and pointing here is what keeps this builder aimed at the tracked copy.
        const string scene = "Assets/Samples/Cuvara UI Toolkit/0.1.0/Screen Flow (scene)/ScreenFlowSample.unity";

        // Derived from dataPath rather than hardcoded: this ran off an absolute E:\ path that only
        // existed on one machine. BuildPipeline needs a real filesystem path and an environment
        // variable set from WSL does not survive into Unity.exe, so the project root is the one
        // anchor available to every caller.
        var output = Path.Combine(
            Directory.GetParent(Application.dataPath)!.FullName,
            "Builds", "ScreenFlowSample", "ScreenFlowSample.exe");

        if (!File.Exists(scene))
        {
            Debug.LogError($"BUILD_RESULT missing scene: {scene}");
            EditorApplication.Exit(2);
            return;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(output)!);

        var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
        {
            scenes           = new[] { scene },
            locationPathName = output,
            target           = BuildTarget.StandaloneWindows64,
            options          = BuildOptions.None,
        });

        var s = report.summary;
        Debug.Log($"BUILD_RESULT result={s.result} errors={s.totalErrors} warnings={s.totalWarnings} " +
                  $"size={s.totalSize} out={s.outputPath}");

        if (s.result != BuildResult.Succeeded)
        {
            foreach (var step in report.steps)
            foreach (var msg in step.messages.Where(m => m.type is LogType.Error or LogType.Exception))
                Debug.Log($"BUILD_ERROR {step.name}: {msg.content}");
        }

        EditorApplication.Exit(s.result == BuildResult.Succeeded ? 0 : 1);
    }
}
