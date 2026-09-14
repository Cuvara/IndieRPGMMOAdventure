#if CUVARA_DOTS
namespace Tests.Editor
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using Scripts.DI.Dots;
    using UnityEngine;

    /// <summary>
    /// <see cref="IViewPrefabLoader"/> whose loads stay pending until the test completes or
    /// faults them, recording every load and release — so the lease contract of
    /// <see cref="LeasedViewAssetProvider"/> can be proven without Addressables.
    /// </summary>
    /// <remarks>
    /// Continuations run inline on <see cref="Complete"/> only with no
    /// <see cref="SynchronizationContext"/> installed; the tests clear it in <c>SetUp</c>.
    /// </remarks>
    internal sealed class FakeViewPrefabLoader : IViewPrefabLoader
    {
        private readonly Dictionary<string, TaskCompletionSource<GameObject>> pending = new Dictionary<string, TaskCompletionSource<GameObject>>();
        private readonly HashSet<string> known;

        public readonly List<string> Loads = new List<string>();
        public readonly List<string> Releases = new List<string>();
        public readonly List<GameObject> Prefabs = new List<GameObject>();

        public FakeViewPrefabLoader(params string[] knownKeys)
        {
            this.known = new HashSet<string>(knownKeys);
        }

        public int PendingCount => this.pending.Count;

        public bool CanLoad(string key) => key != null && (this.known.Count == 0 || this.known.Contains(key));

        public Task<GameObject> LoadAsync(string key, CancellationToken cancellationToken = default)
        {
            this.Loads.Add(key);
            if (!this.CanLoad(key)) throw new KeyNotFoundException(key);

            var source = new TaskCompletionSource<GameObject>();
            this.pending[key] = source;
            return source.Task;
        }

        public void Release(string key) => this.Releases.Add(key);

        /// <summary>Completes the pending load for the key with a fresh inactive prefab.</summary>
        public GameObject Complete(string key)
        {
            var prefab = new GameObject($"prefab:{key}");
            prefab.SetActive(false);
            this.Prefabs.Add(prefab);

            var source = this.pending[key];
            this.pending.Remove(key);
            source.SetResult(prefab);
            return prefab;
        }

        public void Fault(string key)
        {
            var source = this.pending[key];
            this.pending.Remove(key);
            source.SetException(new InvalidOperationException($"load of '{key}' failed"));
        }

        public int LoadsOf(string key) => this.Loads.FindAll(k => k == key).Count;

        public int ReleasesOf(string key) => this.Releases.FindAll(k => k == key).Count;

        public void DestroyPrefabs()
        {
            foreach (var prefab in this.Prefabs)
            {
                if (prefab != null) UnityEngine.Object.DestroyImmediate(prefab);
            }

            this.Prefabs.Clear();
        }
    }
}
#endif
