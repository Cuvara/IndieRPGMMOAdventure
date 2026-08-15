using System;
using Cuvara.DOTS.Groups;
using Unity.Entities;

namespace Cuvara.DOTS.Netcode
{
    /// <summary>
    /// Installs the snapshot adapter into one world: publishes the
    /// <see cref="NetworkEntityViewReference"/> singleton and creates the drain system inside
    /// <see cref="NetcodeSystemGroup"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A separate entry point rather than an addition to <c>DotsViewBootstrap</c>, and that is the
    /// one-way dependency arrow made concrete: <c>Cuvara.DOTS.Runtime</c> must keep compiling with
    /// <c>com.cuvara.netcode</c> absent, so it cannot name a system that lives in this assembly.
    /// Consumers call <c>DotsViewBootstrap.Install</c> and then this.
    /// </para>
    /// <para>
    /// Idempotent, in the same sense and for the same reason as <c>DotsViewBootstrap.Install</c>: a
    /// second call replaces the referenced view rather than creating a second singleton, which would
    /// make every <c>GetSingleton</c> throw.
    /// </para>
    /// </remarks>
    public static class DotsNetcodeBootstrap
    {
        public static Entity Install(World world, DotsEntityView view)
        {
            if (world == null) throw new ArgumentNullException(nameof(world));
            if (view == null) throw new ArgumentNullException(nameof(view));

            var entityManager = world.EntityManager;
            using var query = entityManager.CreateEntityQuery(ComponentType.ReadWrite<NetworkEntityViewReference>());

            Entity entity;
            if (query.IsEmpty)
            {
                entity = entityManager.CreateEntity();
                entityManager.AddComponentObject(entity, new NetworkEntityViewReference { View = view });
#if UNITY_EDITOR
                entityManager.SetName(entity, "NetworkEntityView");
#endif
            }
            else
            {
                entity = query.GetSingletonEntity();
                entityManager.GetComponentObject<NetworkEntityViewReference>(entity).View = view;
            }

            InstallSystems(world);
            return entity;
        }

        /// <summary>
        /// Creates the drain system and hangs it under <see cref="NetcodeSystemGroup"/>, creating
        /// that group and its parent if the view bootstrap has not already.
        /// </summary>
        public static void InstallSystems(World world)
        {
            if (world == null) throw new ArgumentNullException(nameof(world));

            var initialization = world.GetOrCreateSystemManaged<InitializationSystemGroup>();
            var netcode = world.GetOrCreateSystemManaged<NetcodeSystemGroup>();
            initialization.AddSystemToUpdateList(netcode);

            // Into SnapshotApplyGroup, which is where the drain declares itself. Adding it straight
            // to NetcodeSystemGroup — as this did before the sub-groups existed — puts the system in
            // a group its [UpdateInGroup] does not name, so the ordering relation between it and
            // PredictionSystemGroup never resolves and prediction can run BEFORE the anchor it
            // reconciles against is written. That failed three prediction tests with the marker
            // simply never being claimed, which pointed at the predictor rather than at this line.
            var snapshotApply = world.GetOrCreateSystemManaged<SnapshotApplyGroup>();
            netcode.AddSystemToUpdateList(snapshotApply);
            snapshotApply.AddSystemToUpdateList(world.GetOrCreateSystem<NetworkViewCommandSystem>());

            // Manual adds are not sorted automatically; without this the group's ordering relations
            // are declared but never applied.
            initialization.SortSystems();
        }

        /// <summary>
        /// Removes the singleton. Safe on a world that never had one, and does not destroy the
        /// mirrored entities — those belong to the world, and a consumer tearing down a session
        /// usually disposes the world itself.
        /// </summary>
        public static void Uninstall(World world)
        {
            if (world == null || !world.IsCreated) return;

            var entityManager = world.EntityManager;
            using var query = entityManager.CreateEntityQuery(ComponentType.ReadWrite<NetworkEntityViewReference>());
            if (query.IsEmpty) return;

            entityManager.DestroyEntity(query.GetSingletonEntity());
        }
    }
}
