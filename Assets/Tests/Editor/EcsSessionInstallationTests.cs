#if CUVARA_DOTS && CUVARA_DOTS_VCONTAINER && CUVARA_DOTS_MESSAGEPIPE && CUVARA_NETCODE && CUVARA_SHARED_GAMELOGIC
namespace Tests.Editor
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Collections;
    using System.Linq;
    using System.Threading.Tasks;
    using Cuvara.DOTS.Configuration;
    using Cuvara.DOTS.Netcode;
    using Cuvara.DOTS.Netcode.Prediction;
    using Cuvara.DOTS.Provisioning;
    using Cuvara.DOTS.Views;
    using Cuvara.Netcode.Client;
    using Cuvara.Netcode.Codec;
    using Cuvara.Netcode.Prediction;
    using NUnit.Framework;
    using Scripts.DI.Dots;
    using Shared.GameLogic.Components;
    using Unity.Entities;
    using UnityEngine;
    using UnityEngine.TestTools;
    using static Tests.Editor.ScriptedNetcode;

    /// <summary>
    /// The real install and teardown code of <c>DotsWorldBridge</c> (it lives in
    /// <see cref="EcsSessionInstallation"/>) over a throwaway world and a never-connected
    /// <see cref="NetworkClient"/>. The bridge itself stays out of EditMode; this class does not.
    /// </summary>
    /// <remarks>
    /// If the teardown order changes, <see cref="Dispose_ReleasesAssetKeys_OnlyAfterNetcodeAndCatalogAreUninstalled"/>
    /// fails: the recording provider reads the world at the moment of each <c>Release</c>. If
    /// dispose stops reversing install, <see cref="Dispose_RemovesEverySessionSingleton_AndKeepsTheRootOnes"/> fails.
    /// Swapping any two adjacent teardown steps fails <see cref="Dispose_RunsTheTeardownSteps_InExactOrder"/>,
    /// which compares the whole recorded sequence (it reports the first index that differs).
    /// The prewarm shutdown tests (<c>Dispose_*Prewarm*</c>) fail if a cancelled key leaves a warm key behind,
    /// a real fault goes unlogged or is logged twice, or an expected cancellation is logged.
    /// </remarks>
    public sealed class EcsSessionInstallationTests
    {
        private World world;
        private RecordingProvider provider;
        private EntityViewRegistry registry;
        private NetworkClient client;
        private LocalMovePredictor predictor;
        private EcsSessionInstallation installation;

        [SetUp]
        public void SetUp()
        {
            this.world = new World("EcsSessionInstallationTests");
            this.provider = new RecordingProvider(this.world);
            this.registry = new EntityViewRegistry(this.provider);
            // The root scope's half (RegisterDots): never installed or removed by a session.
            DotsViewBootstrap.Install(this.world, this.registry);

            this.client = new NetworkClient(
                FastSettings(), new ScriptedFactory(), new JsonWireCodec(), new RecordingLog(), new StubAuth());
            this.predictor = new LocalMovePredictor(new PredictionSettings(
                GameConstants.DefaultTickRate,
                5f,
                new MapBounds(0f, 0f, GameConstants.DefaultMapWidth, GameConstants.DefaultMapHeight)));
        }

        [TearDown]
        public void TearDown()
        {
            this.installation?.Dispose();
            if (this.world is { IsCreated: true })
            {
                DotsViewBootstrap.Uninstall(this.world);
                this.world.Dispose();
            }
        }

        private EcsSessionInstallation NewInstallation(IEcsWorldProvider worldProvider = null, IViewAssetProvider assets = null) =>
            new EcsSessionInstallation(
                worldProvider ?? new DefaultEcsWorldProvider(this.world),
                this.client,
                this.predictor,
                assets ?? this.provider,
                new DotsViewLibraryReference { Asset = null, Mode = DotsViewProviderMode.Primitive },
                new EcsSessionInstallation.Settings { EnableCameraFollow = false });

        [Test]
        public void TryInstall_PublishesTheSessionSingletons()
        {
            this.installation = this.NewInstallation();

            Assert.That(this.installation.TryInstall(), Is.True);

            Assert.That(this.installation.IsInstalled, Is.True);
            Assert.That(this.installation.World, Is.SameAs(this.world));
            Assert.That(this.installation.Binder, Is.Not.Null);
            Assert.That(this.SingletonExists<ViewConfigTableReference>(), Is.True);
            Assert.That(this.SingletonExists<NetworkEntityViewReference>(), Is.True);
            Assert.That(this.SingletonExists<InterpolationSettings>(), Is.True);
            Assert.That(this.SingletonExists<LocalPredictionReference>(), Is.True);
            Assert.That(this.provider.PrewarmedKeys, Is.Not.Empty, "the catalog's keys are prewarmed");
        }

        [Test]
        public void Dispose_RemovesEverySessionSingleton_AndKeepsTheRootOnes()
        {
            this.installation = this.NewInstallation();
            this.installation.TryInstall();

            this.installation.Dispose();

            Assert.That(this.SingletonExists<LocalPredictionReference>(), Is.False);
            Assert.That(this.SingletonExists<NetworkEntityViewReference>(), Is.False);
            Assert.That(this.SingletonExists<InterpolationSettings>(), Is.False);
            Assert.That(this.SingletonExists<ViewConfigTableReference>(), Is.False);
            Assert.That(this.SingletonExists<EntityViewRegistryReference>(), Is.True,
                "the view bootstrap is root-scoped; a session must not uninstall it");
        }

        [Test]
        public void Dispose_ReleasesAssetKeys_OnlyAfterNetcodeAndCatalogAreUninstalled()
        {
            this.installation = this.NewInstallation();
            this.installation.TryInstall();

            this.installation.Dispose();

            Assert.That(this.provider.ReleasedKeys, Is.Not.Empty);
            Assert.That(this.provider.WorldStateAtRelease, Is.All.EqualTo("netcode-gone,catalog-gone"),
                "assets are released after the adapter's mirrors and the catalog singleton are gone");
        }

        [Test]
        public void Dispose_CancelsThePendingPrewarm()
        {
            this.provider.HoldPrewarm = true;
            this.installation = this.NewInstallation();
            this.installation.TryInstall();

            Assert.That(this.installation.TryFinishPrewarm(out var disable), Is.False, "prewarm still pending");
            Assert.That(disable, Is.False);
            Assert.That(this.provider.PrewarmTokens, Is.Not.Empty);
            Assert.That(this.provider.PrewarmTokens, Has.None.Matches<CancellationToken>(t => t.IsCancellationRequested));

            this.installation.Dispose();

            Assert.That(this.provider.PrewarmTokens, Has.All.Matches<CancellationToken>(t => t.IsCancellationRequested));
            Assert.That(this.provider.PrewarmTasks, Has.All.Matches<Task>(t => t.IsCanceled));
        }

        [Test]
        public void Dispose_RunsTheTeardownSteps_InExactOrder()
        {
            var steps = new List<string>();
            this.installation = this.NewInstallation();
            this.installation.TeardownRecorder = steps.Add;
            this.installation.TryInstall();

            this.installation.Dispose();

            Assert.That(this.provider.ReleasedKeys, Is.Not.Empty);
            var expected = new List<string>
            {
                "unsubscribe-reconnect",
                "dispose-spawn-subscription",
                "dispose-predicted-views",
                "uninstall-prediction",
                "uninstall-elevation",
                "uninstall-netcode",
                "uninstall-session-modules",
                "uninstall-catalog",
            };
            expected.AddRange(this.provider.ReleasedKeys.Select(key => "release:" + key));
            expected.Add("dispose-catalog");
            expected.Add("dispose-library");

            Assert.That(steps, Is.EqualTo(expected));
        }

        [UnityTest]
        public IEnumerator Dispose_WithOneKeyCancelled_LeavesNoWarmKeyAndLogsNothing()
        {
            const string held = DotsViewArchetypes.Mob;
            this.provider.HeldKeys.Add(held);
            this.installation = this.NewInstallation();
            this.installation.TryInstall();
            Assert.That(this.provider.WarmKeys, Is.Not.Empty, "the other keys completed");
            Assert.That(this.provider.WarmKeys, Does.Not.Contain(held), "the held key is still pending");

            this.installation.Dispose();
            yield return null;

            Assert.That(this.provider.TaskFor(held).IsCanceled, Is.True);
            Assert.That(this.provider.WarmKeys, Is.Empty, "every key is released, completed or cancelled");
            Assert.That(this.provider.ReleasedKeys.Count(k => k == held), Is.EqualTo(1));
            LogAssert.NoUnexpectedReceived();
        }

        [UnityTest]
        public IEnumerator Dispose_WithARealPrewarmFault_LogsItOnce()
        {
            const string faulted = DotsViewArchetypes.Mob;
            this.provider.HeldKeys.Add(faulted);
            this.installation = this.NewInstallation();
            this.installation.TryInstall();

            LogAssert.Expect(LogType.Exception, new System.Text.RegularExpressions.Regex("scripted key fault"));
            this.provider.Fault(faulted, new InvalidOperationException("scripted key fault"));
            yield return null;

            this.installation.Dispose();
            yield return null;

            LogAssert.NoUnexpectedReceived();
            Assert.That(this.provider.WarmKeys, Is.Empty);
        }

        [UnityTest]
        public IEnumerator Dispose_WhenAProviderIgnoresCancellation_ReleasesTheKeyAgainOnceItSettles()
        {
            const string late = DotsViewArchetypes.Mob;
            this.provider.HeldKeys.Add(late);
            this.provider.IgnoreCancellation = true;
            this.installation = this.NewInstallation();
            this.installation.TryInstall();

            this.installation.Dispose();
            Assert.That(this.provider.ReleasedKeys.Count(k => k == late), Is.EqualTo(1));

            this.provider.Complete(late);
            yield return null;

            Assert.That(this.provider.ReleasedKeys.Count(k => k == late), Is.EqualTo(2), "released again when it settled");
            Assert.That(this.provider.WarmKeys, Is.Empty);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void LateSettle_OfAnOldInstallation_DoesNotReleaseKeysTheNewInstallationClaimed()
        {
            var (loader, leased, root, saved) = this.NewSharedLeasedProvider();
            try
            {
                var shared = new EpochTokenIgnoringProvider(leased);
                var old = this.NewInstallation(null, shared);
                old.TryInstall();
                old.Dispose();

                // Scene reload: the next installation claims the same keys through the same provider.
                this.installation = this.NewInstallation(null, shared);
                this.installation.TryInstall();

                foreach (var key in loader.PendingKeys.ToList()) loader.Complete(key);

                Assert.That(loader.Releases, Is.Empty, "the old installation's late settle released nothing");
                Assert.That(leased.PooledCount, Is.GreaterThan(0));
                foreach (var key in loader.Loads.Distinct().ToList())
                {
                    Assert.That(leased.IsLoaded(key), Is.True, key);
                    Assert.That(leased.IsWarm(key), Is.True, key);
                }

                Assert.That(loader.Loads, Is.Not.Empty);
            }
            finally
            {
                this.installation?.Dispose();
                this.installation = null;
                leased.Dispose();
                loader.DestroyPrefabs();
                UnityEngine.Object.DestroyImmediate(root.gameObject);
                SynchronizationContext.SetSynchronizationContext(saved);
            }
        }

        [Test]
        public void LateSettle_WithAProviderWithoutClaimEpochs_KeepsThePlainRelease()
        {
            var (loader, leased, root, saved) = this.NewSharedLeasedProvider();
            try
            {
                var plain = new TokenIgnoringProvider(leased);
                var old = this.NewInstallation(null, plain);
                old.TryInstall();
                old.Dispose();
                this.installation = this.NewInstallation(null, plain);
                this.installation.TryInstall();

                foreach (var key in loader.PendingKeys.ToList()) loader.Complete(key);

                Assert.That(loader.Releases, Is.Not.Empty, "control: without epochs the old release still lands");
            }
            finally
            {
                this.installation?.Dispose();
                this.installation = null;
                leased.Dispose();
                loader.DestroyPrefabs();
                UnityEngine.Object.DestroyImmediate(root.gameObject);
                SynchronizationContext.SetSynchronizationContext(saved);
            }
        }

        [Test]
        public void SharedLoad_IsNotCancelledByTheOldInstallation_WhenTheNewOneClaimedTheKey()
        {
            var (loader, leased, root, saved) = this.NewSharedLeasedProvider();
            try
            {
                loader.HonourCancellation = true;
                var old = this.NewInstallation(null, leased);
                old.TryInstall();
                old.Dispose(); // cancels the old installation's token while every load is in flight

                this.installation = this.NewInstallation(null, leased);
                this.installation.TryInstall();

                foreach (var key in loader.PendingKeys.ToList()) loader.Complete(key);

                LogAssert.NoUnexpectedReceived();
                Assert.That(loader.Loads, Is.Not.Empty);
                foreach (var key in loader.Loads.Distinct().ToList())
                {
                    Assert.That(leased.IsLoaded(key), Is.True, key);
                    Assert.That(leased.IsWarm(key), Is.True, key);
                    Assert.That(loader.ReleasesOf(key), Is.EqualTo(0), key);
                }
            }
            finally
            {
                this.installation?.Dispose();
                this.installation = null;
                leased.Dispose();
                loader.DestroyPrefabs();
                UnityEngine.Object.DestroyImmediate(root.gameObject);
                SynchronizationContext.SetSynchronizationContext(saved);
            }
        }

        [Test]
        public void CancelledCaller_EndsItsOwnWait_WithoutCancellingTheSharedLoad()
        {
            var (loader, leased, root, saved) = this.NewSharedLeasedProvider();
            try
            {
                loader.HonourCancellation = true;
                using var cts = new CancellationTokenSource();
                var cancelled = leased.PrewarmAsync("K", 1, cts.Token);
                var other = leased.PrewarmAsync("K", 1);
                cts.Cancel();

                Assert.That(cancelled.IsCanceled, Is.True, "the cancelled caller's wait ended");
                Assert.That(other.IsCompleted, Is.False, "the other caller still waits");

                loader.Complete("K");

                Assert.That(other.IsCompletedSuccessfully, Is.True);
                Assert.That(leased.IsWarm("K"), Is.True);
                Assert.That(loader.LoadsOf("K"), Is.EqualTo(1));
            }
            finally
            {
                leased.Dispose();
                loader.DestroyPrefabs();
                UnityEngine.Object.DestroyImmediate(root.gameObject);
                SynchronizationContext.SetSynchronizationContext(saved);
            }
        }

        [Test]
        public void LastRelease_WhileTheLoadIsInFlight_ReleasesTheHandleWhenItCompletes()
        {
            var (loader, leased, root, saved) = this.NewSharedLeasedProvider();
            try
            {
                var waiting = leased.PrewarmAsync("K", 1);
                leased.Release("K");
                Assert.That(loader.ReleasesOf("K"), Is.EqualTo(0), "nothing to release yet");

                loader.Complete("K");

                Assert.That(loader.ReleasesOf("K"), Is.EqualTo(1), "handle released on completion");
                Assert.That(leased.IsLoaded("K"), Is.False);
                Assert.That(waiting.IsCanceled, Is.True);
            }
            finally
            {
                leased.Dispose();
                loader.DestroyPrefabs();
                UnityEngine.Object.DestroyImmediate(root.gameObject);
                SynchronizationContext.SetSynchronizationContext(saved);
            }
        }

        private (FakeViewPrefabLoader, LeasedViewAssetProvider, Transform, SynchronizationContext) NewSharedLeasedProvider()
        {
            // Inline continuations: the editor's context would defer them to the next tick.
            var saved = SynchronizationContext.Current;
            SynchronizationContext.SetSynchronizationContext(null);
            var loader = new FakeViewPrefabLoader();
            var root = new GameObject("[SharedLeaseRoot]").transform;
            var leased = new LeasedViewAssetProvider(
                new PooledViewAssetProvider(root, defaultPoolSize: 1, maxPoolSize: 8), loader);
            return (loader, leased, root, saved);
        }

        [Test]
        public void Prewarm_CompletesIntoReady()
        {
            this.installation = this.NewInstallation();
            this.installation.TryInstall();

            Assert.That(this.installation.TryFinishPrewarm(out var disable), Is.True);

            Assert.That(disable, Is.False);
            Assert.That(this.installation.IsReady, Is.True);
        }

        [UnityTest]
        public IEnumerator Prewarm_Failure_AsksToDisableOnce()
        {
            this.provider.FailPrewarm = true;
            this.installation = this.NewInstallation();
            this.installation.TryInstall();

            // Each key's fault is logged once by its observer, on the main thread's next pump.
            LogAssert.Expect(LogType.Exception, new System.Text.RegularExpressions.Regex("scripted prewarm failure"));
            for (var i = 1; i < this.provider.PrewarmedKeys.Count; i++)
            {
                LogAssert.Expect(LogType.Exception, new System.Text.RegularExpressions.Regex("scripted prewarm failure"));
            }

            yield return null;

            LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex("Prewarming the view catalog failed"));
            Assert.That(this.installation.TryFinishPrewarm(out var first), Is.False);
            Assert.That(first, Is.True);
            Assert.That(this.installation.TryFinishPrewarm(out var second), Is.False);
            Assert.That(second, Is.False, "the failure is reported once");
            Assert.That(this.installation.IsReady, Is.False);
        }

        [Test]
        public void SecondInstallAfterDispose_Works()
        {
            this.installation = this.NewInstallation();
            Assert.That(this.installation.TryInstall(), Is.True);
            this.installation.Dispose();

            // A scene reload: a new bridge builds a new installation over the same world.
            this.installation = this.NewInstallation();
            Assert.That(this.installation.TryInstall(), Is.True);
            Assert.That(this.SingletonExists<NetworkEntityViewReference>(), Is.True);
            Assert.That(this.SingletonExists<LocalPredictionReference>(), Is.True);

            this.installation.Dispose();
            Assert.That(this.SingletonExists<NetworkEntityViewReference>(), Is.False);
        }

        [Test]
        public void Dispose_IsIdempotent_AndSafeWhenNeverInstalled()
        {
            var never = this.NewInstallation();
            Assert.DoesNotThrow(never.Dispose);
            Assert.DoesNotThrow(never.Dispose);

            this.installation = this.NewInstallation();
            this.installation.TryInstall();
            this.installation.Dispose();
            Assert.DoesNotThrow(this.installation.Dispose);
        }

        [Test]
        public void TryInstall_WithNoWorld_WarnsAndReportsFailure()
        {
            this.installation = this.NewInstallation(new NoWorldProvider());

            LogAssert.Expect(LogType.Warning, "[DotsWorldBridge] no default ECS world — DOTS presentation disabled.");
            Assert.That(this.installation.TryInstall(), Is.False);
            Assert.That(this.installation.IsInstalled, Is.False);
            Assert.DoesNotThrow(this.installation.Dispose);
        }

        [Test]
        public void DefaultProvider_ReturnsItsFixedWorld()
        {
            Assert.That(new DefaultEcsWorldProvider(this.world).World, Is.SameAs(this.world));
        }

        [Test]
        public void DefaultProvider_WithNoDefaultWorld_ThrowsANamedException()
        {
            var saved = World.DefaultGameObjectInjectionWorld;
            World.DefaultGameObjectInjectionWorld = null;
            try
            {
                var ex = Assert.Throws<InvalidOperationException>(() => _ = new DefaultEcsWorldProvider().World);
                Assert.That(ex.Message, Does.Contain("DefaultGameObjectInjectionWorld"));
            }
            finally
            {
                World.DefaultGameObjectInjectionWorld = saved;
            }
        }

        private bool SingletonExists<T>() where T : IComponentData
        {
            using var query = this.world.EntityManager.CreateEntityQuery(ComponentType.ReadOnly<T>());
            return !query.IsEmpty;
        }

        private sealed class NoWorldProvider : IEcsWorldProvider
        {
            public World World => throw new InvalidOperationException("no world");
        }

        /// <summary>
        /// An asset provider that records what the installation asks of it, and reads the world at
        /// each <c>Release</c> to prove the order of the teardown.
        /// </summary>
        private sealed class RecordingProvider : IViewAssetProvider
        {
            private readonly World world;
            private readonly HashSet<string> warm = new HashSet<string>();

            public RecordingProvider(World world)
            {
                this.world = world;
            }

            private readonly Dictionary<string, TaskCompletionSource<bool>> held = new Dictionary<string, TaskCompletionSource<bool>>();
            private readonly Dictionary<string, Task> tasksByKey = new Dictionary<string, Task>();

            public bool HoldPrewarm;
            public bool FailPrewarm;

            /// <summary>Keys whose prewarm stays pending until completed, faulted or cancelled by the token.</summary>
            public readonly HashSet<string> HeldKeys = new HashSet<string>();

            /// <summary>A provider that does not honour the token; a held key then settles only when told to.</summary>
            public bool IgnoreCancellation;

            public IReadOnlyCollection<string> WarmKeys => this.warm;
            public readonly List<string> PrewarmedKeys = new List<string>();
            public readonly List<CancellationToken> PrewarmTokens = new List<CancellationToken>();
            public readonly List<Task> PrewarmTasks = new List<Task>();
            public readonly List<string> ReleasedKeys = new List<string>();
            public readonly List<string> WorldStateAtRelease = new List<string>();

            public Task PrewarmAsync(string key, int count, CancellationToken cancellationToken = default)
            {
                this.PrewarmedKeys.Add(key);
                this.PrewarmTokens.Add(cancellationToken);
                Task task;
                if (this.FailPrewarm)
                {
                    task = Task.FromException(new InvalidOperationException("scripted prewarm failure"));
                }
                else if (this.HoldPrewarm)
                {
                    var source = new TaskCompletionSource<bool>();
                    cancellationToken.Register(() => source.TrySetCanceled(cancellationToken));
                    task = source.Task;
                }
                else
                {
                    this.warm.Add(key);
                    task = Task.CompletedTask;
                }

                if (this.HeldKeys.Contains(key))
                {
                    var source = new TaskCompletionSource<bool>();
                    this.held[key] = source;
                    if (!this.IgnoreCancellation)
                    {
                        cancellationToken.Register(() => source.TrySetCanceled(cancellationToken));
                    }

                    task = source.Task;
                    this.warm.Remove(key);
                }

                this.tasksByKey[key] = task;
                this.PrewarmTasks.Add(task);
                return task;
            }

            public Task TaskFor(string key) => this.tasksByKey[key];

            public void Fault(string key, Exception exception) => this.held[key].SetException(exception);

            public void Complete(string key)
            {
                this.warm.Add(key);
                this.held[key].SetResult(true);
            }

            public bool IsWarm(string key) => this.warm.Contains(key);

            public GameObject Acquire(string key, Vector3 position, Quaternion rotation, Transform parent = null) => null;

            public Task<GameObject> AcquireAsync(
                string key, Vector3 position, Quaternion rotation, Transform parent = null,
                CancellationToken cancellationToken = default) => Task.FromResult<GameObject>(null);

            public void ReleaseInstance(GameObject instance)
            {
            }

            public void Release(string key)
            {
                this.ReleasedKeys.Add(key);
                this.warm.Remove(key);
                var manager = this.world.EntityManager;
                using var netcode = manager.CreateEntityQuery(ComponentType.ReadOnly<NetworkEntityViewReference>());
                using var catalog = manager.CreateEntityQuery(ComponentType.ReadOnly<ViewConfigTableReference>());
                this.WorldStateAtRelease.Add(
                    (netcode.IsEmpty ? "netcode-gone" : "netcode-live") + "," +
                    (catalog.IsEmpty ? "catalog-gone" : "catalog-live"));
            }
        }

        /// <summary>
        /// Forwards to a real <see cref="LeasedViewAssetProvider"/> but drops the cancellation
        /// token, so an old installation's prewarm settles after its <c>Dispose</c> — the case the
        /// claim epoch guards.
        /// </summary>
        private class TokenIgnoringProvider : IViewAssetProvider
        {
            protected readonly LeasedViewAssetProvider inner;

            public TokenIgnoringProvider(LeasedViewAssetProvider inner)
            {
                this.inner = inner;
            }

            public Task PrewarmAsync(string key, int count, CancellationToken cancellationToken = default) =>
                this.inner.PrewarmAsync(key, count, CancellationToken.None);

            public bool IsWarm(string key) => this.inner.IsWarm(key);

            public GameObject Acquire(string key, Vector3 position, Quaternion rotation, Transform parent = null) =>
                this.inner.Acquire(key, position, rotation, parent);

            public Task<GameObject> AcquireAsync(
                string key, Vector3 position, Quaternion rotation, Transform parent = null,
                CancellationToken cancellationToken = default) =>
                this.inner.AcquireAsync(key, position, rotation, parent, CancellationToken.None);

            public void ReleaseInstance(GameObject instance) => this.inner.ReleaseInstance(instance);

            public void Release(string key) => this.inner.Release(key);
        }

        /// <summary>The same, and says who claimed a key last — what the real provider does.</summary>
        private sealed class EpochTokenIgnoringProvider : TokenIgnoringProvider, IViewClaimEpochs
        {
            public EpochTokenIgnoringProvider(LeasedViewAssetProvider inner) : base(inner)
            {
            }

            public int ClaimEpoch(string key) => this.inner.ClaimEpoch(key);
        }
    }
}
#endif
