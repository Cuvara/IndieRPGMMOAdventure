using Unity.Collections;
using Cuvara.DOTS.Groups;
using Unity.Entities;

namespace Cuvara.DOTS.Views
{
    /// <summary>
    /// Recycles views whose entity was destroyed, then lets the entity finish dying.
    /// </summary>
    /// <remarks>
    /// Matches <c>EntityViewLinkCleanup</c> without <c>EntityViewLink</c> — a shape an entity can
    /// only reach by being destroyed, since the spawn system always adds the two together.
    /// Removing the cleanup component is what actually frees the entity id; skip that and the
    /// world fills up with zombies.
    /// </remarks>
    // First in ViewLifecycleGroup: recycling a dead entity's view before this frame's spawns lets
    // the pool hand the freed instance straight back instead of instantiating another.
    [DisableAutoCreation]
    [UpdateInGroup(typeof(ViewLifecycleGroup))]
    internal partial struct EntityViewDespawnSystem : ISystem
    {
        private EntityQuery _destroyed;

        public void OnCreate(ref SystemState state)
        {
            _destroyed = new EntityQueryBuilder(Allocator.Temp)
                .WithAll<EntityViewLinkCleanup>()
                .WithNone<EntityViewLink>()
                .Build(ref state);

            state.RequireForUpdate(_destroyed);
            state.RequireForUpdate<EntityViewRegistryReference>();
        }

        public void OnUpdate(ref SystemState state)
        {
            var registry = SystemAPI.ManagedAPI.GetSingleton<EntityViewRegistryReference>().Registry;

            var entities = _destroyed.ToEntityArray(Allocator.Temp);
            var cleanups = _destroyed.ToComponentDataArray<EntityViewLinkCleanup>(Allocator.Temp);

            if (registry != null)
            {
                for (var i = 0; i < cleanups.Length; i++) registry.Despawn(cleanups[i].ViewId);
            }

            state.EntityManager.RemoveComponent<EntityViewLinkCleanup>(entities);

            cleanups.Dispose();
            entities.Dispose();
        }
    }
}
