#if CUVARA_DOTS
namespace Scripts.DI.Dots
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using Cuvara.DOTS.Provisioning;
    using UnityEngine;

    /// <summary>
    /// The production <see cref="IViewAssetProvider"/>: the package's
    /// <see cref="PooledViewAssetProvider"/> for pooling, an <see cref="IViewPrefabLoader"/>
    /// (Addressables in the game) for the prefabs, and this class for the lease between them.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Lease contract, one rule per line.</b>
    /// </para>
    /// <list type="bullet">
    /// <item>A key's prefab is loaded <b>once</b>, on the first <see cref="PrewarmAsync"/> or
    /// <see cref="AcquireAsync"/>; concurrent callers share the same load. The handle is held for
    /// as long as any instance of the key exists, pooled or acquired.</item>
    /// <item><see cref="Release"/> drops the pooled instances at once and releases the handle
    /// <b>only when no acquired instance of the key remains</b>. If some are still out, the
    /// release is deferred and completes on the return of the last one — a view standing on
    /// screen never has its prefab unloaded underneath it.</item>
    /// <item><see cref="Acquire"/> never loads: a key whose prefab is not loaded returns
    /// <c>null</c> (counted in <see cref="UnloadedAcquires"/>) rather than hitching the main
    /// thread on a synchronous Addressables wait. Warm the catalog's keys first — the bridge
    /// does — or use <see cref="AcquireAsync"/>.</item>
    /// <item>After the handle is released the key reads as not loaded here, whatever the pool
    /// still has registered, so a stale prefab reference is never instantiated. Re-warming
    /// re-loads and re-registers; the pool's replace-key rule then discards nothing, because
    /// its pool for the key is already empty.</item>
    /// <item><see cref="Dispose"/> disposes the pool first (every instance destroyed under the
    /// pool's ownership contract) and then releases every handle — instances before assets,
    /// always.</item>
    /// </list>
    /// <para>
    /// Lifetime is the session's: the root scope owns the instance and disposes it with the
    /// container; a scene's <c>DotsWorldBridge</c> calls <see cref="Release"/> for its catalog's
    /// keys on teardown so a map transfer drops the assets the next map does not share.
    /// </para>
    /// <para>Main thread only.</para>
    /// </remarks>
    public sealed class LeasedViewAssetProvider : IViewAssetProvider, IDisposable
    {
        private sealed class Lease
        {
            public Task<GameObject> Loading;
            public GameObject Prefab;
            public bool ReleasePending;
        }

        private readonly PooledViewAssetProvider pool;
        private readonly IViewPrefabLoader loader;
        private readonly Dictionary<string, Lease> leases = new Dictionary<string, Lease>();
        private readonly HashSet<string> warnedUnloaded = new HashSet<string>();
        private bool disposed;

        public LeasedViewAssetProvider(PooledViewAssetProvider pool, IViewPrefabLoader loader)
        {
            this.pool = pool ?? throw new ArgumentNullException(nameof(pool));
            this.loader = loader ?? throw new ArgumentNullException(nameof(loader));
        }

        /// <summary>The pool this provider leases prefabs into. Diagnostics and tests.</summary>
        public PooledViewAssetProvider Pool => this.pool;

        /// <summary>Keys whose prefab is loaded and whose handle is held.</summary>
        public int LoadedKeyCount
        {
            get
            {
                var count = 0;
                foreach (var lease in this.leases.Values)
                {
                    if (lease.Prefab != null) count++;
                }

                return count;
            }
        }

        /// <summary>Keys whose handle release is waiting on acquired instances to return.</summary>
        public int PendingReleaseCount
        {
            get
            {
                var count = 0;
                foreach (var lease in this.leases.Values)
                {
                    if (lease.ReleasePending) count++;
                }

                return count;
            }
        }

        /// <summary><see cref="Acquire"/> calls refused because the key's prefab was not loaded.</summary>
        public int UnloadedAcquires { get; private set; }

        public int ActiveCount => this.pool.ActiveCount;

        public int PooledCount => this.pool.PooledCount;

        /// <summary>Whether the key's prefab is loaded and its handle held — the honest "can Acquire succeed" answer.</summary>
        public bool IsLoaded(string key) =>
            key != null && this.leases.TryGetValue(key, out var lease) && lease.Prefab != null && !lease.ReleasePending;

        /// <summary>The validator's <c>prefabExists</c>: whether the loader knows the key, loaded or not.</summary>
        public bool CanProvide(string key) => key != null && this.loader.CanLoad(key);

        public async Task PrewarmAsync(string key, int count, CancellationToken cancellationToken = default)
        {
            this.ThrowIfDisposed();
            if (string.IsNullOrEmpty(key)) throw new ArgumentException("Key cannot be empty.", nameof(key));
            cancellationToken.ThrowIfCancellationRequested();

            await this.EnsureLoadedAsync(key, cancellationToken);
            await this.pool.PrewarmAsync(key, count, cancellationToken);
        }

        public bool IsWarm(string key) => this.IsLoaded(key) && this.pool.IsWarm(key);

        public GameObject Acquire(string key, Vector3 position, Quaternion rotation, Transform parent = null)
        {
            this.ThrowIfDisposed();
            if (!this.IsLoaded(key))
            {
                this.UnloadedAcquires++;
                if (key != null && this.warnedUnloaded.Add(key))
                {
                    Debug.LogWarning(
                        $"[LeasedViewAssetProvider] Acquire('{key}') before its prefab was loaded; returning null. " +
                        "Prewarm the catalog's keys before spawning, or use AcquireAsync. Reported once per key.");
                }

                return null;
            }

            return this.pool.Acquire(key, position, rotation, parent);
        }

        public async Task<GameObject> AcquireAsync(string key, Vector3 position, Quaternion rotation, Transform parent = null, CancellationToken cancellationToken = default)
        {
            this.ThrowIfDisposed();
            if (string.IsNullOrEmpty(key)) return null;
            cancellationToken.ThrowIfCancellationRequested();

            await this.EnsureLoadedAsync(key, cancellationToken);
            return this.pool.Acquire(key, position, rotation, parent);
        }

        public void ReleaseInstance(GameObject instance)
        {
            if (this.disposed || instance is null) return;

            // Key first: the pool forgets a destroyed-on-return instance, and a key whose release
            // is pending needs to hear about this return either way.
            var known = this.pool.TryGetKey(instance, out var key);
            this.pool.ReleaseInstance(instance);

            if (known) this.CompletePendingReleaseIfIdle(key);
        }

        /// <summary>
        /// Drops the key's pooled instances now and its asset handle as soon as no acquired
        /// instance remains — immediately if none is out, otherwise on the last return.
        /// </summary>
        public void Release(string key)
        {
            if (this.disposed || key == null) return;

            this.pool.Release(key);

            if (!this.leases.TryGetValue(key, out var lease)) return;

            if (lease.Loading != null && !lease.Loading.IsCompleted)
            {
                // Still loading: mark it so the load's completion releases straight away.
                lease.ReleasePending = true;
                return;
            }

            lease.ReleasePending = true;
            this.CompletePendingReleaseIfIdle(key);
        }

        /// <summary>
        /// Instances first, assets after: disposes the pool (destroying every instance it owns per
        /// its <c>OutstandingLeasePolicy</c>), then releases every held handle. Idempotent.
        /// </summary>
        public void Dispose()
        {
            if (this.disposed) return;
            this.disposed = true;

            this.pool.Dispose();

            foreach (var pair in this.leases)
            {
                if (pair.Value.Prefab != null) this.loader.Release(pair.Key);
            }

            this.leases.Clear();
        }

        private Task<GameObject> EnsureLoadedAsync(string key, CancellationToken cancellationToken)
        {
            if (!this.leases.TryGetValue(key, out var lease))
            {
                lease = new Lease();
                this.leases[key] = lease;
            }

            if (lease.Prefab != null)
            {
                // A pending release that was never completed because instances were out; the key
                // is wanted again, so the handle simply stays.
                lease.ReleasePending = false;
                return Task.FromResult(lease.Prefab);
            }

            if (lease.Loading == null)
            {
                lease.ReleasePending = false;
                lease.Loading = this.LoadAndRegisterAsync(key, lease, cancellationToken);
            }

            return lease.Loading;
        }

        private async Task<GameObject> LoadAndRegisterAsync(string key, Lease lease, CancellationToken cancellationToken)
        {
            GameObject prefab;
            try
            {
                prefab = await this.loader.LoadAsync(key, cancellationToken);
            }
            catch
            {
                // The next caller retries the load rather than awaiting a faulted task forever.
                lease.Loading = null;
                throw;
            }

            if (prefab == null)
            {
                lease.Loading = null;
                throw new InvalidOperationException($"[LeasedViewAssetProvider] Loader returned no prefab for view key '{key}'.");
            }

            if (this.disposed)
            {
                // Disposed while loading: nothing may hold the asset now.
                this.loader.Release(key);
                lease.Loading = null;
                throw new ObjectDisposedException(nameof(LeasedViewAssetProvider));
            }

            lease.Prefab = prefab;
            lease.Loading = null;
            this.pool.RegisterPrefab(key, prefab);

            if (lease.ReleasePending)
            {
                // Released while loading: honour it now that there is something to release.
                this.CompletePendingReleaseIfIdle(key);
                throw new OperationCanceledException($"View key '{key}' was released while its prefab was loading.");
            }

            return prefab;
        }

        private void CompletePendingReleaseIfIdle(string key)
        {
            if (!this.leases.TryGetValue(key, out var lease) || !lease.ReleasePending || lease.Prefab == null) return;
            if (this.pool.GetActiveCount(key) > 0) return;

            // Pooled instances may have been re-created by a prewarm racing the release; they are
            // the pool's and go before the asset does.
            this.pool.Release(key);

            lease.Prefab = null;
            lease.ReleasePending = false;
            this.loader.Release(key);
            this.leases.Remove(key);
        }

        private void ThrowIfDisposed()
        {
            if (this.disposed) throw new ObjectDisposedException(nameof(LeasedViewAssetProvider));
        }
    }
}
#endif
