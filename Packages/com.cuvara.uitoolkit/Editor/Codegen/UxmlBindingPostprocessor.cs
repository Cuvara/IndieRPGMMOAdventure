namespace Cuvara.UIToolkit.Editor
{
    using System;
    using Cuvara.UIToolkit.Codegen;
    using UnityEditor;
    using UnityEngine;

    /// <summary>
    /// Auto-regen: whenever a <c>.uxml</c> is (re)imported, its bindings file is
    /// regenerated — IF one already exists.
    /// </summary>
    /// <remarks>
    /// <para><b>The opt-in gate.</b> A UXML with no <c>Generated/&lt;Name&gt;.uxml.g.cs</c>
    /// is never touched automatically; enrollment happens once, through
    /// <see cref="UxmlBindingMenuItems"/>. The existence of the generated file is the
    /// entire opt-in state — no settings asset, no registry to drift.</para>
    ///
    /// <para><b>The loop guard.</b> Writing a <c>.g.cs</c> triggers an import of its own,
    /// and an import pipeline that writes on every pass never settles. So the pipeline
    /// skips the write (and this class skips <c>AssetDatabase.Refresh</c>) when the fresh
    /// content is byte-identical to what is on disk — the steady state is zero writes.</para>
    ///
    /// <para>A malformed UXML (duplicate names, colliding properties) logs an error and
    /// leaves the previous generated file in place, rather than breaking the import of the
    /// UXML itself.</para>
    /// </remarks>
    internal sealed class UxmlBindingPostprocessor : AssetPostprocessor
    {
        private static void OnPostprocessAllAssets(
            string[] importedAssets, string[] deletedAssets, string[] movedAssets, string[] movedFromAssetPaths)
        {
            var wroteAny = false;
            foreach (var assetPath in importedAssets)
            {
                if (!assetPath.EndsWith(".uxml", StringComparison.OrdinalIgnoreCase)) continue;
                if (!UxmlBindingPipeline.IsEnrolled(assetPath)) continue; // the opt-in gate

                try
                {
                    wroteAny |= UxmlBindingPipeline.RegenerateIfChanged(assetPath);
                }
                catch (UxmlCodegenException exception)
                {
                    Debug.LogError($"UXML bindings NOT regenerated for '{assetPath}': {exception.Message}");
                }
            }

            if (wroteAny) AssetDatabase.Refresh();
        }
    }
}
