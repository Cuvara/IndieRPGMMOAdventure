using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

/// <summary>
/// Builds the netcode DOTS Sample — the one scene in this project that plays against a live
/// backend — into a standalone player that can be launched several times side by side.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why not <c>PlayerBuilder</c>.</b> The enabled set in EditorBuildSettings still names
/// <c>Assets/Samples/Cuvara Netcode/0.28.1/DOTS Sample/Scenes/DOTSSample.unity</c>. The
/// sample now lives at a version-free, tracked path (<c>Assets/Samples/Netcode/DOTS Sample</c>)
/// with its source recorded in <c>.sample-source</c> and checked by CI (#135): the old
/// version-named folder was untracked, held newer content than its name, and existed only on
/// the machine that imported it.
/// Building from the enabled set therefore builds a scene that is not on disk. This builder
/// names the scene it wants and hands it straight to <c>BuildPipeline</c>, which is the same
/// choice <see cref="SampleBuilder"/> made and for the same reason: nothing project-wide
/// changes, so there is nothing to put back.
/// </para>
/// <para>
/// The player takes its backend address from the command line at startup
/// (<c>-cuvara-gateway-host</c>, <c>-cuvara-gateway-port</c>, <c>-cuvara-device</c>,
/// <c>-cuvara-instance</c>, see <c>BackendCommandLine</c>), so one build serves every window
/// and each window authenticates as a different user.
/// </para>
/// <para>
/// <b>The exit code is not the verdict.</b> A Unity batch run on this project has exited 0 with
/// a failed build and a plausible executable behind it. Read the <c>BUILD_RESULT</c> line, and
/// note that this builder also stats the artefact itself rather than trusting the report.
/// </para>
/// </remarks>
public static class PlayClientBuilder
{
    private const string Scene =
        "Assets/Samples/Netcode/DOTS Sample/Scenes/DOTSSample.unity";

    public static void Build()
    {
        if (!File.Exists(Scene))
        {
            Debug.LogError($"BUILD_RESULT missing scene: {Scene}");
            EditorApplication.Exit(2);
            return;
        }

        var output = Path.Combine(
            Directory.GetParent(Application.dataPath)!.FullName,
            "Builds", "PlayClient", "PlayClient.exe");

        Directory.CreateDirectory(Path.GetDirectoryName(output)!);

        var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
        {
            scenes           = new[] { Scene },
            locationPathName = output,
            target           = BuildTarget.StandaloneWindows64,
            options          = BuildOptions.None,
        });

        var s = report.summary;

        // summary.totalSize is uncompressed content, not the artefact — reporting it as the
        // player's size has misled on this project before. Stat the file that was asked for.
        var onDisk = File.Exists(output) ? new FileInfo(output).Length : -1L;

        Debug.Log($"BUILD_RESULT result={s.result} errors={s.totalErrors} warnings={s.totalWarnings} " +
                  $"onDisk={onDisk} out={s.outputPath}");

        if (s.result != BuildResult.Succeeded)
        {
            foreach (var step in report.steps)
            foreach (var msg in step.messages.Where(m => m.type is LogType.Error or LogType.Exception))
                Debug.Log($"BUILD_ERROR {step.name}: {msg.content}");
        }

        EditorApplication.Exit(s.result == BuildResult.Succeeded && onDisk > 0 ? 0 : 1);
    }
}
