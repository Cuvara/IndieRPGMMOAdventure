using Cuvara.DOTS.Views;
using NUnit.Framework;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;

namespace Cuvara.DOTS.Tests
{
    /// <summary>
    /// Entity -&gt; view lifecycle: a request becomes a view, a destroyed entity recycles it, and
    /// the ECS transform reaches the GameObject.
    /// </summary>
    /// <remarks>
    /// Drives an isolated <see cref="World"/> and ticks the three systems by hand rather than
    /// relying on the default world's update loop, so nothing else in the project can influence
    /// the result.
    /// </remarks>
    public sealed class EntityViewLifecycleTests
    {
        private World _world;
        private EntityManager _entityManager;
        private FakeViewAssetProvider _provider;
        private EntityViewRegistry _registry;

        [SetUp]
        public void SetUp()
        {
            _world = new World("Cuvara.DOTS.Tests");
            _entityManager = _world.EntityManager;
            _provider = new FakeViewAssetProvider();
            _registry = new EntityViewRegistry(_provider);

            DotsViewBootstrap.Install(_world, _registry);

            _world.CreateSystem<EntityViewSpawnSystem>();
            _world.CreateSystem<EntityViewDespawnSystem>();
            _world.CreateSystem<EntityViewTransformSyncSystem>();
        }

        [TearDown]
        public void TearDown()
        {
            DotsViewBootstrap.Uninstall(_world);
            _world.Dispose();
        }

        /// <summary>
        /// Drives the three systems in the order the group layout puts them: despawn, spawn, sync.
        /// Kept honest by the edit-mode attribute assertions in <c>ViewSystemGroupLayoutTests</c> —
        /// this harness bypasses the groups, so it cannot detect a wrong attribute on its own.
        /// </summary>
        private void Tick()
        {
            _world.GetExistingSystem<EntityViewDespawnSystem>().Update(_world.Unmanaged);
            _world.GetExistingSystem<EntityViewSpawnSystem>().Update(_world.Unmanaged);
            _world.GetExistingSystem<EntityViewTransformSyncSystem>().Update(_world.Unmanaged);
        }

        /// <summary>
        /// Adds <see cref="LocalToWorld"/> explicitly as well as <see cref="LocalTransform"/>.
        /// <c>LocalToWorld</c> is what the view systems read, and this harness never runs
        /// <c>TransformSystemGroup</c>, so nothing would otherwise compute it — the test has to play
        /// the part of the transform systems.
        /// </summary>
        private Entity CreateRequest(string key, float3 position)
        {
            var entity = _entityManager.CreateEntity();
            _entityManager.AddComponentData(entity, new EntityViewRequest { ViewKey = key });
            _entityManager.AddComponentData(entity, LocalTransform.FromPosition(position));
            _entityManager.AddComponentData(entity, new LocalToWorld
            {
                Value = float4x4.TRS(position, quaternion.identity, new float3(1f, 1f, 1f)),
            });
            return entity;
        }

        [Test]
        public void Request_SpawnsAViewAndLinksIt()
        {
            var entity = CreateRequest("goblin", new float3(1f, 0f, 2f));

            Tick();

            Assert.IsTrue(_entityManager.HasComponent<EntityViewLink>(entity));
            Assert.IsFalse(_entityManager.HasComponent<EntityViewRequest>(entity), "request is consumed");
            Assert.AreEqual(1, _registry.Count);
            Assert.AreEqual(1, _provider.AcquireCount);

            var viewId = _entityManager.GetComponentData<EntityViewLink>(entity).ViewId;
            Assert.AreNotEqual(0, viewId);
            Assert.IsNotNull(_registry.Get(viewId));
        }

        [Test]
        public void SecondTick_DoesNotSpawnADuplicateView()
        {
            CreateRequest("goblin", float3.zero);

            Tick();
            Tick();

            Assert.AreEqual(1, _registry.Count);
            Assert.AreEqual(1, _provider.AcquireCount);
        }

        [Test]
        public void ColdKey_DefersTheSpawnInsteadOfHitching()
        {
            _provider.WarmEverything = false;
            var entity = CreateRequest("goblin", float3.zero);

            Tick();

            Assert.IsFalse(_entityManager.HasComponent<EntityViewLink>(entity));
            Assert.AreEqual(0, _provider.AcquireCount);
            Assert.IsTrue(_entityManager.HasComponent<EntityViewRequest>(entity), "request survives for a retry");

            _provider.PrewarmAsync("goblin", 1);
            Tick();

            Assert.IsTrue(_entityManager.HasComponent<EntityViewLink>(entity));
        }

        [Test]
        public void DestroyingTheEntity_RecyclesTheViewAndFreesTheEntity()
        {
            var entity = CreateRequest("goblin", float3.zero);
            Tick();
            Assert.AreEqual(1, _registry.Count);

            _entityManager.DestroyEntity(entity);

            // The cleanup component keeps the entity alive until the despawn system runs.
            Assert.IsTrue(_entityManager.Exists(entity));

            Tick();

            Assert.AreEqual(0, _registry.Count);
            Assert.AreEqual(1, _provider.ReleaseInstanceCount);
            Assert.IsFalse(_entityManager.Exists(entity), "cleanup component removed, entity freed");
        }

        [Test]
        public void TransformSync_WritesLocalTransformOntoTheGameObject()
        {
            var entity = CreateRequest("goblin", float3.zero);
            Tick();

            var viewId = _entityManager.GetComponentData<EntityViewLink>(entity).ViewId;
            var view = _registry.Get(viewId);

            _entityManager.SetComponentData(entity, new LocalToWorld
            {
                Value = float4x4.TRS(new float3(3f, 0.5f, -7f), quaternion.identity, new float3(2f, 2f, 2f)),
            });

            Tick();

            Assert.AreEqual(new Vector3(3f, 0.5f, -7f), view.transform.position);
            Assert.AreEqual(2f, view.transform.localScale.x, 1e-4f);
        }

        [Test]
        public void RegistryClear_RecyclesEveryLiveView()
        {
            CreateRequest("goblin", float3.zero);
            CreateRequest("torch", float3.zero);
            Tick();
            Assert.AreEqual(2, _registry.Count);

            _registry.Clear();

            Assert.AreEqual(0, _registry.Count);
            Assert.AreEqual(2, _provider.ReleaseInstanceCount);
        }
    }
}
