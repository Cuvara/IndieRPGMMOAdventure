#if CUVARA_DOTS
namespace Scripts.DI.Dots
{
    using System;
    using System.Collections.Generic;
    using Cuvara.DOTS.Configuration;
    using UnityEngine;

    /// <summary>
    /// Validates a <see cref="DotsViewLibraryAsset"/> the way the session will consume it: the
    /// package's own <c>ViewConfigValidator</c> over the generated library, plus this game's rules
    /// — every archetype the resolver can name must exist, every server kind must map, and every
    /// entry must reference a prefab.
    /// </summary>
    /// <remarks>
    /// Runtime-safe and pure C# apart from creating the temporary package objects, so the same
    /// code runs in <c>DotsWorldBridge</c> at session start and in the build-time check in
    /// <c>Assets/BuildScripts</c>. Failing here is the whole point: a wrong key at build time is a
    /// message; at runtime it is an invisible entity.
    /// </remarks>
    public static class DotsViewLibraryValidation
    {
        public const string MissingPrefabReference = "MissingPrefabReference";
        public const string MissingArchetype = "MissingArchetype";
        public const string MissingLibrary = "MissingLibrary";

        /// <summary>
        /// Validates the asset. <paramref name="prefabExists"/> defaults to the asset's own
        /// reference check; the build check passes a stricter one that also looks at the editor
        /// asset.
        /// </summary>
        public static ViewConfigValidationReport Validate(DotsViewLibraryAsset asset, Func<string, bool> prefabExists = null)
        {
            var report = new ViewConfigValidationReport();
            if (asset == null)
            {
                report.Add(ViewConfigIssue.Error(MissingLibrary, "<library>",
                    $"No DotsViewLibrary asset. Create one via Assets > Create > Cuvara > DOTS View Library at '{DotsViewLibraryAsset.DefaultAssetPath}'."));
                return report;
            }

            prefabExists ??= asset.HasPrefab;

            var library = asset.BuildLibrary(out var configs);
            try
            {
                ViewConfigValidator.ValidateLibrary(library, prefabExists, report);

                var mappings = ViewConfigValidator.ValidateMappings(library, DotsViewArchetypes.ServerKindMappings);
                foreach (var issue in mappings.Issues) report.Add(issue);

                // The local archetype is produced by the resolver from isLocal, not from a server
                // kind, so the mapping check above cannot see it.
                var names = new HashSet<string>();
                foreach (var entry in library.Entries) names.Add(entry.Name);
                foreach (var required in DotsViewArchetypes.All)
                {
                    if (!names.Contains(required))
                    {
                        report.Add(ViewConfigIssue.Error(MissingArchetype, required,
                            $"'{asset.name}' has no entry for archetype '{required}', which DotsViewArchetypes names. " +
                            "Entities resolving to it would never be presented. Add the entry."));
                    }
                }

                // A reference check independent of the key rules, so an entry with a bad key and
                // no prefab reports both rather than hiding the second behind the first.
                for (var i = 0; i < asset.Entries.Count; i++)
                {
                    var entry = asset.Entries[i];
                    var subject = string.IsNullOrEmpty(entry.Archetype) ? $"{asset.name}[{i}]" : entry.Archetype;
                    if (entry.Prefab == null || !entry.Prefab.RuntimeKeyIsValid())
                    {
                        report.Add(ViewConfigIssue.Error(MissingPrefabReference, subject,
                            $"Entry '{subject}' in '{asset.name}' has no prefab reference. Assign an Addressable prefab."));
                    }
                }
            }
            finally
            {
                Destroy(library);
                foreach (var config in configs) Destroy(config);
            }

            return report;
        }

        /// <summary>One line per issue, for a console or a build log.</summary>
        public static string Describe(ViewConfigValidationReport report)
        {
            var lines = new List<string>(report.Issues.Count);
            foreach (var issue in report.Issues) lines.Add(issue.ToString());
            return string.Join("\n", lines);
        }

        private static void Destroy(UnityEngine.Object target)
        {
            if (target == null) return;
            if (Application.isPlaying) UnityEngine.Object.Destroy(target);
            else UnityEngine.Object.DestroyImmediate(target);
        }
    }
}
#endif
