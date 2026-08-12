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
        EditorApplication.Exit(0);
    }
}
