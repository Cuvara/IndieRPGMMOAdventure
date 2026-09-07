#if CUVARA_DOTS
namespace Scripts.DI.Dots
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using UnityEngine;
    using UnityEngine.AddressableAssets;
    using UnityEngine.ResourceManagement.AsyncOperations;

    /// <summary>
    /// <see cref="IViewPrefabLoader"/> over Addressables, resolving view keys through the
    /// <see cref="DotsViewLibraryAsset"/>'s prefab references. One handle per key, released
    /// exactly once.
    /// </summary>
    /// <remarks>
    /// The key is the pool key (<c>ViewConfig.ViewKey</c>), not an Addressables address: the
    /// library asset maps it to an <c>AssetReferenceGameObject</c>, so renaming an address in the
    /// Addressables groups never silently breaks a view — the reference follows the asset, and a
    /// missing reference fails validation at build time rather than at spawn time.
    /// </remarks>
    public sealed class AddressableViewPrefabLoader : IViewPrefabLoader
    {
        private readonly DotsViewLibraryAsset library;
        private readonly Dictionary<string, AsyncOperationHandle<GameObject>> handles = new Dictionary<string, AsyncOperationHandle<GameObject>>();

        public AddressableViewPrefabLoader(DotsViewLibraryAsset library)
        {
            this.library = library ?? throw new ArgumentNullException(nameof(library));
        }

        /// <summary>Handles currently held. Diagnostics and tests.</summary>
        public int HeldHandleCount => this.handles.Count;

        public bool CanLoad(string key) => this.library.HasPrefab(key);

        public async Task<GameObject> LoadAsync(string key, CancellationToken cancellationToken = default)
        {
            if (!this.library.TryGetPrefabReference(key, out var reference))
            {
                throw new KeyNotFoundException(
                    $"[AddressableViewPrefabLoader] View key '{key}' has no prefab reference in '{this.library.name}'. " +
                    "Add an entry for it in the DotsViewLibrary asset.");
            }

            if (this.handles.TryGetValue(key, out var existing))
            {
                // Already loaded (or loading): the provider guards this, but a second load would
                // be a second handle to release and this class promises exactly one.
                await existing.Task;
                return existing.Result;
            }

            var handle = Addressables.LoadAssetAsync<GameObject>(reference.RuntimeKey);
            this.handles[key] = handle;

            await handle.Task;

            if (cancellationToken.IsCancellationRequested || handle.Status != AsyncOperationStatus.Succeeded || handle.Result == null)
            {
                this.handles.Remove(key);
                if (handle.IsValid()) Addressables.Release(handle);
                cancellationToken.ThrowIfCancellationRequested();
                throw new InvalidOperationException(
                    $"[AddressableViewPrefabLoader] Addressables failed to load the prefab for view key '{key}' " +
                    $"(status {handle.Status}). Check the reference in '{this.library.name}' and that the asset is in an Addressables group.");
            }

            return handle.Result;
        }

        public void Release(string key)
        {
            if (key == null || !this.handles.TryGetValue(key, out var handle)) return;

            this.handles.Remove(key);
            if (handle.IsValid()) Addressables.Release(handle);
        }
    }
}
#endif
