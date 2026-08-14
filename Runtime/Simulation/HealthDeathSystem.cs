using Cuvara.DOTS.Groups;
using Unity.Burst;
using Unity.Entities;

namespace Cuvara.DOTS.Simulation
{
    /// <summary>Destroys entities whose <see cref="Health.Current"/> has reached zero or below.</summary>
    /// <remarks>
    /// <para>
    /// <b>Two couplings were removed on the way in, and both mattered.</b> The reference version
    /// filtered its query on a game-specific enemy tag, and incremented a game-specific stats
    /// singleton. Neither can exist in a shared package: the tag is one game's vocabulary, and the
    /// singleton makes the system fail — or silently do nothing — in any world that has not created
    /// it. What is left is the rule itself, applied to whatever carries <see cref="Health"/>.
    /// </para>
    /// <para>
    /// <b>No death event is published.</b> The package's publisher seam is managed and lives in
    /// <c>Cuvara.DOTS.DI</c>; reaching it from a Bursted <c>ISystem</c> is not possible, and adding a
    /// managed drain per frame to carry a counter would cost more than it is worth. A consumer that
    /// needs to observe deaths should watch its own component going away, or run its own system
    /// before this one.
    /// </para>
    /// <para>
    /// Destruction goes through <see cref="DotsEndSimulationCommandBufferSystem"/> — the package's
    /// own buffer, which plays back at the end of <see cref="GameplaySystemGroup"/> and therefore
    /// before the transform systems and long before any view is synced. Unity's
    /// <c>EndSimulationEntityCommandBufferSystem</c>, which the reference used, plays back after the
    /// transform systems and would leave exactly one group in which a view could be synced against a
    /// corpse.
    /// </para>
    /// </remarks>
    [BurstCompile]
    [DisableAutoCreation]
    [UpdateInGroup(typeof(LifecycleSystemGroup))]
    internal partial struct HealthDeathSystem : ISystem
    {
        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<Health>();
            state.RequireForUpdate<DotsEndSimulationCommandBufferSystem.Singleton>();
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            var commandBuffer = SystemAPI
                .GetSingleton<DotsEndSimulationCommandBufferSystem.Singleton>()
                .CreateCommandBuffer(state.WorldUnmanaged);

            foreach (var (health, entity) in SystemAPI.Query<RefRO<Health>>().WithEntityAccess())
            {
                if (health.ValueRO.Current <= 0) commandBuffer.DestroyEntity(entity);
            }
        }
    }
}
