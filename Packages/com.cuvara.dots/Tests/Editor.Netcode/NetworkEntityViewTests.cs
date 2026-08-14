using System.Text.RegularExpressions;
using Cuvara.DOTS.Configuration;
using Cuvara.DOTS.Groups;
using Cuvara.DOTS.Netcode;
using Cuvara.DOTS.Simulation;
using Cuvara.DOTS.Views;
using Cuvara.Netcode.View;
using NUnit.Framework;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;
using UnityEngine.TestTools;

namespace Cuvara.DOTS.Tests.Netcode
{
    /// <summary>
    /// The adapter end to end: <c>IEntityView</c> calls in, entities and pooled views out.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Driven through the <b>public groups</b> — <c>NetcodeSystemGroup</c> then
    /// <c>ViewSystemGroup</c> — rather than by ticking named systems. That is the package's stated
    /// ordering contract, so a test that names systems would pass while the contract broke.
    /// </para>
    /// <para>
    /// The three <c>IEntityView</c> methods are called directly, in the order
    /// <c>WorldViewBinder.Tick</c> calls them, rather than through the binder itself. The binder
    /// needs <c>WorldState</c>, which needs <c>Shared.GameLogic</c> — a second optional dependency
    /// this assembly would then have to be constrained on, to test a class that is not ours. What is
    /// ours is the adapter's response to that call sequence.
    /// </para>
    /// <para>
    /// <b>The ids here deliberately carry no meaning.</b> The mob's id is <c>"uuid-e1"</c>, not
    /// <c>"enemy-1"</c>, and one test spawns a <i>player</i> whose id is literally
    /// <c>"enemy-9"</c>. Before netcode 0.4.0 the second case was unrepresentable and the first
    /// would have resolved to a player. Ids that no longer encode kind are how these tests assert
    /// that nothing reads them for kind any more.
    /// </para>
    /// </remarks>
    public sealed class NetworkEntityViewTests
    {
        private const string LocalArchetype = "player-local";
        private const string RemoteArchetype = "player-remote";
        private const string MobArchetype = "goblin";

        private const string PlayerType = "player";
        private const string MobType = "mob";

        private World _world;
        private EntityManager _entityManager;
        private EntityViewRegistry _registry;
        private ViewConfigCatalog _catalog;
        private ViewArchetypeLibrary _library;
        private ViewConfig _localConfig;
        private ViewConfig _remoteConfig;
        private ViewConfig _mobConfig;
        private DotsEntityView _view;

        [SetUp]
        public void SetUp()
        {
            _world = new World("Cuvara.DOTS.NetworkEntityViewTests");
            _entityManager = _world.EntityManager;

            _registry = new EntityViewRegistry(new StubViewAssetProvider());
            DotsViewBootstrap.Install(_world, _registry);

            _localConfig = ScriptableObject.CreateInstance<ViewConfig>();
            _localConfig.Configure("player", uniformScale: 1.2f);

            _remoteConfig = ScriptableObject.CreateInstance<ViewConfig>();
            _remoteConfig.Configure("player");

            // The half-height lift the reference implementation baked into its (x, 0.5f, y) literal,
            // authored where it belongs: on the art, not on the world.
            _mobConfig = ScriptableObject.CreateInstance<ViewConfig>();
            _mobConfig.Configure("goblin", uniformScale: 0.8f, position: new Vector3(0f, 0.5f, 0f));

            _library = ScriptableObject.CreateInstance<ViewArchetypeLibrary>();
            _library.Configure(
                new ViewArchetypeLibrary.Entry { Name = LocalArchetype, Config = _localConfig },
                new ViewArchetypeLibrary.Entry { Name = RemoteArchetype, Config = _remoteConfig },
                new ViewArchetypeLibrary.Entry { Name = MobArchetype, Config = _mobConfig });

            _catalog = new ViewConfigCatalog();
            _catalog.Build(_library);
            _catalog.Install(_world);

            _view = NewView();
            DotsNetcodeBootstrap.Install(_world, _view);
        }

        [TearDown]
        public void TearDown()
        {
            DotsNetcodeBootstrap.Uninstall(_world);
            _catalog.Dispose();
            Object.DestroyImmediate(_library);
            Object.DestroyImmediate(_localConfig);
            Object.DestroyImmediate(_remoteConfig);
            Object.DestroyImmediate(_mobConfig);
            DotsViewBootstrap.Uninstall(_world);
            _world.Dispose();
        }

        private DotsEntityView NewView(bool writeHealth = false) => new DotsEntityView(
            _catalog,
            // (localArchetype, unknownArchetype, ...rules) — no catch-all, so an unmapped kind is
            // refused rather than quietly rendered as something.
            new TypeArchetypeResolver(
                LocalArchetype,
                null,
                new TypeArchetypeResolver.Rule(PlayerType, RemoteArchetype),
                new TypeArchetypeResolver.Rule(MobType, MobArchetype)),
            SnapshotSpaceMapping.XZPlane,
            writeHealth);

        /// <summary>One frame: the netcode group drains, then the view group presents.</summary>
        private void Tick()
        {
            _world.GetExistingSystemManaged<NetcodeSystemGroup>().Update();
            _world.GetExistingSystemManaged<ViewSystemGroup>().Update();
        }

        private Entity Find(string id)
        {
            using var query = _entityManager.CreateEntityQuery(ComponentType.ReadOnly<NetworkEntity>());
            using var entities = query.ToEntityArray(Allocator.Temp);
            var wanted = new FixedString64Bytes(id);

            for (var i = 0; i < entities.Length; i++)
            {
                if (_entityManager.GetComponentData<NetworkEntity>(entities[i]).Id.Equals(wanted))
                {
                    return entities[i];
                }
            }

            return Entity.Null;
        }

        private int MirrorCount()
        {
            using var query = _entityManager.CreateEntityQuery(ComponentType.ReadOnly<NetworkEntity>());
            return query.CalculateEntityCount();
        }

        private int ConfigIndexOf(string id) => _entityManager.GetComponentData<ViewConfigRef>(Find(id)).Index;

        private GameObject ViewOf(string id) =>
            _registry.Get(_entityManager.GetComponentData<EntityViewLink>(Find(id)).ViewId);

        [Test]
        public void Spawn_ThenState_ProducesAPositionedViewInTheSameFrame()
        {
            // The frame-landing claim in DotsEntityView's remarks, asserted: enqueue before the
            // netcode group runs and the pooled instance is at the right place after the view group,
            // with no second Tick.
            var view = (IEntityView)_view;
            view.Spawn("uuid-a", isLocal: false, type: PlayerType);
            view.SetState("uuid-a", 3f, 7f, 90, 100);

            Tick();

            var entity = Find("uuid-a");
            Assert.AreNotEqual(Entity.Null, entity, "the drain created the mirror entity");
            Assert.AreEqual(new float3(3f, 0f, 7f), _entityManager.GetComponentData<LocalTransform>(entity).Position);
            Assert.AreEqual(new Vector3(3f, 0f, 7f), ViewOf("uuid-a").transform.position);
        }

        [Test]
        public void TheServersType_DecidesTheArchetype_NotHowTheIdIsSpelled()
        {
            // The regression this whole release exists to make impossible. Under the old prefix
            // resolver the first of these was a player and the second was a goblin — both wrong, and
            // both wrong silently.
            var view = (IEntityView)_view;
            view.Spawn("uuid-e1", isLocal: false, type: MobType);      // no prefix, still a mob
            view.Spawn("enemy-9", isLocal: false, type: PlayerType);   // prefix says monster, wire says player

            Tick();

            Assert.AreEqual(_catalog.IndexOf(MobArchetype), ConfigIndexOf("uuid-e1"));
            Assert.AreEqual(_catalog.IndexOf(RemoteArchetype), ConfigIndexOf("enemy-9"));
        }

        [Test]
        public void Archetype_ComesFromTheCatalog_ForEveryKind()
        {
            var view = (IEntityView)_view;
            view.Spawn("uuid-e1", isLocal: false, type: MobType);
            view.Spawn("uuid-a", isLocal: false, type: PlayerType);
            view.Spawn("uuid-me", isLocal: true, type: PlayerType);

            Tick();

            Assert.AreEqual(_catalog.IndexOf(MobArchetype), ConfigIndexOf("uuid-e1"));
            Assert.AreEqual(_catalog.IndexOf(RemoteArchetype), ConfigIndexOf("uuid-a"));
            Assert.AreEqual(_catalog.IndexOf(LocalArchetype), ConfigIndexOf("uuid-me"),
                "the local override beats the type rule the remote player matched");

            // The config's scale reached the instance, which is what proves the index was used
            // rather than merely written.
            Assert.AreEqual(1.2f, ViewOf("uuid-me").transform.localScale.x, 1e-4f);
        }

        [Test]
        public void ServerXY_LandsOnTheGroundPlane_AndTheLiftComesFromTheConfig()
        {
            // The two halves of the sample's (x, 0.5f, y) literal, now separable: the entity is on
            // the plane the mapping describes, and only the mob's view is lifted, because only the
            // mob's ViewConfig asks for it.
            var view = (IEntityView)_view;
            view.Spawn("uuid-e1", isLocal: false, type: MobType);
            view.SetState("uuid-e1", 3f, 7f, 30, 30);
            view.Spawn("uuid-a", isLocal: false, type: PlayerType);
            view.SetState("uuid-a", 3f, 7f, 90, 100);

            Tick();

            Assert.AreEqual(new float3(3f, 0f, 7f), _entityManager.GetComponentData<LocalTransform>(Find("uuid-e1")).Position,
                "the entity itself is never lifted — gameplay maths is 2D");
            Assert.AreEqual(new Vector3(3f, 0.5f, 7f), ViewOf("uuid-e1").transform.position, "lifted by the config offset");
            Assert.AreEqual(new Vector3(3f, 0f, 7f), ViewOf("uuid-a").transform.position, "the player config asks for no lift");
        }

        [Test]
        public void IdTypeAndIsLocal_AreAllCarriedOntoTheEntity()
        {
            // Type is on the entity so a consumer system can filter by kind without a managed
            // lookup — what the reference implementation's EnemyTag was for.
            var view = (IEntityView)_view;
            view.Spawn("uuid-me", isLocal: true, type: PlayerType);
            view.Spawn("uuid-e1", isLocal: false, type: MobType);

            Tick();

            var me = _entityManager.GetComponentData<NetworkEntity>(Find("uuid-me"));
            Assert.AreEqual(new FixedString64Bytes("uuid-me"), me.Id);
            Assert.AreEqual(new FixedString32Bytes(PlayerType), me.Type);
            Assert.IsTrue(me.IsLocal);

            var mob = _entityManager.GetComponentData<NetworkEntity>(Find("uuid-e1"));
            Assert.AreEqual(new FixedString32Bytes(MobType), mob.Type);
            Assert.IsFalse(mob.IsLocal);
        }

        [Test]
        public void UnmappedType_IsNotPresented_AndSaysSoOnce()
        {
            // Silence here would be the bad outcome: a build talking to a newer server would show an
            // empty world and no reason for it.
            LogAssert.Expect(LogType.Error, new Regex("projectile"));

            var view = (IEntityView)_view;
            view.Spawn("uuid-p1", isLocal: false, type: "projectile");
            view.Spawn("uuid-p2", isLocal: false, type: "projectile");

            Tick();

            Assert.AreEqual(0, MirrorCount(), "an unmapped kind is invisible, not rendered as something else");
        }

        [Test]
        public void EmptyType_IsRefused_RatherThanGuessed()
        {
            // netcode documents type as "never null; empty when the server sent no type at all".
            // The empty case is the one where guessing from the id would be most tempting.
            LogAssert.Expect(LogType.Error, new Regex("no type"));

            ((IEntityView)_view).Spawn("enemy-1", isLocal: false, type: string.Empty);

            Tick();

            Assert.AreEqual(0, MirrorCount());
        }

        [Test]
        public void Despawn_DestroysTheEntity_AndRecyclesTheViewInTheSameFrame()
        {
            var view = (IEntityView)_view;
            view.Spawn("uuid-a", isLocal: false, type: PlayerType);
            view.SetState("uuid-a", 1f, 1f, 100, 100);
            Tick();
            Assert.AreEqual(1, _registry.Count);

            view.Despawn("uuid-a");
            Tick();

            Assert.AreEqual(Entity.Null, Find("uuid-a"), "the mirror entity is gone");
            Assert.AreEqual(0, _registry.Count, "and its view went back to the pool");
        }

        [Test]
        public void RespawnAfterDespawn_Works_AndReResolvesTheKind()
        {
            // An AOI exit followed by a re-entry is the common case and the id is identical across
            // it, so a stale id → Entity mapping would refuse the second spawn. The kind is
            // re-resolved too: the per-id config cache is dropped on despawn.
            var view = (IEntityView)_view;
            view.Spawn("uuid-x", isLocal: false, type: PlayerType);
            Tick();
            var first = Find("uuid-x");
            Assert.AreEqual(_catalog.IndexOf(RemoteArchetype), ConfigIndexOf("uuid-x"));

            view.Despawn("uuid-x");
            Tick();

            // Same id, different kind — a server reusing an id for a different entity. Nothing
            // cached from the first life may leak into the second.
            view.Spawn("uuid-x", isLocal: false, type: MobType);
            view.SetState("uuid-x", 5f, 5f, 30, 30);
            Tick();

            var second = Find("uuid-x");
            Assert.AreNotEqual(Entity.Null, second);
            Assert.AreNotEqual(first, second);
            Assert.AreEqual(_catalog.IndexOf(MobArchetype), ConfigIndexOf("uuid-x"));
            Assert.AreEqual(new float3(5f, 0f, 5f), _entityManager.GetComponentData<LocalTransform>(second).Position);
        }

        [Test]
        public void DuplicateSpawn_IsIgnored_NotReplaced()
        {
            var view = (IEntityView)_view;
            view.Spawn("uuid-a", isLocal: false, type: PlayerType);
            view.Spawn("uuid-a", isLocal: false, type: PlayerType);

            Tick();

            Assert.AreEqual(1, MirrorCount());
        }

        [Test]
        public void StateForAnUnknownId_IsDropped_NotImplicitlySpawned()
        {
            // SetState carries neither kind nor isLocal, so an implicit spawn would have to invent
            // both — and inventing the kind is exactly what this release removed.
            ((IEntityView)_view).SetState("uuid-ghost", 1f, 1f, 10, 10);

            Tick();

            Assert.AreEqual(0, MirrorCount());
        }

        [Test]
        public void WireHp_LandsOnNetworkEntityState_AndNotOnHealth_ByDefault()
        {
            // Health means "destroy at zero" in this package. Mirroring server hp into it by default
            // would let a client-side system destroy entities the server is still listing.
            var view = (IEntityView)_view;
            view.Spawn("uuid-e1", isLocal: false, type: MobType);
            view.SetState("uuid-e1", 0f, 0f, 12, 30);

            Tick();

            var entity = Find("uuid-e1");
            var state = _entityManager.GetComponentData<NetworkEntityState>(entity);
            Assert.AreEqual(12, state.Hp);
            Assert.AreEqual(30, state.MaxHp);
            Assert.IsFalse(_entityManager.HasComponent<Health>(entity), "opt-in, and off by default");
        }

        [Test]
        public void WriteHealth_MirrorsWireHpIntoHealth_WhenAskedFor()
        {
            var opted = NewView(writeHealth: true);
            DotsNetcodeBootstrap.Install(_world, opted);

            var view = (IEntityView)opted;
            view.Spawn("uuid-e1", isLocal: false, type: MobType);
            view.SetState("uuid-e1", 0f, 0f, 12, 30);

            Tick();

            var health = _entityManager.GetComponentData<Health>(Find("uuid-e1"));
            Assert.AreEqual(12, health.Current);
            Assert.AreEqual(30, health.Max);
        }

        [Test]
        public void UnknownArchetype_IsNotPresented_AndDoesNotThrow()
        {
            // The resolver named an archetype; the catalog has never heard of it. Distinct from an
            // unmapped *kind*, and reported by the adapter rather than by the resolver.
            var stranded = new DotsEntityView(
                _catalog,
                new TypeArchetypeResolver(null, "no-such-archetype"),
                SnapshotSpaceMapping.XZPlane);
            DotsNetcodeBootstrap.Install(_world, stranded);

            LogAssert.Expect(LogType.Error, new Regex("no-such-archetype"));

            ((IEntityView)stranded).Spawn("uuid-a", isLocal: false, type: PlayerType);
            Tick();

            Assert.AreEqual(0, MirrorCount(), "an unconfigured archetype is invisible, not rendered as something else");
            Assert.AreEqual(0, stranded.Count);
        }

        [Test]
        public void SessionReset_DespawnsEverything()
        {
            // What WorldViewBinder.Reset does on a map transfer: a Despawn for every live id.
            var view = (IEntityView)_view;
            view.Spawn("uuid-a", isLocal: false, type: PlayerType);
            view.Spawn("uuid-b", isLocal: false, type: PlayerType);
            view.Spawn("uuid-e1", isLocal: false, type: MobType);
            Tick();
            Assert.AreEqual(3, MirrorCount());

            view.Despawn("uuid-a");
            view.Despawn("uuid-b");
            view.Despawn("uuid-e1");
            Tick();

            Assert.AreEqual(0, MirrorCount());
            Assert.AreEqual(0, _registry.Count);
            Assert.AreEqual(0, _view.Count);
        }

        [Test]
        public void Anchor_IsPresentFromSpawn_AndTracksTheServerPosition()
        {
            // Present from spawn so a predictor attaching later never reads a default, and updated
            // on every state so it is always "what the server last said".
            var view = (IEntityView)_view;
            view.Spawn("uuid-a", isLocal: false, type: PlayerType);

            Tick();
            Assert.AreEqual(float3.zero, _entityManager.GetComponentData<ReconciliationAnchor>(Find("uuid-a")).Position);

            view.SetState("uuid-a", 3f, 7f, 100, 100);
            Tick();
            Assert.AreEqual(new float3(3f, 0f, 7f), _entityManager.GetComponentData<ReconciliationAnchor>(Find("uuid-a")).Position);
        }

        [Test]
        public void Anchor_IsWrittenForRemotesToo_NotOnlyForPredictedEntities()
        {
            // Uniform on purpose: one float3 per mirror entity, and no "why does the local one have
            // this and remotes not" question for a future reader to re-derive.
            var view = (IEntityView)_view;
            view.Spawn("uuid-e1", isLocal: false, type: MobType);
            view.SetState("uuid-e1", 1f, 2f, 30, 30);

            Tick();

            Assert.AreEqual(new float3(1f, 0f, 2f), _entityManager.GetComponentData<ReconciliationAnchor>(Find("uuid-e1")).Position);
        }

        [Test]
        public void PredictedTransform_StopsTheTransformWrite_ButNotTheAnchor()
        {
            // The whole point of the split. With a predictor owning LocalTransform, the adapter must
            // not also write it — that is two writers where the later one usually wins, and the
            // entity snaps back on the frames the predictor does not run.
            var view = (IEntityView)_view;
            view.Spawn("uuid-me", isLocal: true, type: PlayerType);
            view.SetState("uuid-me", 1f, 1f, 100, 100);
            Tick();

            var entity = Find("uuid-me");
            _entityManager.AddComponent<PredictedTransform>(entity);

            // Stand in for a predictor having moved the entity this frame.
            var predicted = new float3(50f, 0f, 50f);
            var transform = _entityManager.GetComponentData<LocalTransform>(entity);
            transform.Position = predicted;
            _entityManager.SetComponentData(entity, transform);

            view.SetState("uuid-me", 2f, 2f, 100, 100);
            Tick();

            Assert.AreEqual(predicted, _entityManager.GetComponentData<LocalTransform>(entity).Position,
                "the adapter must not overwrite a transform something else owns");
            Assert.AreEqual(new float3(2f, 0f, 2f), _entityManager.GetComponentData<ReconciliationAnchor>(entity).Position,
                "but the authoritative value still lands, because that is what a predictor rewinds to");
        }

        [Test]
        public void RemovingPredictedTransform_HandsTheTransformBack()
        {
            // A predictor that stops predicting — a spectate, a death — must not leave a transform
            // nobody writes.
            var view = (IEntityView)_view;
            view.Spawn("uuid-me", isLocal: true, type: PlayerType);
            Tick();

            var entity = Find("uuid-me");
            _entityManager.AddComponent<PredictedTransform>(entity);
            view.SetState("uuid-me", 9f, 9f, 100, 100);
            Tick();
            Assert.AreNotEqual(new float3(9f, 0f, 9f), _entityManager.GetComponentData<LocalTransform>(entity).Position);

            _entityManager.RemoveComponent<PredictedTransform>(entity);
            view.SetState("uuid-me", 4f, 5f, 100, 100);
            Tick();

            Assert.AreEqual(new float3(4f, 0f, 5f), _entityManager.GetComponentData<LocalTransform>(entity).Position);
        }

        [Test]
        public void WithoutAPredictor_TheLocalEntityMovesExactlyAsBefore()
        {
            // The regression guard for the shortcut not taken: keying the skip off IsLocal instead of
            // component presence would leave the local avatar frozen in every build that has no
            // predictor — which is every build today.
            var view = (IEntityView)_view;
            view.Spawn("uuid-me", isLocal: true, type: PlayerType);
            view.SetState("uuid-me", 6f, 8f, 100, 100);

            Tick();

            Assert.AreEqual(new float3(6f, 0f, 8f), _entityManager.GetComponentData<LocalTransform>(Find("uuid-me")).Position);
            Assert.AreEqual(new Vector3(6f, 0f, 8f), ViewOf("uuid-me").transform.position, "and its view followed");
        }

        [Test]
        public void QueueIsFullyDrained_EveryTick()
        {
            var view = (IEntityView)_view;
            for (var i = 0; i < 50; i++)
            {
                view.Spawn($"uuid-{i}", isLocal: false, type: PlayerType);
                view.SetState($"uuid-{i}", i, i, 100, 100);
            }

            Assert.AreEqual(100, _view.PendingCommands);

            Tick();

            Assert.AreEqual(0, _view.PendingCommands, "a partial drain would leave entities at the origin");
            Assert.AreEqual(50, MirrorCount());
        }
    }
}
