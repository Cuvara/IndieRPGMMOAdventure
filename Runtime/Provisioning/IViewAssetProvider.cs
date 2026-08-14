using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace Cuvara.DOTS.Provisioning
{
    /// <summary>
    /// The only thing the DOTS view layer knows about asset loading and pooling.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is a seam, not a loader. Implementations are expected to be thin adapters over a
    /// pool and loader that already exist in the host project — the GameFoundation / UniT
    /// <c>IAssetsManager</c> + <c>IObjectPoolManager</c> pair in Cuvara projects. Writing a
    /// second pool behind this interface would put two owners on the same prefabs, and the
    /// two would fight over recycling.
    /// </para>
    /// <para>
    /// Deliberately expressed in <see cref="Task"/> rather than UniTask: the core assembly
    /// depends on nothing but Entities, Burst, Collections and Mathematics, and UniTask is not
    /// one of those. The GameFoundation adapter converts at the boundary.
    /// </para>
    /// </remarks>
    public interface IViewAssetProvider
    {
        /// <summary>
        /// Loads the prefab behind <paramref name="key"/> and makes at least
        /// <paramref name="count"/> instances available for a hitch-free first spawn.
        /// Must be safe to call repeatedly for the same key.
        /// </summary>
        Task PrewarmAsync(string key, int count, CancellationToken cancellationToken = default);

        /// <summary>True once <see cref="PrewarmAsync"/> has completed for the key.</summary>
        bool IsWarm(string key);

        /// <summary>
        /// Takes an instance from the pool. Only valid for a warm key — an implementation may
        /// load synchronously as a fallback, which hitches, so callers that care should check
        /// <see cref="IsWarm"/> first.
        /// </summary>
        GameObject Acquire(string key, Vector3 position, Quaternion rotation, Transform parent = null);

        /// <summary>Async counterpart of <see cref="Acquire"/>; warms the key first if needed.</summary>
        Task<GameObject> AcquireAsync(string key, Vector3 position, Quaternion rotation, Transform parent = null, CancellationToken cancellationToken = default);

        /// <summary>Returns a single instance to the pool. The instance must not be used afterwards.</summary>
        void ReleaseInstance(GameObject instance);

        /// <summary>
        /// Recycles every live instance of the key and drops the pooled ones plus the prefab.
        /// Called only when the last referencing chunk went away — see
        /// <see cref="ChunkViewProvisioner"/>.
        /// </summary>
        void Release(string key);
    }
}
