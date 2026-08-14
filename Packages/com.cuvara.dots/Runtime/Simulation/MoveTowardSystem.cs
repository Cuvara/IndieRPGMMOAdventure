using Cuvara.DOTS.Groups;
using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

namespace Cuvara.DOTS.Simulation
{
    /// <summary>Advances every <see cref="MoveToward"/> entity toward its target.</summary>
    /// <remarks>
    /// <para>
    /// First in <see cref="MovementSystemGroup"/>, so the position later movement systems read is
    /// this frame's, not the previous one's.
    /// </para>
    /// <para>
    /// The step is clamped to the remaining distance, so an entity cannot overshoot its target at
    /// any speed or delta time — without that clamp a fast entity oscillates around the target
    /// forever, which is the failure this looks like when frame time spikes rather than when the
    /// speed is wrong.
    /// </para>
    /// </remarks>
    [BurstCompile]
    [DisableAutoCreation]
    [UpdateInGroup(typeof(MovementSystemGroup))]
    internal partial struct MoveTowardSystem : ISystem
    {
        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<MoveToward>();
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            var deltaTime = SystemAPI.Time.DeltaTime;

            foreach (var (transform, move) in
                     SystemAPI.Query<RefRW<LocalTransform>, RefRO<MoveToward>>())
            {
                var position = transform.ValueRO.Position;
                var toTarget = move.ValueRO.Target - position;
                var distance = math.length(toTarget);

                // Arrived — and the zero-distance case must be caught here, because normalising a
                // zero vector below would produce NaN and poison the transform permanently.
                if (distance <= move.ValueRO.StopDistance || distance <= math.EPSILON) continue;

                var step = math.normalize(toTarget) * move.ValueRO.Speed * deltaTime;
                if (math.length(step) > distance) step = toTarget;

                transform.ValueRW.Position = position + step;
            }
        }
    }
}
