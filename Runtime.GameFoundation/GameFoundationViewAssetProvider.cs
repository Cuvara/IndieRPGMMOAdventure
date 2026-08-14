#if CUVARA_DOTS_UNIT_POOLING && CUVARA_DOTS_UNIT_RESOURCES && CUVARA_DOTS_UNITASK
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Cuvara.DOTS.Provisioning;
using Cysharp.Threading.Tasks;
using UniT.Pooling;
using UniT.ResourceManagement;
using UnityEngine;

namespace Cuvara.DOTS.GameFoundation
{
    /// <summary>
    /// <see cref="IViewAssetProvider"/> on top of the GameFoundation / UniT
    /// <see cref="IAssetsManager"/> + <see cref="IObjectPoolManager"/> pair.
    /// </summary>
    /// <remarks>
    /// <para>
    /// An adapter and nothing else. It owns no cache and no pool of its own — the pool it talks to
    /// is the same one <c>AudioService</c> and every other GameFoundation consumer uses, which is
    /// the point: a second pool over the same prefabs would double-instantiate and fight over
    /// recycling.
    /// </para>
    /// <para>
    /// The <c>_warm</c> set is bookkeeping, not a cache. <see cref="IObjectPoolManager"/> exposes
    /// no "is this key loaded" query, and the alternative — asking the pool to spawn and seeing
    /// whether it hitches — is not a question you can ask.
    /// </para>
    /// </remarks>
    public sealed class GameFoundationViewAssetProvider : IViewAssetProvider
    {
        private readonly IAssetsManager _assetsManager;
        private readonly IObjectPoolManager _objectPoolManager;
        private readonly HashSet<string> _warm = new HashSet<string>();

        public GameFoundationViewAssetProvider(IAssetsManager assetsManager, IObjectPoolManager objectPoolManager)
        {
            _assetsManager = assetsManager ?? throw new ArgumentNullException(nameof(assetsManager));
            _objectPoolManager = objectPoolManager ?? throw new ArgumentNullException(nameof(objectPoolManager));
        }

        public bool IsWarm(string key) => key != null && _warm.Contains(key);

        public async Task PrewarmAsync(string key, int count, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrEmpty(key)) return;
            if (count < 1) count = 1;

            await _objectPoolManager.LoadAsync(key, count, null, cancellationToken);
            _warm.Add(key);
        }

        public GameObject Acquire(string key, Vector3 position, Quaternion rotation, Transform parent = null)
        {
            if (string.IsNullOrEmpty(key)) return null;

            // The pool loads synchronously when the key is cold. Callers that care about the hitch
            // check IsWarm first; EntityViewSpawnSystem does.
            var instance = _objectPoolManager.Spawn(key, position, rotation, parent);
            _warm.Add(key);
            return instance;
        }

        public async Task<GameObject> AcquireAsync(string key, Vector3 position, Quaternion rotation, Transform parent = null, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrEmpty(key)) return null;

            if (!_warm.Contains(key)) await PrewarmAsync(key, 1, cancellationToken);
            return _objectPoolManager.Spawn(key, position, rotation, parent);
        }

        public void ReleaseInstance(GameObject instance)
        {
            if (instance != null) _objectPoolManager.Recycle(instance);
        }

        public void Release(string key)
        {
            if (string.IsNullOrEmpty(key) || !_warm.Remove(key)) return;

            // Order matters: live instances have to come home before the pool is torn down, and the
            // prefab can only be unloaded once nothing references it.
            _objectPoolManager.RecycleAll(key);
            _objectPoolManager.Unload(key);
            _assetsManager.Unload(key);
        }
    }
}
#endif
