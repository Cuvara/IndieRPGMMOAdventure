namespace Cuvara.UIToolkit.Editor
{
    using Cuvara.UIToolkit.Codegen;
    using UnityEditor;
    using UnityEngine;

    /// <summary>
    /// The enrollment gesture: right-click one or more <c>.uxml</c> assets →
    /// <c>Assets/Cuvara/Generate UXML Bindings</c> generates
    /// <c>Generated/&lt;Name&gt;.uxml.g.cs</c> beside each.
    /// </summary>
    /// <remarks>
    /// This is deliberately the ONLY way a UXML starts being generated for.
    /// <see cref="UxmlBindingPostprocessor"/> regenerates on import, but only files whose
    /// generated counterpart already exists — so codegen never touches a UXML nobody asked
    /// it to, and un-enrolling is just deleting the generated file.
    /// </remarks>
    internal static class UxmlBindingMenuItems
    {
        private const string MenuPath = "Assets/Cuvara/Generate UXML Bindings";

        [MenuItem(MenuPath)]
        private static void GenerateForSelection()
        {
            var wroteAny = false;
            foreach (var uxmlPath in SelectedUxmlPaths())
            {
                try
                {
                    if (UxmlBindingPipeline.RegenerateIfChanged(uxmlPath))
                    {
                        wroteAny = true;
                        Debug.Log($"UXML bindings generated: {UxmlBindingPipeline.GetGeneratedPath(uxmlPath)}");
                    }
                    else
                    {
                        Debug.Log($"UXML bindings already up to date: {UxmlBindingPipeline.GetGeneratedPath(uxmlPath)}");
                    }
                }
                catch (UxmlCodegenException exception)
                {
                    Debug.LogError($"UXML bindings NOT generated for '{uxmlPath}': {exception.Message}");
                }
            }

            if (wroteAny) AssetDatabase.Refresh();
        }

        [MenuItem(MenuPath, isValidateFunction: true)]
        private static bool SelectionHasUxml()
        {
            foreach (var _ in SelectedUxmlPaths()) return true;
            return false;
        }

        private static System.Collections.Generic.IEnumerable<string> SelectedUxmlPaths()
        {
            foreach (var guid in Selection.assetGUIDs)
            {
                var assetPath = AssetDatabase.GUIDToAssetPath(guid);
                if (assetPath.EndsWith(".uxml", System.StringComparison.OrdinalIgnoreCase))
                {
                    yield return assetPath;
                }
            }
        }
    }
}
