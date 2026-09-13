using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

/// <summary>
/// Builds one scene as an IL2CPP player at a chosen managed-stripping level, then puts the
/// project's settings back.
/// </summary>
/// <remarks>
/// <para>
/// IL2CPP strips managed code the Editor never strips, so a library reached only through a
/// registry or by reflection can vanish from a player while every Editor test stays green.
/// That asymmetry has no compile error and no stack trace — the symptom is a feature that
/// silently does nothing — so the only way to answer "does this survive stripping?" is to
/// build a player and run it.
/// </para>
/// <para>
/// <b>The restore is the part that has gone wrong before.</b> Scripting backend and stripping
/// level are project settings, not build options: setting them for one build leaves them set
/// for every build afterwards, including somebody else's. And the restore only reaches disk
/// if the serialised settings are flushed — logging "restored" without
/// <see cref="AssetDatabase.SaveAssets"/> has previously left IL2CPP/High on disk while the
/// log claimed otherwise. So the restore runs in a <c>finally</c> and flushes, and the log
/// line is written after the flush rather than before it.
/// </para>
/// </remarks>
public static class StrippingProbeBuilder
{
    public static void Build()
    {
        string scene = ReadArg("-probeScene")
            ?? throw new InvalidOperationException(
                "StrippingProbeBuilder.Build needs -probeScene <Assets/.../Thing.unity>.");

        if (!File.Exists(scene))
        {
            // Fail here rather than in BuildPipeline, which reports a missing scene as a
            // generic build failure several minutes later.
            throw new InvalidOperationException($"scene not found on disk: {scene}");
        }

        string levelName = ReadArg("-strippingLevel") ?? "High";
        if (!Enum.TryParse(levelName, ignoreCase: true, out ManagedStrippingLevel level))
        {
            throw new InvalidOperationException(
                $"-strippingLevel '{levelName}' is not a ManagedStrippingLevel. " +
                $"Valid: {string.Join(", ", Enum.GetNames(typeof(ManagedStrippingLevel)))}");
        }

        string output = ReadArg("-buildOutput") ?? "build-stripping";
        BuildTarget target = EditorUserBuildSettings.activeBuildTarget;
        var group = NamedBuildTarget.FromBuildTargetGroup(BuildPipeline.GetBuildTargetGroup(target));

        ScriptingImplementation previousBackend = PlayerSettings.GetScriptingBackend(group);
        ManagedStrippingLevel previousLevel = PlayerSettings.GetManagedStrippingLevel(group);

        try
        {
            PlayerSettings.SetScriptingBackend(group, ScriptingImplementation.IL2CPP);
            PlayerSettings.SetManagedStrippingLevel(group, level);
            AssetDatabase.SaveAssets();

            Debug.Log($"[StrippingProbeBuilder] {target}: IL2CPP, stripping {level}, scene {scene}");

            string dir = Path.Combine(output, target.ToString());
            Directory.CreateDirectory(dir);
            string exe = Path.Combine(dir, Path.GetFileNameWithoutExtension(scene) + ExtensionFor(target));

            BuildReport report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = new[] { scene },
                locationPathName = exe,
                target = target,
                targetGroup = BuildPipeline.GetBuildTargetGroup(target),
                options = BuildOptions.None,
            });

            BuildSummary summary = report.summary;

            // A Failed build exits 0 and can leave a plausible-looking .exe behind, so the
            // result is asserted rather than inferred from the file existing.
            if (summary.result != BuildResult.Succeeded)
            {
                throw new Exception(
                    $"[StrippingProbeBuilder] Build {summary.result}: {summary.totalErrors} error(s)");
            }

            Debug.Log(
                $"[StrippingProbeBuilder] BUILD OK {summary.result} -> {exe} " +
                $"(stripping {level}, {summary.totalTime})");
        }
        finally
        {
            PlayerSettings.SetScriptingBackend(group, previousBackend);
            PlayerSettings.SetManagedStrippingLevel(group, previousLevel);
            AssetDatabase.SaveAssets();
            Debug.Log(
                $"[StrippingProbeBuilder] settings restored and FLUSHED: backend {previousBackend}, " +
                $"stripping {previousLevel}");
        }
    }

    private static string ExtensionFor(BuildTarget target)
    {
        switch (target)
        {
            case BuildTarget.StandaloneWindows:
            case BuildTarget.StandaloneWindows64:
                return ".exe";
            case BuildTarget.Android:
                return ".apk";
            default:
                return string.Empty;
        }
    }

    private static string ReadArg(string flag)
    {
        string[] args = Environment.GetCommandLineArgs();
        int i = Array.IndexOf(args, flag);
        return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
    }
}
