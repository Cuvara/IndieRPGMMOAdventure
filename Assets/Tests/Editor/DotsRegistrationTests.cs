#if CUVARA_DOTS && CUVARA_DOTS_VCONTAINER && CUVARA_DOTS_MESSAGEPIPE && CUVARA_NETCODE && CUVARA_SHARED_GAMELOGIC
namespace Tests.Editor
{
    using Cuvara.DOTS.Messaging;
    using Cuvara.DOTS.Provisioning;
    using Cuvara.DOTS.Simulation;
    using Cuvara.DOTS.Views;
    using Cuvara.Netcode.Prediction;
    using NUnit.Framework;
    using Scripts.DI.Dots;
    using Unity.Entities;
    using VContainer;

    /// <summary>
    /// Proves the root-scope wiring the way the Editor would exercise it: the container builds
    /// with <c>RegisterDots</c> on it, resolves the layer's services, and the build callback has
    /// installed the view bootstrap into the world it was given.
    /// </summary>
    /// <remarks>
    /// A throwaway <see cref="World"/> is passed explicitly so the test never depends on whether
    /// the Editor happens to have a default injection world — the production call site omits the
    /// parameter and gets <c>World.DefaultGameObjectInjectionWorld</c>.
    /// </remarks>
    public class DotsRegistrationTests
    {
        private World world;
        private IObjectResolver container;

        [SetUp]
        public void SetUp()
        {
            this.world = new World("DotsRegistrationTests");

            var builder = new ContainerBuilder();
            builder.RegisterDots(viewRoot: null, world: this.world);
            this.container = builder.Build();
        }

        [TearDown]
        public void TearDown()
        {
            this.container?.Dispose();
            this.container = null;

            if (this.world is { IsCreated: true })
            {
                DotsViewBootstrap.Uninstall(this.world);
                this.world.Dispose();
            }

            this.world = null;
        }

        [Test]
        public void ContainerBuild_Succeeds_AndResolvesViewLayer()
        {
            Assert.That(this.container.Resolve<EntityViewRegistry>(), Is.Not.Null);
            Assert.That(this.container.Resolve<ChunkViewProvisioner>(), Is.Not.Null);
        }

        [Test]
        public void ViewAssetProvider_IsThePrimitiveFallback()
        {
            // GameLifetimeScope registers no GameFoundation services (IAssetsManager,
            // IObjectPoolManager), so RegisterDots must fall back to the primitive provider.
            // When RegisterGameFoundation lands, this assertion is the one to flip.
            Assert.That(this.container.Resolve<IViewAssetProvider>(), Is.InstanceOf<PrimitiveViewAssetProvider>());
        }

        [Test]
        public void SimulationModel_IsAuthoritative_BecauseSharedGameLogicIsInstalled()
        {
            var model = this.container.Resolve<ISimulationModel>();
            Assert.That(model, Is.Not.Null);
            Assert.That(model.IsAuthoritative, Is.True,
                "com.rpgmmo.shared-gamelogic is installed, so the seam must bind " +
                "SharedGameLogicSimulation, not the passive model.");
        }

        [Test]
        public void DotsMessaging_IsMessagePipeBacked_NotTheNullPublisher()
        {
            Assert.That(this.container.Resolve<IDotsPublisher<ViewSpawned>>(),
                Is.Not.InstanceOf<NullDotsPublisher<ViewSpawned>>());
            Assert.That(this.container.Resolve<IDotsPublisher<ViewDespawned>>(),
                Is.Not.InstanceOf<NullDotsPublisher<ViewDespawned>>());
            Assert.That(this.container.Resolve<IDotsPublisher<ChunkWarmed>>(),
                Is.Not.InstanceOf<NullDotsPublisher<ChunkWarmed>>());
            Assert.That(this.container.Resolve<IDotsPublisher<ChunkReleased>>(),
                Is.Not.InstanceOf<NullDotsPublisher<ChunkReleased>>());
            Assert.That(this.container.Resolve<IDotsPublisher<ChunkCascadeReleased>>(),
                Is.Not.InstanceOf<NullDotsPublisher<ChunkCascadeReleased>>());
        }

        [Test]
        public void Predictor_ResolvesAsSingleInstance()
        {
            var first = this.container.Resolve<LocalMovePredictor>();
            var second = this.container.Resolve<LocalMovePredictor>();
            Assert.That(first, Is.Not.Null);
            Assert.That(second, Is.SameAs(first),
                "The predictor must be one instance shared between input recording and the " +
                "prediction driver, or replay diverges by construction.");
        }

        [Test]
        public void BuildCallback_InstalledViewBootstrap_IntoTheGivenWorld()
        {
            using var query = this.world.EntityManager.CreateEntityQuery(
                ComponentType.ReadOnly<EntityViewRegistryReference>());
            Assert.That(query.IsEmpty, Is.False,
                "container.Build() must install DotsViewBootstrap into the world handed to RegisterDots.");
        }
    }
}
#endif
