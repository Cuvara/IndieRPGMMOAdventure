using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

namespace Cuvara.DOTS.Samples.HybridViews
{
    /// <summary>
    /// Writes <see cref="LocalTransform"/> for every <see cref="OrbitMotion"/> entity.
    /// </summary>
    /// <remarks>
    /// Left in the default <see cref="SimulationSystemGroup"/> on purpose: the package's view
    /// systems live in <see cref="PresentationSystemGroup"/>, which the root ordering runs after
    /// simulation, so whatever this writes is what the views show in the same frame. That
    /// relationship is the thing the sample is demonstrating — move this system into presentation
    /// and the views go one frame stale.
    /// <para>
    /// The group is named explicitly rather than left to the default, matching the package's own
    /// rule: no system should sit in a group by accident. A sample system stays in Unity's
    /// <see cref="SimulationSystemGroup"/> rather than in the package's <c>MovementSystemGroup</c>
    /// because that group is <c>[DisableAutoCreation]</c> and created by
    /// <c>DotsViewBootstrap</c> — an auto-created system cannot reliably be placed inside it.
    /// </para>
    /// </remarks>
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [BurstCompile]
    public partial struct OrbitMotionSystem : ISystem
    {
        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<OrbitMotion>();
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            state.Dependency = new OrbitJob
            {
                ElapsedTime = (float)SystemAPI.Time.ElapsedTime,
            }.ScheduleParallel(state.Dependency);
        }
    }

    [BurstCompile]
    public partial struct OrbitJob : IJobEntity
    {
        public float ElapsedTime;

        private void Execute(ref LocalTransform transform, in OrbitMotion motion)
        {
            var angle = motion.Phase + motion.Speed * ElapsedTime;

            transform.Position = new float3(
                math.cos(angle) * motion.Radius,
                motion.Height,
                math.sin(angle) * motion.Radius);

            // Spin as well as orbit, so rotation sync is visible and not just position sync.
            transform.Rotation = quaternion.AxisAngle(math.up(), angle * 2f);
        }
    }
}
