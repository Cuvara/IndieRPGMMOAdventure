using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Cuvara.DOTS.Groups;
using Unity.Transforms;

namespace Cuvara.DOTS.Views
{
    /// <summary>
    /// Turns <see cref="EntityViewRequest"/> into a live view plus an <see cref="EntityViewLink"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Not Bursted, and not schedulable.</b> Spawning goes through the managed pool, so
    /// <see cref="OnUpdate"/> runs on the main thread by definition. There is nothing to gain by
    /// pretending otherwise, and no measurement here claims a win.
    /// </para>
    /// <para>
    /// A request whose key is not warm is <b>left in place and retried next frame</b> rather than
    /// force-loading synchronously. The pool would happily load on demand and hitch; deferring
    /// keeps the hitch in <see cref="Provisioning.ChunkViewProvisioner"/>'s prewarm, where it is
    /// asynchronous and expected. The visible cost is that an entity spawned before its chunk
    /// finished warming stays invisible for a few frames.
    /// </para>
    /// </remarks>
    // After despawn — see EntityViewDespawnSystem for why that order. Expressed as an explicit
    // UpdateAfter rather than OrderFirst/OrderLast: Entities batches those separately and then drops
    // ordering relations between the batch and normal members, so this is the relation that holds.
    [DisableAutoCreation]
    [UpdateInGroup(typeof(ViewLifecycleGroup))]
    [UpdateAfter(typeof(EntityViewDespawnSystem))]
    internal partial struct EntityViewSpawnSystem : ISystem
    {
        private EntityQuery _pending;

        public void OnCreate(ref SystemState state)
        {
            _pending = new EntityQueryBuilder(Allocator.Temp)
                .WithAll<EntityViewRequest>()
                .WithNone<EntityViewLink>()
                .Build(ref state);

            state.RequireForUpdate(_pending);
            state.RequireForUpdate<EntityViewRegistryReference>();
        }

        public void OnUpdate(ref SystemState state)
        {
            var registry = SystemAPI.ManagedAPI.GetSingleton<EntityViewRegistryReference>().Registry;
            if (registry == null) return;

            var entities = _pending.ToEntityArray(Allocator.Temp);
            var entityManager = state.EntityManager;

            for (var i = 0; i < entities.Length; i++)
            {
                var entity = entities[i];
                var key = entityManager.GetComponentData<EntityViewRequest>(entity).ViewKey.ToString();
                if (!registry.IsWarm(key))
                {
                    // Retried next frame — but counted, so a key that will never arrive (a typo, a
                    // prefab missing from the chunk manifest) eventually says so instead of
                    // deferring in silence forever. One warning per key, not one per frame.
                    registry.NoteDeferredSpawn(key);
                    continue;
                }

                // LocalToWorld, not LocalTransform: LocalTransform is relative to the parent, so a
                // parented entity would place its view at local coordinates. LocalToWorld is what
                // TransformSystemGroup just computed and is correct either way.
                var hasTransform = entityManager.HasComponent<LocalToWorld>(entity);
                var localToWorld = hasTransform ? entityManager.GetComponentData<LocalToWorld>(entity) : default;
                var position = hasTransform ? localToWorld.Position : float3.zero;

                // Spawn already rotated: spawning at identity and letting the first sync correct it
                // shows one frame of wrong facing on everything that spawns already oriented.
                var rotation = hasTransform ? localToWorld.Rotation : quaternion.identity;

                var viewId = registry.Spawn(key, position, rotation);
                if (viewId == 0) continue;

                entityManager.AddComponentData(entity, new EntityViewLink { ViewId = viewId });
                entityManager.AddComponentData(entity, new EntityViewLinkCleanup { ViewId = viewId });
                entityManager.RemoveComponent<EntityViewRequest>(entity);
            }

            entities.Dispose();
        }
    }
}
