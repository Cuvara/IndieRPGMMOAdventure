namespace Scripts.UI.Hud.Ecs
{
    using System;
    using Unity.Entities;

    /// <summary>
    /// Installs the two HUD systems into one world, and removes them again — the ECS half
    /// of what <see cref="HudWorldBridge"/> wires.
    /// </summary>
    /// <remarks>
    /// <para>A static entry point rather than logic inside the MonoBehaviour, for the same
    /// reason <c>DotsNetcodeBootstrap</c> is one: tests install into a throwaway world with
    /// no scene, no GameObject and no UIDocument, and exercise exactly the sequence the
    /// component runs.</para>
    ///
    /// <para><b>Idempotent.</b> <c>GetOrCreateSystem</c> returns the existing instance and
    /// <c>AddSystemToUpdateList</c> refuses duplicates, so a second install is a no-op —
    /// the same contract the dots bootstraps hold.</para>
    ///
    /// <para><b>Uninstall destroys the systems.</b> Unlike the dots view systems — which
    /// are root-scoped because other scenes stand on them — these two exist only for the
    /// HUD, and the default world outlives the scene: leaving the aggregator running would
    /// walk the mirror query every frame for a screen that no longer exists. Destroying
    /// <see cref="HudStateSystem"/> also removes the <see cref="HudState"/> singleton (its
    /// <c>OnDestroy</c>), so a reinstall starts from a fresh value, not a stale one.</para>
    /// </remarks>
    public static class HudEcsBootstrap
    {
        /// <summary>Creates and schedules both systems; returns the bridge to register sinks on.</summary>
        public static HudBridgeSystem Install(World world)
        {
            if (world == null) throw new ArgumentNullException(nameof(world));

            // Aggregator into simulation: it must run after the netcode drain and the
            // prediction driver (both under InitializationSystemGroup) and before the
            // bridge in PresentationSystemGroup — the root-group order guarantees both.
            var simulation = world.GetOrCreateSystemManaged<SimulationSystemGroup>();
            simulation.AddSystemToUpdateList(world.GetOrCreateSystem<HudStateSystem>());
            simulation.SortSystems();

            var presentation = world.GetOrCreateSystemManaged<PresentationSystemGroup>();
            var bridge = world.GetOrCreateSystemManaged<HudBridgeSystem>();
            presentation.AddSystemToUpdateList(bridge);
            presentation.SortSystems();

            return bridge;
        }

        /// <summary>Destroys both systems. Safe on a world that never had them, or is gone.</summary>
        public static void Uninstall(World world)
        {
            if (world == null || !world.IsCreated)
            {
                return;
            }

            var aggregator = world.GetExistingSystem<HudStateSystem>();
            if (aggregator != default)
            {
                world.DestroySystem(aggregator);
            }

            var bridge = world.GetExistingSystemManaged<HudBridgeSystem>();
            if (bridge != null)
            {
                world.DestroySystemManaged(bridge);
            }
        }
    }
}
