using System;
using System.Collections.Generic;
using Cuvara.DOTS.Groups;
using Cuvara.DOTS.Provisioning;
using Unity.Collections;
using Unity.Entities;

namespace Cuvara.DOTS.Views
{
    /// <summary>
    /// Puts the entities of an unloading chunk through the ordinary despawn path, so their assets
    /// can be released without leaving anything pointing at them.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The inversion this implements.</b> Releasing a chunk used to destroy the assets first and
    /// leave the links dead, with nobody to clean them. Now the views come down first — recycled to
    /// the pool, handles dropped, links cleared — and only then does the refcount reach zero and the
    /// asset get released.
    /// </para>
    /// <para>
    /// <b>Two halves, and only one of them is synchronous.</b> Recycling the instance and dropping
    /// the handle are managed side-table operations and happen immediately, inside the
    /// <c>ReleaseChunk</c> call. Removing <see cref="EntityViewLink"/> and
    /// <see cref="EntityViewLinkCleanup"/> is a structural change, and it is recorded into
    /// <see cref="DotsEndSimulationCommandBufferSystem"/> rather than applied inline — an
    /// <c>EntityManager</c> structural change from arbitrary consumer code would invalidate any
    /// chunk iteration in flight.
    /// </para>
    /// <para>
    /// <b>The state a caller observes mid-cascade</b>, between the call returning and that buffer
    /// playing back at the end of the next <see cref="GameplaySystemGroup"/>: the GameObjects are
    /// already gone and the assets already released, while the entities still carry an
    /// <see cref="EntityViewLink"/> whose handle no longer resolves. Nothing misbehaves in that
    /// window — <c>EntityViewRegistry.ApplyTransform</c> ignores an unknown handle, and the despawn
    /// system's <c>Despawn</c> on a dropped handle is a no-op — but a consumer querying for
    /// <c>EntityViewLink</c> in that window still sees it. Query for a resolvable view via
    /// <c>EntityViewRegistry.Get</c>, not by the presence of the component.
    /// </para>
    /// <para>
    /// <b>No respawn loop.</b> The cascade removes the link and does <i>not</i> re-add an
    /// <see cref="EntityViewRequest"/>, and the spawn system acts only on requests — so an entity
    /// whose view was cascaded away stays view-less until someone deliberately requests one again.
    /// That is the intended outcome of a streaming unload: the entity survives, its region does not.
    /// </para>
    /// </remarks>
    public sealed class EntityViewCascade : IViewCascadeSink
    {
        private readonly World _world;
        private readonly EntityViewRegistry _registry;

        public EntityViewCascade(World world, EntityViewRegistry registry)
        {
            _world = world ?? throw new ArgumentNullException(nameof(world));
            _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        }

        public int CascadeDespawn(IReadOnlyCollection<string> keys)
        {
            if (keys == null || keys.Count == 0 || !_world.IsCreated) return 0;

            var targets = new HashSet<string>(keys);
            var entityManager = _world.EntityManager;

            using var query = entityManager.CreateEntityQuery(ComponentType.ReadOnly<EntityViewLink>());
            var entities = query.ToEntityArray(Allocator.Temp);
            var links = query.ToComponentDataArray<EntityViewLink>(Allocator.Temp);

            var commandBuffer = GetCommandBuffer();
            var despawned = 0;

            for (var i = 0; i < entities.Length; i++)
            {
                var viewId = links[i].ViewId;
                if (!_registry.TryGetKey(viewId, out var key) || !targets.Contains(key)) continue;

                // The ordinary despawn path: recycle the instance, drop the handle, publish
                // ViewDespawned. Synchronous, because none of it is a structural change.
                if (!_registry.Despawn(viewId)) continue;

                commandBuffer.RemoveComponent<EntityViewLink>(entities[i]);
                commandBuffer.RemoveComponent<EntityViewLinkCleanup>(entities[i]);
                despawned++;
            }

            links.Dispose();
            entities.Dispose();
            return despawned;
        }

        /// <summary>
        /// The package's own buffer, so playback lands at the end of
        /// <see cref="GameplaySystemGroup"/> — before the transform systems and long before
        /// presentation.
        /// </summary>
        /// <remarks>
        /// If the system is missing, the world was not set up through
        /// <c>DotsViewBootstrap.InstallSystems</c>. Falling back to an immediate
        /// <c>EntityManager</c> change would be worse than failing: it would work in a test and
        /// corrupt an iteration in production.
        /// </remarks>
        private EntityCommandBuffer GetCommandBuffer()
        {
            var system = _world.GetExistingSystemManaged<DotsEndSimulationCommandBufferSystem>();
            if (system == null)
            {
                throw new InvalidOperationException(
                    "DotsEndSimulationCommandBufferSystem is missing from this world. Call " +
                    "DotsViewBootstrap.Install (or InstallSystems) before releasing chunks.");
            }

            return system.CreateCommandBuffer();
        }
    }
}
