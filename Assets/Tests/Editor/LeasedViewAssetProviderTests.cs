#if CUVARA_DOTS
namespace Tests.Editor
{
    using System;
    using System.Threading;
    using System.Threading.Tasks;
    using Cuvara.DOTS.Provisioning;
    using NUnit.Framework;
    using Scripts.DI.Dots;
    using UnityEngine;
    using UnityEngine.TestTools;

    /// <summary>
    /// The lease contract between the package's pooled provider and the host's prefab loader:
    /// one load per key, the handle held while any instance exists, released after the last
    /// instance returns, instances destroyed before assets on dispose.
    /// </summary>
    public sealed class LeasedViewAssetProviderTests
    {
        private SynchronizationContext savedContext;
        private FakeViewPrefabLoader loader;
        private Transform root;
        private LeasedViewAssetProvider provider;

        [SetUp]
        public void SetUp()
        {
            // Inline continuations: the editor's context would defer them to the next tick.
            this.savedContext = SynchronizationContext.Current;
            SynchronizationContext.SetSynchronizationContext(null);

            this.loader = new FakeViewPrefabLoader("goblin", "torch");
            this.root = new GameObject("[LeaseTestRoot]").transform;
            this.provider = new LeasedViewAssetProvider(
                new PooledViewAssetProvider(this.root, defaultPoolSize: 1, maxPoolSize: 8),
                this.loader);

            // Dispose warns about open leases in a few tests on purpose; Acquire-before-load warns too.
            LogAssert.ignoreFailingMessages = true;
        }

        [TearDown]
        public void TearDown()
        {
            this.provider.Dispose();
            this.loader.DestroyPrefabs();
            if (this.root != null) UnityEngine.Object.DestroyImmediate(this.root.gameObject);
            LogAssert.ignoreFailingMessages = false;
            SynchronizationContext.SetSynchronizationContext(this.savedContext);
        }

        private GameObject Acquire(string key = "goblin") => this.provider.Acquire(key, Vector3.zero, Quaternion.identity);

        private static void Observe(Task task)
        {
            if (task.IsFaulted) _ = task.Exception;
        }

        [Test]
        public void ConcurrentPrewarms_ShareOneLoad()
        {
            var first = this.provider.PrewarmAsync("goblin", 2);
            var second = this.provider.PrewarmAsync("goblin", 3);
            Assert.That(this.loader.LoadsOf("goblin"), Is.EqualTo(1), "load once per key");
            Assert.That(first.IsCompleted, Is.False);

            this.loader.Complete("goblin");

            Assert.That(first.Status, Is.EqualTo(TaskStatus.RanToCompletion));
            Assert.That(second.Status, Is.EqualTo(TaskStatus.RanToCompletion));
            Assert.That(this.provider.IsLoaded("goblin"), Is.True);
            Assert.That(this.provider.IsWarm("goblin"), Is.True);
            Assert.That(this.provider.PooledCount, Is.EqualTo(3), "the larger request wins");
            Assert.That(this.provider.LoadedKeyCount, Is.EqualTo(1));
        }

        [Test]
        public void AcquireBeforeLoad_ReturnsNull_AndCreatesNothing()
        {
            var task = this.provider.PrewarmAsync("goblin", 1);

            Assert.That(this.Acquire(), Is.Null, "no synchronous load, no hitch");
            Assert.That(this.provider.UnloadedAcquires, Is.EqualTo(1));
            Assert.That(this.provider.ActiveCount, Is.EqualTo(0));

            this.loader.Complete("goblin");
            Assert.That(task.Status, Is.EqualTo(TaskStatus.RanToCompletion));
            Assert.That(this.Acquire(), Is.Not.Null);
        }

        [Test]
        public void Release_WithNoActiveInstances_ReleasesTheHandleImmediately()
        {
            this.provider.PrewarmAsync("goblin", 2);
            this.loader.Complete("goblin");

            this.provider.Release("goblin");

            Assert.That(this.loader.ReleasesOf("goblin"), Is.EqualTo(1));
            Assert.That(this.provider.IsLoaded("goblin"), Is.False);
            Assert.That(this.provider.PooledCount, Is.EqualTo(0));
            Assert.That(this.provider.PendingReleaseCount, Is.EqualTo(0));
            Assert.That(this.Acquire(), Is.Null, "a released key cannot instantiate a stale prefab");
        }

        [Test]
        public void Release_WithActiveInstances_DefersTheHandleUntilTheLastReturn()
        {
            this.provider.PrewarmAsync("goblin", 2);
            this.loader.Complete("goblin");
            var a = this.Acquire();
            var b = this.Acquire();

            this.provider.Release("goblin");

            Assert.That(this.loader.ReleasesOf("goblin"), Is.EqualTo(0), "views are on screen; the prefab stays");
            Assert.That(this.provider.PendingReleaseCount, Is.EqualTo(1));
            Assert.That(this.provider.PooledCount, Is.EqualTo(0), "but the pooled instances went now");
            Assert.That(a == null, Is.False);

            this.provider.ReleaseInstance(a);
            Assert.That(this.loader.ReleasesOf("goblin"), Is.EqualTo(0), "one still out");
            Assert.That(a == null, Is.True, "returned into a released key: destroyed, not pooled");

            this.provider.ReleaseInstance(b);
            Assert.That(this.loader.ReleasesOf("goblin"), Is.EqualTo(1), "last instance back: handle released");
            Assert.That(b == null, Is.True);
            Assert.That(this.provider.IsLoaded("goblin"), Is.False);
            Assert.That(this.provider.PendingReleaseCount, Is.EqualTo(0));
            Assert.That(this.provider.ActiveCount, Is.EqualTo(0));
        }

        [Test]
        public void RewarmDuringADeferredRelease_KeepsTheHandle()
        {
            this.provider.PrewarmAsync("goblin", 1);
            this.loader.Complete("goblin");
            var a = this.Acquire();
            this.provider.Release("goblin");
            Assert.That(this.provider.PendingReleaseCount, Is.EqualTo(1));

            var rewarm = this.provider.PrewarmAsync("goblin", 2);

            Assert.That(rewarm.Status, Is.EqualTo(TaskStatus.RanToCompletion), "the prefab is still held; no load needed");
            Assert.That(this.loader.LoadsOf("goblin"), Is.EqualTo(1));
            Assert.That(this.provider.PendingReleaseCount, Is.EqualTo(0));
            Assert.That(this.provider.PooledCount, Is.EqualTo(2));

            this.provider.ReleaseInstance(a);
            Assert.That(this.loader.ReleasesOf("goblin"), Is.EqualTo(0), "the key is wanted again");
            Assert.That(this.provider.IsLoaded("goblin"), Is.True);
        }

        [Test]
        public void ReleaseWhileLoading_ReleasesOnCompletion_AndFailsTheWaiter()
        {
            var task = this.provider.PrewarmAsync("goblin", 1);
            this.provider.Release("goblin");
            Assert.That(this.loader.ReleasesOf("goblin"), Is.EqualTo(0), "nothing loaded yet to release");

            this.loader.Complete("goblin");
            Observe(task);

            Assert.That(task.IsCanceled || task.IsFaulted, Is.True, "the waiter learns its key was released");
            Assert.That(this.loader.ReleasesOf("goblin"), Is.EqualTo(1));
            Assert.That(this.provider.IsLoaded("goblin"), Is.False);
            Assert.That(this.provider.PooledCount, Is.EqualTo(0));
        }

        [Test]
        public void LoadFailure_Propagates_AndTheNextPrewarmRetries()
        {
            var first = this.provider.PrewarmAsync("goblin", 1);
            this.loader.Fault("goblin");
            Observe(first);
            Assert.That(first.IsFaulted, Is.True);
            Assert.That(this.provider.IsLoaded("goblin"), Is.False);

            var second = this.provider.PrewarmAsync("goblin", 1);
            Assert.That(this.loader.LoadsOf("goblin"), Is.EqualTo(2), "a faulted load is not cached");
            this.loader.Complete("goblin");
            Assert.That(second.Status, Is.EqualTo(TaskStatus.RanToCompletion));
            Assert.That(this.loader.ReleasesOf("goblin"), Is.EqualTo(0), "nothing was ever released for a load that failed");
        }

        [Test]
        public void UnknownKey_FailsThePrewarm_AndIsNotProvidable()
        {
            Assert.That(this.provider.CanProvide("dragon"), Is.False);
            Assert.That(this.provider.CanProvide("goblin"), Is.True);

            var task = this.provider.PrewarmAsync("dragon", 1);
            Observe(task);
            Assert.That(task.IsFaulted, Is.True);
            Assert.That(this.provider.LoadedKeyCount, Is.EqualTo(0));
        }

        [Test]
        public void Dispose_DestroysInstancesThenReleasesEveryHandle()
        {
            this.provider.PrewarmAsync("goblin", 2);
            this.provider.PrewarmAsync("torch", 1);
            this.loader.Complete("goblin");
            this.loader.Complete("torch");
            var live = this.Acquire();
            Assert.That(this.provider.Pool.TotalInstanceCount, Is.EqualTo(3));

            this.provider.Dispose();

            Assert.That(live == null, Is.True, "acquired instances are the provider's and go with it");
            Assert.That(this.provider.Pool.TotalInstanceCount, Is.EqualTo(0));
            Assert.That(this.root.childCount, Is.EqualTo(0), "the caller-owned root is emptied, not destroyed");
            Assert.That(this.root == null, Is.False);
            Assert.That(this.loader.ReleasesOf("goblin"), Is.EqualTo(1));
            Assert.That(this.loader.ReleasesOf("torch"), Is.EqualTo(1));
            Assert.That(this.provider.LoadedKeyCount, Is.EqualTo(0));

            Assert.DoesNotThrow(() => this.provider.Dispose());
            Assert.Throws<ObjectDisposedException>(() => this.Acquire());
            Assert.DoesNotThrow(() => this.provider.ReleaseInstance(live));
            Assert.DoesNotThrow(() => this.provider.Release("goblin"));
        }

        [Test]
        public void AcquireAsync_LoadsFirst_ThenAcquires()
        {
            var task = this.provider.AcquireAsync("torch", Vector3.one, Quaternion.identity);
            Assert.That(task.IsCompleted, Is.False);
            Assert.That(this.loader.LoadsOf("torch"), Is.EqualTo(1));

            this.loader.Complete("torch");

            Assert.That(task.Status, Is.EqualTo(TaskStatus.RanToCompletion));
            Assert.That(task.Result, Is.Not.Null);
            Assert.That(this.provider.ActiveCount, Is.EqualTo(1));
            Assert.That(this.provider.IsLoaded("torch"), Is.True, "the handle is held for the acquired instance");

            // Never prewarmed, so the key has no pool: the package destroys the return rather than
            // parking it (its documented cap rule). The lease is unaffected — the prefab stays held.
            this.provider.ReleaseInstance(task.Result);
            Assert.That(this.provider.ActiveCount, Is.EqualTo(0));
            Assert.That(this.provider.IsLoaded("torch"), Is.True);
            Assert.That(this.loader.ReleasesOf("torch"), Is.EqualTo(0));
        }
    }
}
#endif
