#if CUVARA_DOTS
namespace Scripts.DI.Dots
{
    using System;
    using System.Collections.Generic;
    using Cuvara.DOTS.Configuration;
    using UnityEngine;
    using UnityEngine.AddressableAssets;

    /// <summary>
    /// The authored list of view archetypes for this game: one entry per archetype name the
    /// <c>TypeArchetypeResolver</c> can produce, each naming the pool key, the Addressables prefab
    /// behind it and the presentation numbers a <c>ViewConfig</c> carries.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>One asset, generated package objects.</b> The package's <c>ViewConfig</c> and
    /// <c>ViewArchetypeLibrary</c> are ScriptableObjects, and authoring them by hand means three
    /// assets per archetype whose keys must agree with an Addressables address typed somewhere
    /// else. This asset holds everything in one place and <see cref="BuildLibrary"/> generates the
    /// package objects at session start; the prefab is an <c>AssetReferenceGameObject</c>, so the
    /// link survives renames and a missing one is a build-time error
    /// (<see cref="DotsViewLibraryValidation"/>) rather than an invisible entity.
    /// </para>
    /// <para>
    /// Lives in <c>Resources/</c> at <see cref="ResourcesPath"/> so the root scope can find it
    /// without a scene reference; <c>DotsWorldBridge</c> may also point at a different asset for a
    /// specific scene.
    /// </para>
    /// </remarks>
    [CreateAssetMenu(fileName = "DotsViewLibrary", menuName = "Cuvara/DOTS View Library")]
    public sealed class DotsViewLibraryAsset : ScriptableObject
    {
        /// <summary>Path under <c>Resources/</c> the root scope loads by default.</summary>
        public const string ResourcesPath = "DotsViews/DotsViewLibrary";

        /// <summary>Where the default asset is expected on disk, for messages and the build check.</summary>
        public const string DefaultAssetPath = "Assets/Resources/" + ResourcesPath + ".asset";

        [Serializable]
        public struct Entry
        {
            [Tooltip("Archetype name the resolver produces — see DotsViewArchetypes.")]
            public string Archetype;

            [Tooltip("Pool / view key. Defaults to the archetype name when empty.")]
            public string ViewKey;

            [Tooltip("The prefab this view spawns. Must be in an Addressables group.")]
            public AssetReferenceGameObject Prefab;

            [Min(0)] public int PoolSize;

            [Min(0.0001f)] public float Scale;

            public Vector3 PositionOffset;

            public Vector3 RotationOffsetEuler;

            /// <summary>The key the pool and the loader use: the explicit view key, else the archetype name.</summary>
            public string EffectiveViewKey => string.IsNullOrEmpty(this.ViewKey) ? this.Archetype : this.ViewKey;
        }

        [SerializeField] private Entry[] entries = Array.Empty<Entry>();

        public IReadOnlyList<Entry> Entries => this.entries;

        /// <summary>Replaces the entries in code — tests, and editor tooling that seeds the asset.</summary>
        public void Configure(params Entry[] newEntries)
        {
            this.entries = newEntries ?? Array.Empty<Entry>();
        }

        /// <summary>Whether <paramref name="viewKey"/> has a usable prefab reference — the validator's <c>prefabExists</c>.</summary>
        public bool HasPrefab(string viewKey) => this.TryGetPrefabReference(viewKey, out _);

        public bool TryGetPrefabReference(string viewKey, out AssetReferenceGameObject reference)
        {
            reference = null;
            if (string.IsNullOrEmpty(viewKey)) return false;

            for (var i = 0; i < this.entries.Length; i++)
            {
                if (this.entries[i].EffectiveViewKey != viewKey) continue;
                var candidate = this.entries[i].Prefab;
                if (candidate == null || !candidate.RuntimeKeyIsValid()) return false;

                reference = candidate;
                return true;
            }

            return false;
        }

        /// <summary>
        /// Generates the package's <c>ViewArchetypeLibrary</c> and one <c>ViewConfig</c> per entry.
        /// The caller owns the returned objects and destroys them on teardown.
        /// </summary>
        public ViewArchetypeLibrary BuildLibrary(out ViewConfig[] configs)
        {
            configs = new ViewConfig[this.entries.Length];
            var libraryEntries = new ViewArchetypeLibrary.Entry[this.entries.Length];

            for (var i = 0; i < this.entries.Length; i++)
            {
                var entry = this.entries[i];
                var config = ScriptableObject.CreateInstance<ViewConfig>();
                config.name = string.IsNullOrEmpty(entry.Archetype) ? $"{this.name}[{i}]" : entry.Archetype;
                config.Configure(
                    entry.EffectiveViewKey,
                    pool: entry.PoolSize,
                    uniformScale: entry.Scale,
                    position: entry.PositionOffset,
                    rotationEuler: entry.RotationOffsetEuler);

                configs[i] = config;
                libraryEntries[i] = new ViewArchetypeLibrary.Entry { Name = entry.Archetype, Config = config };
            }

            var library = ScriptableObject.CreateInstance<ViewArchetypeLibrary>();
            library.name = this.name;
            library.Configure(libraryEntries);
            return library;
        }
    }
}
#endif
