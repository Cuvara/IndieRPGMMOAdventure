using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Cuvara.DOTS.Provisioning;
using UnityEngine;

namespace Cuvara.DOTS.Tests.Editor
{
    /// <summary>
    /// Records what <see cref="ChunkViewProvisioner"/> asked the provider to do.
    /// </summary>
    /// <remarks>
    /// Completes synchronously so the reference-count assertions are about ordering, not timing.
    /// A copy of this exists in the play-mode test assembly; the two test assemblies cannot
    /// reference each other, and a shared test-support assembly would be a third asmdef to carry
    /// for twenty lines.
    /// </remarks>
    internal sealed class RecordingViewAssetProvider : IViewAssetProvider
    {
        public readonly List<string> Prewarmed = new List<string>();
        public readonly List<string> Released = new List<string>();
        public readonly Dictionary<string, int> WarmCounts = new Dictionary<string, int>();

        public Task PrewarmAsync(string key, int count, CancellationToken cancellationToken = default)
        {
            Prewarmed.Add(key);
            WarmCounts[key] = count;
            return Task.CompletedTask;
        }

        public bool IsWarm(string key) => WarmCounts.ContainsKey(key);

        public GameObject Acquire(string key, Vector3 position, Quaternion rotation, Transform parent = null) => null;

        public Task<GameObject> AcquireAsync(string key, Vector3 position, Quaternion rotation, Transform parent = null, CancellationToken cancellationToken = default)
            => Task.FromResult<GameObject>(null);

        public void ReleaseInstance(GameObject instance)
        {
        }

        public void Release(string key)
        {
            Released.Add(key);
            WarmCounts.Remove(key);
        }
    }
}
