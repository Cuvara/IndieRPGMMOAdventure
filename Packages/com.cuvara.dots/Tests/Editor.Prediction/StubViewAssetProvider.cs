using System.Threading;
using System.Threading.Tasks;
using Cuvara.DOTS.Provisioning;
using UnityEngine;

namespace Cuvara.DOTS.Tests.Prediction
{
    /// <summary>
    /// Everything is warm and every acquire is a fresh empty GameObject named after its key.
    /// </summary>
    /// <remarks>
    /// A third copy of the same few lines — the play-mode and edit-mode test assemblies each hold
    /// one. Sharing it would mean a non-test assembly shipping a fake, or this assembly referencing
    /// a test assembly that is constrained on a different package; both are worse than the
    /// duplication. The adapter's own tests do not care about pooling behaviour at all, so this copy
    /// is the smallest of the three on purpose: it only has to make the view pipeline reachable.
    /// </remarks>
    internal sealed class StubViewAssetProvider : IViewAssetProvider
    {
        public Task PrewarmAsync(string key, int count, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public bool IsWarm(string key) => true;

        public GameObject Acquire(string key, Vector3 position, Quaternion rotation, Transform parent = null)
        {
            var instance = new GameObject(key);
            instance.transform.SetPositionAndRotation(position, rotation);
            if (parent != null) instance.transform.SetParent(parent, true);
            return instance;
        }

        public Task<GameObject> AcquireAsync(string key, Vector3 position, Quaternion rotation, Transform parent = null, CancellationToken cancellationToken = default)
            => Task.FromResult(Acquire(key, position, rotation, parent));

        public void ReleaseInstance(GameObject instance)
        {
            if (instance != null) Object.DestroyImmediate(instance);
        }

        public void Release(string key)
        {
        }
    }
}
