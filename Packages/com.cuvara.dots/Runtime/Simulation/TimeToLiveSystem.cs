using Cuvara.DOTS.Groups;
using Unity.Burst;
using Unity.Entities;

namespace Cuvara.DOTS.Simulation
{
    /// <summary>Counts down <see cref="TimeToLive"/> and destroys entities whose time is up.</summary>
    /// <remarks>
    /// <para>
    /// After <see cref="HealthDeathSystem"/>, so an entity that dies of damage and expires on the
    /// same frame is recorded once by each — harmless, since a second
    /// <c>DestroyEntity</c> on an already-destroyed entity is a no-op at playback — and the order is
    /// fixed rather than incidental.
    /// </para>
    /// <para>
    /// The reference version resolved Unity's end-of-simulation buffer. This one takes the package's
    /// own, for the reason given on <see cref="DotsEndSimulationCommandBufferSystem"/>: playback
    /// before the transform systems is what keeps a view from being synced against an entity that
    /// died this frame.
    /// </para>
    /// <para>
    /// The countdown is written even on the frame the entity dies. That is deliberate: a consumer
    /// reading <see cref="TimeToLive.Remaining"/> during playback sees a value that has actually
    /// reached zero, rather than the last positive one.
    /// </para>
    /// </remarks>
    [BurstCompile]
    [DisableAutoCreation]
    [UpdateInGroup(typeof(LifecycleSystemGroup))]
    [UpdateAfter(typeof(HealthDeathSystem))]
    internal partial struct TimeToLiveSystem : ISystem
    {
        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<TimeToLive>();
            state.RequireForUpdate<DotsEndSimulationCommandBufferSystem.Singleton>();
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            var deltaTime = SystemAPI.Time.DeltaTime;
            var commandBuffer = SystemAPI
                .GetSingleton<DotsEndSimulationCommandBufferSystem.Singleton>()
                .CreateCommandBuffer(state.WorldUnmanaged);

            foreach (var (timeToLive, entity) in SystemAPI.Query<RefRW<TimeToLive>>().WithEntityAccess())
            {
                timeToLive.ValueRW.Remaining -= deltaTime;
                if (timeToLive.ValueRO.Remaining <= 0f) commandBuffer.DestroyEntity(entity);
            }
        }
    }
}
