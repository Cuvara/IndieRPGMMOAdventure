#if CUVARA_DOTS && CUVARA_DOTS_VCONTAINER && CUVARA_DOTS_MESSAGEPIPE && CUVARA_NETCODE && CUVARA_SHARED_GAMELOGIC
namespace Tests.Editor
{
    using Cuvara.DOTS.Configuration;
    using Cuvara.DOTS.Groups;
    using Cuvara.DOTS.Netcode;
    using Cuvara.DOTS.Netcode.Prediction;
    using Cuvara.DOTS.Simulation;
    using Cuvara.DOTS.Views;
    using Cuvara.Netcode.Prediction;
    using Cuvara.Netcode.World;
    using NUnit.Framework;
    using Scripts.DI.Dots;
    using Shared.GameLogic.Components;
    using Unity.Entities;
    using UnityEngine;

    /// <summary>
    /// Proves the scene-scoped half — the install/uninstall sequence <c>DotsWorldBridge</c> runs —
    /// against a throwaway world: the group tree comes up, the singletons exist, and teardown in
    /// the documented order (prediction → netcode → catalog) leaves no singleton and no live blob
    /// behind.
    /// </summary>
    public class DotsBootstrapLifecycleTests
    {
        private World world;
        private PrimitiveViewAssetProvider provider;
        private EntityViewRegistry registry;
        private ViewConfigCatalog catalog;
        private ViewArchetypeLibrary library;
        private ViewConfig config;
        private DotsEntityView view;
        private LocalMovePredictor predictor;

        [SetUp]
        public void SetUp()
        {
            this.world = new World("DotsBootstrapLifecycleTests");
            this.provider = new PrimitiveViewAssetProvider(null);
            this.registry = new EntityViewRegistry(this.provider);

            this.config = ScriptableObject.CreateInstance<ViewConfig>();
            this.config.Configure(DotsViewArchetypes.PlayerLocal, pool: 2);
            this.library = ScriptableObject.CreateInstance<ViewArchetypeLibrary>();
            this.library.Configure(new ViewArchetypeLibrary.Entry
            {
                Name = DotsViewArchetypes.PlayerLocal,
                Config = this.config,
            });
            this.catalog = new ViewConfigCatalog();
            this.catalog.Build(this.library);

            var resolver = new TypeArchetypeResolver(
                localArchetype: DotsViewArchetypes.PlayerLocal,
                unknownArchetype: null,
                new TypeArchetypeResolver.Rule(DotsViewArchetypes.ServerKindPlayer, DotsViewArchetypes.PlayerLocal));
            this.view = new DotsEntityView(this.catalog, resolver, SnapshotSpaceMapping.XZPlane);

            this.predictor = new LocalMovePredictor(new PredictionSettings(
                GameConstants.DefaultTickRate,
                5f,
                new MapBounds(0f, 0f, GameConstants.DefaultMapWidth, GameConstants.DefaultMapHeight)));
        }

        [TearDown]
        public void TearDown()
        {
            if (this.world is { IsCreated: true })
            {
                this.world.Dispose();
            }

            this.catalog?.Dispose();
            if (this.library != null)
            {
                Object.DestroyImmediate(this.library);
            }

            if (this.config != null)
            {
                Object.DestroyImmediate(this.config);
            }
        }

        private void InstallAll()
        {
            DotsSimulationBootstrap.InstallSimulationSystems(this.world);
            DotsViewBootstrap.Install(this.world, this.registry);
            this.catalog.Install(this.world);
            DotsNetcodeBootstrap.Install(this.world, this.view);
            DotsPredictionBootstrap.Install(this.world, this.predictor, new WorldState());
        }

        [Test]
        public void Install_CreatesTheExpectedGroupTree()
        {
            this.InstallAll();

            Assert.That(this.world.GetExistingSystemManaged<GameplaySystemGroup>(), Is.Not.Null);
            Assert.That(this.world.GetExistingSystemManaged<MovementSystemGroup>(), Is.Not.Null);
            Assert.That(this.world.GetExistingSystemManaged<LifecycleSystemGroup>(), Is.Not.Null);
            Assert.That(this.world.GetExistingSystemManaged<ViewSystemGroup>(), Is.Not.Null);
            Assert.That(this.world.GetExistingSystemManaged<ViewInterpolationGroup>(), Is.Not.Null);
            Assert.That(this.world.GetExistingSystemManaged<NetcodeSystemGroup>(), Is.Not.Null);
            Assert.That(this.world.GetExistingSystemManaged<SnapshotApplyGroup>(), Is.Not.Null);
            Assert.That(this.world.GetExistingSystemManaged<PredictionSystemGroup>(), Is.Not.Null);
        }

        [Test]
        public void Install_PublishesTheSingletons()
        {
            this.InstallAll();

            Assert.That(this.SingletonExists<EntityViewRegistryReference>(), Is.True);
            Assert.That(this.SingletonExists<ViewConfigTableReference>(), Is.True);
            Assert.That(this.SingletonExists<NetworkEntityViewReference>(), Is.True);
            Assert.That(this.SingletonExists<InterpolationSettings>(), Is.True);
            Assert.That(this.SingletonExists<LocalPredictionReference>(), Is.True);
        }

        [Test]
        public void Install_IsIdempotent()
        {
            this.InstallAll();
            this.InstallAll();

            // A second install must replace, not duplicate — a doubled singleton makes every
            // GetSingleton in the package throw.
            using var query = this.world.EntityManager.CreateEntityQuery(
                ComponentType.ReadOnly<NetworkEntityViewReference>());
            Assert.That(query.CalculateEntityCount(), Is.EqualTo(1));
        }

        [Test]
        public void Uninstall_InDocumentedOrder_RemovesEverySingleton_AndFreesTheBlob()
        {
            this.InstallAll();

            // The order DotsWorldBridge.OnDestroy runs: prediction hands transforms back first,
            // then the adapter goes, then the catalog blob is freed.
            DotsPredictionBootstrap.Uninstall(this.world);
            DotsNetcodeBootstrap.Uninstall(this.world);
            DotsViewBootstrap.Uninstall(this.world);

            Assert.That(this.SingletonExists<LocalPredictionReference>(), Is.False);
            Assert.That(this.SingletonExists<NetworkEntityViewReference>(), Is.False);
            Assert.That(this.SingletonExists<InterpolationSettings>(), Is.False);
            Assert.That(this.SingletonExists<EntityViewRegistryReference>(), Is.False);

            this.catalog.Dispose();
            Assert.That(this.catalog.Table.IsCreated, Is.False, "Dispose must free the blob — anything else leaks it.");
        }

        private bool SingletonExists<T>() where T : IComponentData
        {
            using var query = this.world.EntityManager.CreateEntityQuery(ComponentType.ReadOnly<T>());
            return !query.IsEmpty;
        }
    }
}
#endif
