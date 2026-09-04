#if CUVARA_DOTS && CUVARA_NETCODE && CUVARA_UITOOLKIT_ENTITIES
namespace Tests.Editor
{
    using System.Collections.Generic;
    using Cuvara.DOTS.Netcode;
    using Cuvara.UIToolkit.Ecs;
    using NUnit.Framework;
    using Scripts.UI.Hud.Ecs;
    using Unity.Collections;
    using Unity.Entities;
    using Unity.Mathematics;
    using Unity.Transforms;

    /// <summary>
    /// The ECS half against a throwaway world: install, aggregation from synthetic mirror
    /// entities, the sink catch-up, the change-driven quiet path, and teardown that leaves
    /// nothing behind — the exact sequence <c>HudWorldBridge</c> runs.
    /// </summary>
    public class HudEcsLifecycleTests
    {
        private World world;

        private sealed class SpySink : IViewModelSink<HudSnapshot>
        {
            public readonly List<HudSnapshot> Received = new();

            public void Push(in HudSnapshot snapshot) => this.Received.Add(snapshot);
        }

        [SetUp]
        public void SetUp()
        {
            this.world = new World("HudEcsLifecycleTests");
        }

        [TearDown]
        public void TearDown()
        {
            if (this.world is { IsCreated: true })
            {
                this.world.Dispose();
            }
        }

        private Entity CreateMirror(string id, string kind, bool isLocal, int hp, int maxHp, float3 position)
        {
            var entityManager = this.world.EntityManager;
            var entity = entityManager.CreateEntity();
            entityManager.AddComponentData(entity, new NetworkEntity
            {
                Id = new FixedString64Bytes(id),
                Type = new FixedString32Bytes(kind),
                IsLocal = isLocal,
            });
            entityManager.AddComponentData(entity, new NetworkEntityState { Hp = hp, MaxHp = maxHp });
            entityManager.AddComponentData(entity, LocalTransform.FromPosition(position));
            return entity;
        }

        private void Simulate() => this.world.GetExistingSystemManaged<SimulationSystemGroup>().Update();

        private HudState Singleton()
        {
            using var query = this.world.EntityManager.CreateEntityQuery(ComponentType.ReadOnly<HudState>());
            return query.GetSingleton<HudState>();
        }

        [Test]
        public void Install_CreatesTheSingleton_AndBothSystems()
        {
            var bridge = HudEcsBootstrap.Install(this.world);

            Assert.That(bridge, Is.Not.Null);
            Assert.That(this.world.GetExistingSystem<HudStateSystem>(), Is.Not.EqualTo(default(SystemHandle)));

            using var query = this.world.EntityManager.CreateEntityQuery(ComponentType.ReadOnly<HudState>());
            Assert.That(query.CalculateEntityCount(), Is.EqualTo(1));
        }

        [Test]
        public void Install_IsIdempotent()
        {
            var first = HudEcsBootstrap.Install(this.world);
            var second = HudEcsBootstrap.Install(this.world);

            Assert.That(second, Is.SameAs(first));

            using var query = this.world.EntityManager.CreateEntityQuery(ComponentType.ReadOnly<HudState>());
            Assert.That(query.CalculateEntityCount(), Is.EqualTo(1));
        }

        [Test]
        public void TheAggregator_ReadsTheMirrors_LocalVitals_QuantizedPosition_AndCounts()
        {
            HudEcsBootstrap.Install(this.world);
            this.CreateMirror("local", "player", isLocal: true, hp: 57, maxHp: 100, new float3(12.34f, 0f, 45.67f));
            this.CreateMirror("remote", "player", isLocal: false, hp: 80, maxHp: 100, new float3(5f, 0f, 5f));
            this.CreateMirror("enemy-1", "mob", isLocal: false, hp: 20, maxHp: 20, new float3(9f, 0f, 9f));

            this.Simulate();

            var state = this.Singleton();
            Assert.That(state.HasLocalPlayer, Is.True);
            Assert.That(state.Hp, Is.EqualTo(57));
            Assert.That(state.MaxHp, Is.EqualTo(100));
            Assert.That(state.PosX, Is.EqualTo(12.3f).Within(1e-4f));
            Assert.That(state.PosZ, Is.EqualTo(45.7f).Within(1e-4f));
            Assert.That(state.PlayersVisible, Is.EqualTo(2));
            Assert.That(state.EntitiesVisible, Is.EqualTo(3));
        }

        [Test]
        public void ASinkRegisteredAfterTheFact_GetsOneCatchUpPush_WithTheCurrentState()
        {
            var bridge = HudEcsBootstrap.Install(this.world);
            this.CreateMirror("local", "player", isLocal: true, hp: 57, maxHp: 100, new float3(1f, 0f, 2f));
            this.Simulate();

            var sink = new SpySink();
            using var registration = EcsSinkRegistration.Bind(bridge, sink);
            bridge.Update();

            Assert.That(sink.Received, Has.Count.EqualTo(1), "the one-shot unfiltered catch-up pass");
            Assert.That(sink.Received[0].HealthCaption, Is.EqualTo("57/100"));
            Assert.That(sink.Received[0].EntitiesVisible, Is.EqualTo(1));
        }

        [Test]
        public void AQuietFrame_PushesNothing_AndAChangePushesAgain()
        {
            var bridge = HudEcsBootstrap.Install(this.world);
            var local = this.CreateMirror("local", "player", isLocal: true, hp: 57, maxHp: 100, new float3(1f, 0f, 2f));
            this.Simulate();

            var sink = new SpySink();
            using var registration = EcsSinkRegistration.Bind(bridge, sink);
            bridge.Update();
            Assert.That(sink.Received, Has.Count.EqualTo(1));

            // Nothing changed: the aggregator compares-before-write, so the HudState chunk
            // version stands still and the bridge's change filter skips it entirely.
            this.Simulate();
            bridge.Update();
            Assert.That(sink.Received, Has.Count.EqualTo(1), "a quiet frame must not wake the sink");

            this.world.EntityManager.SetComponentData(local, new NetworkEntityState { Hp = 30, MaxHp = 100 });
            this.Simulate();
            bridge.Update();

            Assert.That(sink.Received, Has.Count.EqualTo(2));
            Assert.That(sink.Received[1].HealthCaption, Is.EqualTo("30/100"));
        }

        [Test]
        public void Uninstall_RemovesBothSystems_AndTheSingleton()
        {
            var bridge = HudEcsBootstrap.Install(this.world);
            var sink = new SpySink();
            var registration = EcsSinkRegistration.Bind(bridge, sink);

            // The order HudWorldBridge.OnDestroy runs: sink unhooked first, then systems.
            registration.Dispose();
            HudEcsBootstrap.Uninstall(this.world);

            Assert.That(this.world.GetExistingSystemManaged<HudBridgeSystem>(), Is.Null);
            Assert.That(this.world.GetExistingSystem<HudStateSystem>(), Is.EqualTo(default(SystemHandle)));

            using var query = this.world.EntityManager.CreateEntityQuery(ComponentType.ReadOnly<HudState>());
            Assert.That(query.IsEmpty, Is.True, "HudStateSystem.OnDestroy must take its singleton with it");
        }

        [Test]
        public void Uninstall_OnAWorldThatNeverInstalled_IsSafe()
        {
            Assert.DoesNotThrow(() => HudEcsBootstrap.Uninstall(this.world));
            Assert.DoesNotThrow(() => HudEcsBootstrap.Uninstall(null));
        }
    }
}
#endif
