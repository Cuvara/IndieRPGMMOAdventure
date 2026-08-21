using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

/// <summary>
/// Editor entry point for building a standalone player from CI.
/// Invoked by the unity-build-workflows toolkit's self-hosted (local) lane via
/// <c>-executeMethod PlayerBuilder.Build</c>. The workflow first switches the
/// active build target with <c>-buildTarget</c>, so this method builds for
/// <see cref="EditorUserBuildSettings.activeBuildTarget"/>.
///
/// Output goes to <c>&lt;BUILD_OUTPUT_DIR&gt;/&lt;target&gt;/</c> (default
/// <c>build/&lt;target&gt;/</c>, relative to the working directory) so the
/// toolkit's <c>Upload build artifact</c> step (path <c>build/</c>) collects it.
/// </summary>
public static class PlayerBuilder
{
    public static void Build()
    {
        BuildTarget target = EditorUserBuildSettings.activeBuildTarget;

        if (target == BuildTarget.Android)
        {
            ConfigureAndroidFromEnvironment();
        }

        string[] scenes = EditorBuildSettings.scenes
            .Where(s => s.enabled)
            .Select(s => s.path)
            .ToArray();

        if (scenes.Length == 0)
        {
            throw new Exception(
                "[PlayerBuilder] No enabled scenes in Build Settings. " +
                "Add at least one scene under File > Build Settings.");
        }

        scenes = ApplyBootSceneOverride(scenes, ReadArg("-bootScene"));

        string outputRoot = ReadArg("-buildOutput")
                            ?? Environment.GetEnvironmentVariable("BUILD_OUTPUT_DIR");
        if (string.IsNullOrEmpty(outputRoot))
        {
            outputRoot = "build";
        }

        string productName = string.IsNullOrEmpty(PlayerSettings.productName)
            ? "Build"
            : string.Concat(PlayerSettings.productName.Split(Path.GetInvalidFileNameChars()));

        string targetDir = Path.Combine(outputRoot, target.ToString());
        Directory.CreateDirectory(targetDir);

        string locationPath = LocationFor(target, targetDir, productName);

        Debug.Log($"[PlayerBuilder] Building {target} -> {locationPath} " +
                  $"({scenes.Length} scene(s))");

        var options = new BuildPlayerOptions
        {
            scenes = scenes,
            locationPathName = locationPath,
            target = target,
            targetGroup = BuildPipeline.GetBuildTargetGroup(target),
            options = BuildOptions.None,
        };

        BuildReport report = BuildPipeline.BuildPlayer(options);
        BuildSummary summary = report.summary;

        if (summary.result != BuildResult.Succeeded)
        {
            throw new Exception(
                $"[PlayerBuilder] Build {summary.result} for {target}: " +
                $"{summary.totalErrors} error(s).");
        }

        Debug.Log($"[PlayerBuilder] Build succeeded: {summary.totalSize} bytes -> {locationPath}");
    }

    /// <summary>
    /// Moves <paramref name="bootScene"/> to index 0 of <paramref name="scenes"/>, leaving the
    /// relative order of everything else intact. Returns <paramref name="scenes"/> unchanged when
    /// no override was given.
    /// </summary>
    /// <remarks>
    /// Index 0 is the scene the player boots, so the build-settings order alone decides it. The
    /// release build must boot <c>Assets/Scenes/MainScene.unity</c>, while the three-client
    /// multiplayer harness documented in CLAUDE.md needs a player that boots the netcode DOTS
    /// sample. Both scenes stay enabled and shipped; this flag picks which one starts, so the
    /// harness no longer needs the build settings edited (and committed) to work.
    ///
    /// The value is matched against the exact scene path as it appears in Build Settings.
    /// A path that is not in the enabled set is a hard error rather than a silent no-op:
    /// silently building the wrong boot scene is the failure this flag exists to prevent.
    /// </remarks>
    private static string[] ApplyBootSceneOverride(string[] scenes, string bootScene)
    {
        if (string.IsNullOrEmpty(bootScene))
        {
            return scenes;
        }

        string normalized = bootScene.Replace('\\', '/');
        int index = Array.FindIndex(scenes,
            s => string.Equals(s, normalized, StringComparison.OrdinalIgnoreCase));

        if (index < 0)
        {
            throw new Exception(
                $"[PlayerBuilder] -bootScene '{bootScene}' is not an enabled scene in Build " +
                $"Settings. Enabled scenes: {string.Join(", ", scenes)}");
        }

        if (index == 0)
        {
            Debug.Log($"[PlayerBuilder] -bootScene '{normalized}' is already index 0.");
            return scenes;
        }

        var reordered = new string[scenes.Length];
        reordered[0] = scenes[index];
        int write = 1;
        for (int i = 0; i < scenes.Length; i++)
        {
            if (i != index)
            {
                reordered[write++] = scenes[i];
            }
        }

        Debug.Log($"[PlayerBuilder] -bootScene '{normalized}' moved to index 0 " +
                  $"(was index {index}).");
        return reordered;
    }

    /// <summary>Reads <c>&lt;flag&gt; &lt;value&gt;</c> off the Editor's command line; null when absent.</summary>
    /// <remarks>
    /// <c>BUILD_OUTPUT_DIR</c> alone is not enough when the build is driven from WSL:
    /// exporting a variable in a WSL shell does not put it in the environment of a
    /// Windows <c>Unity.exe</c>, so the output silently landed in the default
    /// <c>build/</c> instead of where the caller asked. A command-line flag crosses that
    /// boundary. The environment variable still works and still wins nothing — the flag
    /// takes precedence, everything else is unchanged.
    /// </remarks>
    private static string ReadArg(string flag)
    {
        string[] args = Environment.GetCommandLineArgs();
        for (int i = 0; i < args.Length - 1; i++)
        {
            if (string.Equals(args[i], flag, StringComparison.Ordinal) &&
                !string.IsNullOrEmpty(args[i + 1]))
            {
                return args[i + 1];
            }
        }

        return null;
    }

    // Optional env-driven Android configuration, used by the release-signing lane
    // to produce a .aab signed with a specific keystore without touching
    // ProjectSettings. Every variable is optional and backward-compatible:
    // when unset, the project's existing Build Settings / ProjectSettings
    // values are left untouched.
    //   ANDROID_APP_BUNDLE=1                 -> EditorUserBuildSettings.buildAppBundle = true
    //   ANDROID_KEYSTORE=<path>               -> useCustomKeystore + keystoreName
    //   ANDROID_KEYSTORE_PASS=<pass>           -> keystorePass
    //   ANDROID_KEYALIAS_NAME=<alias>          -> keyaliasName
    //   ANDROID_KEYALIAS_PASS=<pass>           -> keyaliasPass
    private static void ConfigureAndroidFromEnvironment()
    {
        string appBundle = Environment.GetEnvironmentVariable("ANDROID_APP_BUNDLE");
        if (!string.IsNullOrEmpty(appBundle) && (appBundle == "1" ||
            appBundle.Equals("true", StringComparison.OrdinalIgnoreCase)))
        {
            EditorUserBuildSettings.buildAppBundle = true;
            Debug.Log("[PlayerBuilder] ANDROID_APP_BUNDLE set -> building .aab");
        }

        string keystorePath = Environment.GetEnvironmentVariable("ANDROID_KEYSTORE");
        if (!string.IsNullOrEmpty(keystorePath))
        {
            PlayerSettings.Android.useCustomKeystore = true;
            PlayerSettings.Android.keystoreName = keystorePath;
            Debug.Log($"[PlayerBuilder] ANDROID_KEYSTORE set -> using custom keystore {keystorePath}");

            string keystorePass = Environment.GetEnvironmentVariable("ANDROID_KEYSTORE_PASS");
            if (!string.IsNullOrEmpty(keystorePass))
            {
                PlayerSettings.Android.keystorePass = keystorePass;
            }

            string keyaliasName = Environment.GetEnvironmentVariable("ANDROID_KEYALIAS_NAME");
            if (!string.IsNullOrEmpty(keyaliasName))
            {
                PlayerSettings.Android.keyaliasName = keyaliasName;
            }

            string keyaliasPass = Environment.GetEnvironmentVariable("ANDROID_KEYALIAS_PASS");
            if (!string.IsNullOrEmpty(keyaliasPass))
            {
                PlayerSettings.Android.keyaliasPass = keyaliasPass;
            }
        }
    }

    // Per-target output location. WebGL builds into a directory; standalone/
    // mobile targets build to a file with the platform's expected extension.
    private static string LocationFor(BuildTarget target, string targetDir, string productName)
    {
        switch (target)
        {
            case BuildTarget.WebGL:
                return targetDir; // WebGL emits a folder (index.html, Build/, etc.)
            case BuildTarget.Android:
                return Path.Combine(targetDir,
                    productName + (EditorUserBuildSettings.buildAppBundle ? ".aab" : ".apk"));
            case BuildTarget.StandaloneWindows64:
            case BuildTarget.StandaloneWindows:
                return Path.Combine(targetDir, productName + ".exe");
            case BuildTarget.StandaloneLinux64:
                return Path.Combine(targetDir, productName + ".x86_64");
            case BuildTarget.StandaloneOSX:
                return Path.Combine(targetDir, productName + ".app");
            default:
                return Path.Combine(targetDir, productName);
        }
    }
}
