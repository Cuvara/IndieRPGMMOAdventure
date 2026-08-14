using System;
using Cuvara.DOTS.Groups;
using Unity.Entities;

namespace Cuvara.DOTS.Simulation
{
    /// <summary>
    /// Creates the simulation systems and puts them in the package's movement and lifecycle groups.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Separate from the view bootstrap on purpose. The simulation components are usable without the
    /// view layer — an entity that moves, spins and expires needs no GameObject — so installing them
    /// must not require an <c>EntityViewRegistry</c> or an <c>IViewAssetProvider</c>. A consumer that
    /// wants both calls both.
    /// </para>
    /// <para>
    /// Every system carries <c>[DisableAutoCreation]</c>, matching the rest of the package: which
    /// systems exist in a world is the consumer's decision, not a side effect of the assembly being
    /// referenced.
    /// </para>
    /// <para>
    /// Idempotent. <c>GetOrCreateSystem</c> returns an existing instance and
    /// <c>AddSystemToUpdateList</c> ignores a system already in the list, so calling this twice —
    /// or after the view bootstrap, which creates the same groups — changes nothing.
    /// </para>
    /// </remarks>
    public static class DotsSimulationBootstrap
    {
        /// <summary>
        /// Installs the simulation systems into <paramref name="world"/>, creating the package's
        /// group tree if the view bootstrap has not already done so.
        /// </summary>
        public static void InstallSimulationSystems(World world)
        {
            if (world == null) throw new ArgumentNullException(nameof(world));

            var simulation = world.GetOrCreateSystemManaged<SimulationSystemGroup>();
            var gameplay = world.GetOrCreateSystemManaged<GameplaySystemGroup>();
            var movement = world.GetOrCreateSystemManaged<MovementSystemGroup>();
            var lifecycle = world.GetOrCreateSystemManaged<LifecycleSystemGroup>();
            var commandBuffer = world.GetOrCreateSystemManaged<DotsEndSimulationCommandBufferSystem>();

            simulation.AddSystemToUpdateList(gameplay);
            gameplay.AddSystemToUpdateList(movement);
            gameplay.AddSystemToUpdateList(lifecycle);
            gameplay.AddSystemToUpdateList(commandBuffer);

            movement.AddSystemToUpdateList(world.GetOrCreateSystem<MoveTowardSystem>());
            movement.AddSystemToUpdateList(world.GetOrCreateSystem<MoveBounceSystem>());
            movement.AddSystemToUpdateList(world.GetOrCreateSystem<SpinSystem>());

            lifecycle.AddSystemToUpdateList(world.GetOrCreateSystem<HealthDeathSystem>());
            lifecycle.AddSystemToUpdateList(world.GetOrCreateSystem<TimeToLiveSystem>());

            // Adding a system manually does not sort the group: without this the UpdateAfter chains
            // above are declared and not applied, and the systems run in insertion order by luck.
            simulation.SortSystems();
        }
    }
}
