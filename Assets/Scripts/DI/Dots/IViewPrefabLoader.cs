#if CUVARA_DOTS
namespace Scripts.DI.Dots
{
    using System.Threading;
    using System.Threading.Tasks;
    using UnityEngine;

    /// <summary>
    /// Loads and releases the prefab behind a view key on behalf of
    /// <see cref="LeasedViewAssetProvider"/>. The Addressables implementation is
    /// <see cref="AddressableViewPrefabLoader"/>; tests substitute a fake.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The seam exists so the lease bookkeeping — load once per key, release the handle only after
    /// the pool has released every instance — can be proven without Addressables. It is the only
    /// place in the DOTS wiring that knows what an asset handle is.
    /// </para>
    /// <para>
    /// Main thread only, like everything that touches Unity objects. <see cref="LoadAsync"/> for a
    /// key already loaded must return the same prefab; the provider guarantees it never calls
    /// <see cref="Release"/> for a key it did not load, and never twice for one load.
    /// </para>
    /// </remarks>
    public interface IViewPrefabLoader
    {
        /// <summary>Whether this loader can produce a prefab for <paramref name="key"/> at all — the validator's <c>prefabExists</c>.</summary>
        bool CanLoad(string key);

        /// <summary>Loads the prefab for <paramref name="key"/>. Faults when the key is unknown or the asset failed to load.</summary>
        Task<GameObject> LoadAsync(string key, CancellationToken cancellationToken = default);

        /// <summary>Releases the handle a successful <see cref="LoadAsync"/> for <paramref name="key"/> acquired.</summary>
        void Release(string key);
    }
}
#endif
