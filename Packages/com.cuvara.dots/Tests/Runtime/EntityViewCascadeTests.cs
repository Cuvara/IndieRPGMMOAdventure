using Cuvara.DOTS.Groups;
using Cuvara.DOTS.Provisioning;
using Cuvara.DOTS.Views;
using NUnit.Framework;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

namespace Cuvara.DOTS.Tests
{
    /// <summary>
    /// The view-layer half of the cascade: entities survive, links are cleared, handles are gone,
    /// and nothing respawns from a released key.
    /// </summary>
    /// <remarks>
    /// Needs a real <see cref="World"/> because the link removal is a structural change recorded
    /// into <see cref="DotsEndSimulationCommandBufferSystem"/> — the assertion that matters is what
    /// the world looks like after that buffer plays back.
    /// </remarks>
    public sealed class EntityViewCascadeTests
    {
        private World _world;
        private EntityManager _entityManager;
        private FakeViewAssetProvider _provider;
        private EntityViewRegistry _registry;
        private EntityViewCascade _cascade;
        private ChunkViewProvisioner _provisioner;

        [SetUp]
        public void SetUp()
        {
            _world = new World("Cuvara.DOTS.CascadeTests");
            _entityManager = _world.EntityManager;
            _provider = new FakeViewAssetProvider();
            _registry = new EntityViewRegistry(_provider);

            DotsViewBootstrap.Install(_world, _registry);

            _cascade = new EntityViewCascade(_world, _registry);
            _provisioner = new ChunkViewProvisioner(_provider, _cascade);
        }

        [TearDown]
        public void TearDown()
        {
            DotsViewBootstrap.Uninstall(_world);
            _world.Dispose();
        }

        private Entity CreateSpawned(string key)
        {
            var entity = _entityManager.CreateEntity();
            _entityManager.AddComponentData(entity, new EntityViewRequest { ViewKey = key });
            _entityManager.AddComponentData(entity, LocalTransform.Identity);
            _entityManager.AddComponentData(entity, new LocalToWorld { Value = float4x4.identity });

            _world.GetExistingSystem<EntityViewSpawnSystem>().Update(_world.Unmanaged);
            Assert.IsTrue(_entityManager.HasComponent<EntityViewLink>(entity), "precondition: view spawned");
            return entity;
        }

        /// <summary>Plays back the package command buffer, which is where the link removal landed.</summary>
        private void FlushStructuralChanges() =>
            _world.GetExistingSystemManaged<DotsEndSimulationCommandBufferSystem>().Update();

        [Test]
        public void Cascade_ClearsTheLink_DropsTheHandle_AndLeavesTheEntityAlive()
        {
            _provisioner.PrewarmChunkAsync("chunk-a", new[] { "goblin" });
            var entity = CreateSpawned("goblin");
            var viewId = _entityManager.GetComponentData<EntityViewLink>(entity).ViewId;

            var result = _provisioner.ReleaseChunk("chunk-a");

            Assert.AreEqual(1, result.ViewsDespawned);
            Assert.AreEqual(1, _provider.ReleaseInstanceCount, "recycled through the ordinary path");
            Assert.AreEqual(0, _registry.Count, "handle dropped from the registry");
            Assert.IsNull(_registry.Get(viewId));

            FlushStructuralChanges();

            Assert.IsTrue(_entityManager.Exists(entity), "the entity survives a streaming unload");
            Assert.IsFalse(_entityManager.HasComponent<EntityViewLink>(entity));
            Assert.IsFalse(_entityManager.HasComponent<EntityViewLinkCleanup>(entity));
        }

        [Test]
        public void AfterACascade_TheEntityDoesNotRespawnAView()
        {
            _provisioner.PrewarmChunkAsync("chunk-a", new[] { "goblin" });
            var entity = CreateSpawned("goblin");

            _provisioner.ReleaseChunk("chunk-a");
            FlushStructuralChanges();

            // The spawn system acts only on EntityViewRequest, and the cascade does not re-add one.
            // If it did, this would spawn from a key that was just released.
            _provider.WarmEverything = true;
            for (var i = 0; i < 3; i++)
            {
                _world.GetExistingSystem<EntityViewSpawnSystem>().Update(_world.Unmanaged);
            }

            Assert.AreEqual(0, _registry.Count);
            Assert.IsFalse(_entityManager.HasComponent<EntityViewLink>(entity));
            Assert.AreEqual(1, _provider.AcquireCount, "the original spawn, and nothing since");
        }

        [Test]
        public void MidCascade_TheLinkStillExistsButResolvesToNothing()
        {
            // The documented window: managed teardown is synchronous, the link removal waits for the
            // command buffer. Nothing misbehaves in it — this pins what a caller would observe.
            _provisioner.PrewarmChunkAsync("chunk-a", new[] { "goblin" });
            var entity = CreateSpawned("goblin");
            var viewId = _entityManager.GetComponentData<EntityViewLink>(entity).ViewId;

            _provisioner.ReleaseChunk("chunk-a");

            Assert.IsTrue(_entityManager.HasComponent<EntityViewLink>(entity), "not removed yet");
            Assert.IsNull(_registry.Get(viewId), "but it already resolves to nothing");

            // The sync system must tolerate the dangling handle rather than throwing.
            Assert.DoesNotThrow(() => _world.GetExistingSystem<EntityViewTransformSyncSystem>().Update(_world.Unmanaged));
        }

        [Test]
        public void EntitiesOfAnotherChunk_AreUntouched()
        {
            _provisioner.PrewarmChunkAsync("chunk-a", new[] { "goblin" });
            _provisioner.PrewarmChunkAsync("chunk-b", new[] { "torch" });
            var goblin = CreateSpawned("goblin");
            var torch = CreateSpawned("torch");

            _provisioner.ReleaseChunk("chunk-a");
            FlushStructuralChanges();

            Assert.IsFalse(_entityManager.HasComponent<EntityViewLink>(goblin));
            Assert.IsTrue(_entityManager.HasComponent<EntityViewLink>(torch), "different key, different chunk");
            Assert.AreEqual(1, _registry.Count);
        }
    }
}
