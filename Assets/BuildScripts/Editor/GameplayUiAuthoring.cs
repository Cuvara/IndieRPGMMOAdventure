using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// Authors the <see cref="PanelSettings"/> the runtime-hosted gameplay panel renders with
/// (<c>GameplayPanelDriver</c> loads it from Resources), the way
/// <c>DotsViewLibraryAuthoring</c> authors the view library: from code, idempotently, so no one
/// hand-edits the asset's YAML.
/// </summary>
/// <remarks>
/// Headless: <c>-executeMethod GameplayUiAuthoring.CreatePanelSettings</c>. The theme is the
/// project's <c>UnityDefaultRuntimeTheme.tss</c>; without a theme a runtime panel draws no text.
/// Scale-with-screen at 1920 x 1080, sorted above the default (0) so it draws over other panels.
/// </remarks>
public static class GameplayUiAuthoring
{
    public const string PanelSettingsPath = "Assets/Scripts/UI/Gameplay/Resources/GameplayPanelSettings.asset";
    public const string ThemePath = "Assets/UI Toolkit/UnityThemes/UnityDefaultRuntimeTheme.tss";

    [MenuItem("Cuvara/UI/Create Gameplay Panel Settings")]
    public static void CreatePanelSettings()
    {
        var theme = AssetDatabase.LoadAssetAtPath<ThemeStyleSheet>(ThemePath);
        if (theme == null)
        {
            throw new FileNotFoundException($"[GameplayUiAuthoring] runtime theme not found at '{ThemePath}'.");
        }

        var settings = AssetDatabase.LoadAssetAtPath<PanelSettings>(PanelSettingsPath);
        var created = settings == null;
        if (created)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(PanelSettingsPath) ?? ".");
            settings = ScriptableObject.CreateInstance<PanelSettings>();
            AssetDatabase.CreateAsset(settings, PanelSettingsPath);
        }

        settings.themeStyleSheet = theme;
        settings.scaleMode = PanelScaleMode.ScaleWithScreenSize;
        settings.referenceResolution = new Vector2Int(1920, 1080);
        settings.sortingOrder = 10;
        EditorUtility.SetDirty(settings);
        AssetDatabase.SaveAssets();

        Debug.Log($"[GameplayUiAuthoring] {(created ? "Created" : "Updated")} '{PanelSettingsPath}' (theme '{ThemePath}').");
    }
}
