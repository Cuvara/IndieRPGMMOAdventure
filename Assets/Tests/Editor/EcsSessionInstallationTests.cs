#if CUVARA_DOTS && CUVARA_DOTS_VCONTAINER && CUVARA_DOTS_MESSAGEPIPE && CUVARA_NETCODE && CUVARA_SHARED_GAMELOGIC
namespace Tests.Editor
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
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

        private EcsSessionInstallation NewInstallation(IEcsWorldProvider worldProvider = null) =>
            new EcsSessionInstallation(
                worldProvider ?? new DefaultEcsWorldProvider(this.world),
                this.client,
                this.predictor,
                this.provider,
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
        public void Prewarm_CompletesIntoReady()
        {
            this.installation = this.NewInstallation();
            this.installation.TryInstall();

            Assert.That(this.installation.TryFinishPrewarm(out var disable), Is.True);

            Assert.That(disable, Is.False);
            Assert.That(this.installation.IsReady, Is.True);
        }

        [Test]
        public void Prewarm_Failure_AsksToDisableOnce()
        {
            this.provider.FailPrewarm = true;
            this.installation = this.NewInstallation();
            this.installation.TryInstall();

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

            public bool HoldPrewarm;
            public bool FailPrewarm;
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

                this.PrewarmTasks.Add(task);
                return task;
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
                var manager = this.world.EntityManager;
                using var netcode = manager.CreateEntityQuery(ComponentType.ReadOnly<NetworkEntityViewReference>());
                using var catalog = manager.CreateEntityQuery(ComponentType.ReadOnly<ViewConfigTableReference>());
                this.WorldStateAtRelease.Add(
                    (netcode.IsEmpty ? "netcode-gone" : "netcode-live") + "," +
                    (catalog.IsEmpty ? "catalog-gone" : "catalog-live"));
            }
        }
    }
}
#endif
