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

#if CUVARA_DOTS
        // Before the expensive part: a view library with a missing or mismatched key is a
        // player whose entities never appear, and the message should not wait for a full build.
        DotsViewLibraryBuildCheck.Run();
#endif

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

        // -development: BuildOptions.Development, for runs that need profiler counters in
        // the player — the device benchmark (docs/DEVICE-BENCHMARK.md) is the consumer.
        // Absent, the build is exactly what it always was.
        BuildOptions buildOptions = BuildOptions.None;
        if (HasArg("-development"))
        {
            buildOptions |= BuildOptions.Development;
            Debug.Log("[PlayerBuilder] -development set -> development build");
        }

        var options = new BuildPlayerOptions
        {
            scenes = scenes,
            locationPathName = locationPath,
            target = target,
            targetGroup = BuildPipeline.GetBuildTargetGroup(target),
            options = buildOptions,
        };

        BuildReport report = BuildPipeline.BuildPlayer(options);
        BuildSummary summary = report.summary;

        if (summary.result != BuildResult.Succeeded)
        {
            throw new Exception(
                $"[PlayerBuilder] Build {summary.result} for {target}: " +
                $"{summary.totalErrors} error(s).");
        }

        // summary.totalSize is the build's UNCOMPRESSED content, not the artefact. Reporting
        // it as "N bytes -> <path>" said 2175682249 for a 70 MB apk -- a consistent number
        // about the wrong object, which is the hardest kind of wrong to notice. Report both,
        // each labelled, and read the artefact's size from disk.
        long onDisk = File.Exists(locationPath) ? new FileInfo(locationPath).Length : -1;
        string onDiskText = onDisk >= 0 ? $"{onDisk} bytes" : "MISSING";
        Debug.Log(
            $"[PlayerBuilder] Build succeeded: {locationPath} is {onDiskText} " +
            $"(uncompressed content {summary.totalSize} bytes)");

        // An apk or aab that the report called a success but that is not on disk has happened
        // on this project in the Windows IL2CPP case (exit 0, plausible .exe, no
        // GameAssembly.dll). Fail here rather than let a later step discover it.
        if (onDisk < 0)
        {
            throw new Exception(
                $"[PlayerBuilder] Build reported {summary.result} but produced no file at " +
                $"{locationPath}.");
        }
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
    /// The value is matched against the exact scene path as it appears in Build Settings. A
    /// scene that is NOT in the enabled set but exists on disk is prepended for this build
    /// only — that is how harness-only scenes (the device benchmark,
    /// <c>docs/DEVICE-BENCHMARK.md</c>) boot without ever being enabled, and therefore without
    /// ever shipping in a release build. A path that matches nothing at all is still a hard
    /// error rather than a silent no-op: silently building the wrong boot scene is the failure
    /// this flag exists to prevent.
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
            if (!File.Exists(normalized))
            {
                throw new Exception(
                    $"[PlayerBuilder] -bootScene '{bootScene}' is neither an enabled scene in " +
                    $"Build Settings nor a scene file on disk. Enabled scenes: " +
                    $"{string.Join(", ", scenes)}");
            }

            var prepended = new string[scenes.Length + 1];
            prepended[0] = normalized;
            Array.Copy(scenes, 0, prepended, 1, scenes.Length);
            Debug.Log($"[PlayerBuilder] -bootScene '{normalized}' is not in Build Settings; " +
                      "prepending it for this build only.");
            return prepended;
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

    /// <summary>True when <paramref name="flag"/> appears on the Editor's command line (valueless flag).</summary>
    private static bool HasArg(string flag)
    {
        string[] args = Environment.GetCommandLineArgs();
        for (int i = 0; i < args.Length; i++)
        {
            if (string.Equals(args[i], flag, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
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

        // ANDROID_ABIS picks the native architectures IL2CPP emits, comma-separated:
        // arm64, armv7, x86_64. Unset keeps whatever the project has (arm64 only today).
        //
        // This exists because of the emulator. Every Android emulator image that runs at
        // usable speed on an x86_64 host is x86_64, and an arm64-only apk simply will not
        // install on one -- so without this flag the only way to run an Android build of this
        // game is to own the phone. `ANDROID_ABIS=arm64,x86_64` produces an apk that installs
        // on both, at the cost of a second IL2CPP pass and roughly double the native payload,
        // which is why it is opt-in rather than the default for shipping builds.
        string abis = Environment.GetEnvironmentVariable("ANDROID_ABIS");
        if (!string.IsNullOrEmpty(abis))
        {
            AndroidArchitecture selected = AndroidArchitecture.None;
            foreach (string raw in abis.Split(','))
            {
                switch (raw.Trim().ToLowerInvariant())
                {
                    case "arm64":
                    case "arm64-v8a":
                        selected |= AndroidArchitecture.ARM64;
                        break;
                    case "armv7":
                    case "armeabi-v7a":
                        selected |= AndroidArchitecture.ARMv7;
                        break;
                    case "x86_64":
                    case "x64":
                        selected |= AndroidArchitecture.X86_64;
                        break;
                    default:
                        // Refuse rather than silently build the wrong architecture set: a
                        // typo here produces an apk that installs nowhere, and the failure
                        // appears at `adb install` time with no mention of this variable.
                        throw new Exception(
                            $"[PlayerBuilder] ANDROID_ABIS contains unknown architecture " +
                            $"'{raw.Trim()}'. Known: arm64, armv7, x86_64.");
                }
            }

            PlayerSettings.Android.targetArchitectures = selected;
            Debug.Log($"[PlayerBuilder] ANDROID_ABIS={abis} -> targetArchitectures={selected}");
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
