using System.Runtime.InteropServices;
using System.Threading;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEngine;

/// <summary>
/// Editor entry point for building Addressables content from CI.
/// Invoked by the unity-build-workflows toolkit via
/// <c>-executeMethod AddressableBuilder.Build</c> (platform=Addressables).
/// </summary>
public static class AddressableBuilder
{
    public static void Build()
    {
        var settings = AddressableAssetSettingsDefaultObject.Settings;
        if (settings == null)
        {
            throw new System.Exception(
                "AddressableAssetSettings not found. Open " +
                "Window > Asset Management > Addressables > Groups to create them.");
        }

        AddressableAssetSettings.BuildPlayerContent(out var result);

        if (!string.IsNullOrEmpty(result.Error))
        {
            throw new System.Exception("Addressables build failed: " + result.Error);
        }

        Debug.Log("[AddressableBuilder] Addressables build succeeded.");

        // Exit explicitly rather than leaving it to batchmode's -quit.
        //
        // On 2026-08-12 this job ran for the full 120-minute timeout and was
        // cancelled, with all platform builds skipped as a result — even though the
        // build itself had finished in five minutes. The log shows the sequence:
        // "Addressables build succeeded", then "Batchmode quit successfully invoked
        // - shutting down!", then McpManagerClientHub reporting its server going
        // down, and then nothing at all for 115 minutes.
        //
        // Unity had done the work and begun shutting down; it never finished,
        // because com.ivanmurzak.unity.mcp keeps a hub alive that -quit does not
        // reap. Platform builds are unaffected, which is why Android and WebGL go
        // green on the same commit while this job hangs.
        //
        // Failures above throw before reaching this line, so Unity still exits
        // non-zero on a real build failure — this only forces the successful path
        // to actually terminate.
        //
        // UPDATE 2026-08-21: EditorApplication.Exit(0) alone was NOT enough, and the
        // 2026-08-20 run proves it. Measured from that job's log:
        //
        //   11:28:21  "[AddressableBuilder] Addressables build succeeded"
        //   11:28:25  "Cleanup mono"            <- teardown ran, and ran FAR
        //   11:28:58  [AI] BufferedFileLogStorage flush-after-dispose warning
        //   13:20:04  cancelled by the 120-minute timeout
        //
        // 111 minutes of total silence after a build that took eight. The fix above
        // did help — the 2026-08-12 hang stopped at "Batchmode quit successfully
        // invoked", this one reached "Cleanup mono", which is very late in Unity's
        // teardown — but the process still never terminated.
        //
        // The obvious suspect is com.ivanmurzak.unity.mcp, which is the last thing to
        // speak. IT IS NOT SUFFICIENT AS AN EXPLANATION: the Android build on the same
        // day logs the identical warning one second after its own "Cleanup mono" and
        // exits cleanly. Both paths run -batchmode -quit -executeMethod with the same
        // flags. Why this path specifically stalls is NOT established.
        //
        // So: keep the graceful path, and bound it. The watchdog is a background
        // thread, so it costs nothing when the exit works — it dies with the process.
        // When the exit stalls it kills outright, turning a 120-minute timeout that
        // takes every platform build down with it into a 30-second annoyance.
        //
        // It is also the experiment. If the CI log shows the watchdog line, the
        // graceful shutdown stalled and the deadlock is in managed teardown. If it
        // never appears, EditorApplication.Exit was fine all along and the stall was
        // somewhere this line cannot see.
        // MEASURED 2026-08-21: the watchdog below FIRED. The build reported success,
        // EditorApplication.Exit(0) ran, and the process still had to be killed — so the
        // deadlock is in managed shutdown, which is what the previous revision set out
        // to find out. The job ended in 8.5 minutes instead of the 120-minute timeout.
        //
        // But it ended in FAILURE, because killing yourself yields 137 and GameCI reads
        // the exit code. A build that succeeded must not report failure, so the exit has
        // to be immediate AND zero.
        //
        // Environment.Exit is not that: it runs finalizers and AppDomain unload, which is
        // exactly the machinery that is stuck. libc `_exit` returns to the kernel without
        // touching any of it. The Editor here is Linux (the lane builds
        // StandaloneLinux64 in the GameCI container), so libc is present; the fallback
        // covers anything else and is no worse than the previous behaviour.
        var watchdog = new Thread(() =>
        {
            Thread.Sleep(30000);
            Debug.LogWarning(
                "[AddressableBuilder] Graceful exit stalled for 30s after a successful " +
                "build; leaving the process immediately with code 0. The build output is " +
                "already on disk — see the comment in AddressableBuilder.cs for what this " +
                "means.");

            // Give the log a moment to drain. `_exit` returns straight to the kernel and
            // flushes nothing, so without this the warning above — the only evidence that
            // this path was taken at all — can be lost with the buffer.
            Thread.Sleep(2000);
            HardExitZero();
        })
        {
            IsBackground = true,
            Name = "AddressableBuilder exit watchdog"
        };
        watchdog.Start();

        EditorApplication.Exit(0);
    }

    [DllImport("libc", EntryPoint = "_exit")]
    private static extern void LibcExit(int code);

    /// <summary>
    /// Leaves the process immediately with the given code, running no managed shutdown.
    /// </summary>
    private static void HardExitZero()
    {
        try
        {
            LibcExit(0);
        }
        catch (System.Exception)
        {
            // No libc (or the P/Invoke was refused). Killing still beats hanging; the
            // non-zero code is the old behaviour, not a new failure mode.
            System.Diagnostics.Process.GetCurrentProcess().Kill();
        }
    }
}
