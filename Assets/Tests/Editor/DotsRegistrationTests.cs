#if CUVARA_DOTS && CUVARA_DOTS_VCONTAINER && CUVARA_DOTS_MESSAGEPIPE && CUVARA_NETCODE && CUVARA_SHARED_GAMELOGIC
namespace Tests.Editor
{
    using Cuvara.DOTS.Messaging;
    using Cuvara.DOTS.Provisioning;
    using Cuvara.DOTS.Simulation;
    using Cuvara.DOTS.Views;
    using Cuvara.Netcode.Prediction;
    using Shared.GameLogic.Components;
    using NUnit.Framework;
    using Scripts.DI.Dots;
    using Unity.Entities;
    using UnityEngine;
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
            // Primitive on purpose: the production mode needs the DotsViewLibrary asset in
            // Resources, which a unit test must not depend on. Production wiring is proven
            // below with an explicit asset and a fake loader.
            builder.RegisterDots(viewRoot: null, world: this.world, mode: DotsViewProviderMode.Primitive);
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
        public void ViewAssetProvider_IsPrimitive_InPrimitiveMode()
        {
            Assert.That(this.container.Resolve<IViewAssetProvider>(), Is.InstanceOf<PrimitiveViewAssetProvider>());
            var reference = this.container.Resolve<DotsViewLibraryReference>();
            Assert.That(reference.Mode, Is.EqualTo(DotsViewProviderMode.Primitive));
            Assert.That(reference.Asset, Is.Null);
        }

        [Test]
        public void ViewAssetProvider_IsTheLeasedPooledProvider_InProductionMode()
        {
            var asset = ScriptableObject.CreateInstance<DotsViewLibraryAsset>();
            using var productionWorld = new World("DotsRegistrationTests.Production");
            var builder = new ContainerBuilder();
            builder.RegisterDots(
                viewRoot: null,
                world: productionWorld,
                mode: DotsViewProviderMode.Production,
                library: asset,
                loader: new FakeViewPrefabLoader());
            using var production = builder.Build();

            var provider = production.Resolve<IViewAssetProvider>();
            Assert.That(provider, Is.InstanceOf<LeasedViewAssetProvider>());
            Assert.That(((LeasedViewAssetProvider)provider).Pool, Is.Not.Null, "the package's pooled provider underneath");
            Assert.That(production.Resolve<DotsViewLibraryReference>().Asset, Is.SameAs(asset));
            Assert.That(production.Resolve<DotsViewLibraryReference>().Mode, Is.EqualTo(DotsViewProviderMode.Production));

            DotsViewBootstrap.Uninstall(productionWorld);
            Object.DestroyImmediate(asset);
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

        /// <summary>
        /// The predictor's play area must be the server's: centred on the origin
        /// (MapBounds.FromSize), not anchored at (0,0). With corner-anchored bounds every
        /// predicted step into negative x or y was clamped to 0, the server accepted it, and the
        /// local player snapped back by the whole distance on every snapshot (testlab S05:
        /// 1964 rendered jumps for a client walking toward -x, 0 for one walking toward +x).
        /// </summary>
        [Test]
        public void Predictor_MovesIntoNegativeCoordinates_LikeTheServer()
        {
            var bounds = DotsRegistration.PredictorBounds;
            var server = MapBounds.FromSize(GameConstants.DefaultMapWidth, GameConstants.DefaultMapHeight);
            Assert.That(bounds.MinX, Is.EqualTo(server.MinX));
            Assert.That(bounds.MinY, Is.EqualTo(server.MinY));
            Assert.That(bounds.MaxX, Is.EqualTo(server.MaxX));
            Assert.That(bounds.MaxY, Is.EqualTo(server.MaxY));
            Assert.That(bounds.Contains(new Vec2(-100f, -100f)), Is.True,
                "the server's default map spans negative coordinates; the predictor's must too");

            // Behaviour, not only the value: walking toward -x must cover the same distance as
            // walking toward +x (symmetric about the spawn at the origin) instead of stopping at 0.
            float left = Walk(bounds, -1f);
            float right = Walk(bounds, +1f);
            Assert.That(right, Is.GreaterThan(0.5f), "the predictor did not move at all; the test proves nothing");
            Assert.That(left, Is.EqualTo(-right).Within(1e-3f), "predicted position was clamped at x = 0");

            // Control: the corner-anchored bounds this replaced DO clamp, so this test can fail.
            var corner = new MapBounds(0f, 0f, GameConstants.DefaultMapWidth, GameConstants.DefaultMapHeight);
            Assert.That(Walk(corner, -1f), Is.EqualTo(0f).Within(1e-4f));
        }

        /// <summary>Ten inputs held in one direction at the 60 Hz base rate, 4 frames per input.</summary>
        private static float Walk(MapBounds bounds, float direction)
        {
            // Same construction as HeldMovementParityTests in com.cuvara.netcode: 60 Hz base, a
            // 4-tick hold, anchored at the spawn by a first reconcile.
            var predictor = new LocalMovePredictor(new PredictionSettings(60, 5f, bounds));
            predictor.SetHoldTicks(4);
            predictor.Reconcile(Vec2.Zero, 0);
            for (long tick = 1; tick <= 10; tick++)
            {
                predictor.RecordInput(tick, direction, 0f);
                for (int frame = 0; frame < 4; frame++)
                {
                    predictor.Advance(1f / 60f);
                }
            }

            return predictor.SimulatedPosition.X;
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
