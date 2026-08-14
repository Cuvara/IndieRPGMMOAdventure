using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Cuvara.DOTS.Provisioning;
using UnityEngine;

namespace Cuvara.DOTS.Tests
{
    /// <summary>
    /// Hands out real (empty) GameObjects and counts recycles, so the lifecycle test can assert on
    /// the thing that actually matters: every spawned view comes back.
    /// </summary>
    /// <remarks>
    /// Instances are destroyed rather than pooled — this fake stands in for a pool, it is not one.
    /// A copy of it exists in the edit-mode test assembly; see the note there.
    /// </remarks>
    internal sealed class FakeViewAssetProvider : IViewAssetProvider
    {
        private readonly HashSet<string> _warm = new HashSet<string>();

        public int AcquireCount;
        public int ReleaseInstanceCount;
        public bool WarmEverything = true;

        public Task PrewarmAsync(string key, int count, CancellationToken cancellationToken = default)
        {
            _warm.Add(key);
            return Task.CompletedTask;
        }

        public bool IsWarm(string key) => WarmEverything || _warm.Contains(key);

        public GameObject Acquire(string key, Vector3 position, Quaternion rotation, Transform parent = null)
        {
            AcquireCount++;
            var instance = new GameObject(key);
            instance.transform.SetPositionAndRotation(position, rotation);
            if (parent != null) instance.transform.SetParent(parent, true);
            return instance;
        }

        public Task<GameObject> AcquireAsync(string key, Vector3 position, Quaternion rotation, Transform parent = null, CancellationToken cancellationToken = default)
            => Task.FromResult(Acquire(key, position, rotation, parent));

        public void ReleaseInstance(GameObject instance)
        {
            ReleaseInstanceCount++;
            if (instance != null) Object.DestroyImmediate(instance);
        }

        public void Release(string key) => _warm.Remove(key);
    }
}
