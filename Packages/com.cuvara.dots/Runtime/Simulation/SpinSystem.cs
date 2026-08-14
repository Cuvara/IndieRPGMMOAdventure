using Cuvara.DOTS.Groups;
using Unity.Burst;
using Unity.Entities;
using Unity.Transforms;

namespace Cuvara.DOTS.Simulation
{
    /// <summary>Rotates every <see cref="SpinSpeed"/> entity about its Y axis.</summary>
    /// <remarks>
    /// Last in <see cref="MovementSystemGroup"/> only to fix an order; it writes rotation and the
    /// two movement systems write position, so it cannot actually contend with them.
    /// </remarks>
    [BurstCompile]
    [DisableAutoCreation]
    [UpdateInGroup(typeof(MovementSystemGroup))]
    [UpdateAfter(typeof(MoveBounceSystem))]
    internal partial struct SpinSystem : ISystem
    {
        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<SpinSpeed>();
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            var deltaTime = SystemAPI.Time.DeltaTime;

            foreach (var (transform, spin) in
                     SystemAPI.Query<RefRW<LocalTransform>, RefRO<SpinSpeed>>())
            {
                transform.ValueRW = transform.ValueRO.RotateY(spin.ValueRO.RadiansPerSecond * deltaTime);
            }
        }
    }
}
