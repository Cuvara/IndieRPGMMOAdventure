using Cuvara.DOTS.Groups;
using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

namespace Cuvara.DOTS.Simulation
{
    /// <summary>Integrates <see cref="MoveData"/> velocity and reflects it off the entity's bounds.</summary>
    /// <remarks>
    /// After <see cref="MoveTowardSystem"/>: an entity carrying both components is steered first and
    /// bounced second, so the bounds are the last word on where it ends up. The order is stated
    /// rather than left to Entities' fallback, because two systems writing
    /// <see cref="LocalTransform.Position"/> in an unspecified order is a frame-dependent result.
    /// </remarks>
    [BurstCompile]
    [DisableAutoCreation]
    [UpdateInGroup(typeof(MovementSystemGroup))]
    [UpdateAfter(typeof(MoveTowardSystem))]
    internal partial struct MoveBounceSystem : ISystem
    {
        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<MoveData>();
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            var deltaTime = SystemAPI.Time.DeltaTime;

            foreach (var (transform, move) in
                     SystemAPI.Query<RefRW<LocalTransform>, RefRW<MoveData>>())
            {
                var position = transform.ValueRO.Position + move.ValueRO.Velocity * deltaTime;
                var velocity = move.ValueRO.Velocity;
                var min = move.ValueRO.BoundsMin;
                var max = move.ValueRO.BoundsMax;

                // Clamp as well as flip: reflecting the velocity alone leaves the entity outside the
                // box for one frame, and if it is still outside next frame the sign flips again and
                // it sticks to the wall vibrating.
                for (var axis = 0; axis < 3; axis++)
                {
                    if (position[axis] >= min[axis] && position[axis] <= max[axis]) continue;
                    velocity[axis] = -velocity[axis];
                    position[axis] = math.clamp(position[axis], min[axis], max[axis]);
                }

                move.ValueRW.Velocity = velocity;
                transform.ValueRW.Position = position;
            }
        }
    }
}
