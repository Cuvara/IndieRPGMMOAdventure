using Cuvara.DOTS.Groups;
using Cuvara.DOTS.Simulation;
using Unity.Burst;
using Unity.Entities;
using Unity.Physics;

namespace Cuvara.DOTS.Physics
{
    /// <summary>
    /// Bridges the package's <see cref="MoveData"/> component to Unity.Physics
    /// <see cref="PhysicsVelocity"/>. Entities with both components have their
    /// <c>MoveData.Velocity</c> written to <c>PhysicsVelocity.Linear</c>, letting
    /// Unity.Physics handle integration and collision response.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This replaces <see cref="MoveBounceSystem"/>'s manual AABB reflection with real
    /// collision. Entities that have <see cref="PhysicsCollider"/> +
    /// <see cref="PhysicsVelocity"/> + <see cref="MoveData"/> are driven by physics;
    /// entities with only <see cref="MoveData"/> keep the old bounce behaviour.
    /// </para>
    /// <para>
    /// Runs in <see cref="MovementSystemGroup"/>, before Unity.Physics steps the world.
    /// The velocity is set here; position is updated by <c>StepPhysicsWorld</c>.
    /// </para>
    /// </remarks>
    [DisableAutoCreation]
    [UpdateInGroup(typeof(MovementSystemGroup))]
    public partial struct PhysicsMovementBridge : ISystem
    {
        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            new WriteVelocityJob().ScheduleParallel();
        }
    }

    [BurstCompile]
    internal partial struct WriteVelocityJob : IJobEntity
    {
        private void Execute(in MoveData move, ref PhysicsVelocity velocity)
        {
            velocity.Linear = move.Velocity;
        }
    }
}
