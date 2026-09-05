using Cuvara.DOTS.Groups;
using Unity.Collections;
using Unity.Entities;
using Unity.Physics;
using Unity.Physics.Systems;

namespace Cuvara.DOTS.Physics
{
    /// <summary>
    /// Reads Unity.Physics collision events each frame and stores the count in
    /// <see cref="CollisionEventBuffer"/>. Game systems read full event details
    /// directly from <see cref="SimulationSingleton"/>.
    /// </summary>
    [DisableAutoCreation]
    [UpdateInGroup(typeof(GameplaySystemGroup))]
    [UpdateAfter(typeof(PhysicsSystemGroup))]
    public partial struct CollisionEventSystem : ISystem
    {
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<SimulationSingleton>();
            state.RequireForUpdate<PhysicsWorldSingleton>();
        }

        public void OnUpdate(ref SystemState state)
        {
            var simulation = SystemAPI.GetSingleton<SimulationSingleton>();

            int count = 0;
            foreach (var _ in simulation.AsSimulation().CollisionEvents)
                count++;

            if (!SystemAPI.HasSingleton<CollisionEventBuffer>())
            {
                var entity = state.EntityManager.CreateEntity();
                state.EntityManager.AddComponentData(entity, new CollisionEventBuffer());
#if UNITY_EDITOR
                state.EntityManager.SetName(entity, "CollisionEventBuffer");
#endif
            }

            SystemAPI.GetSingletonRW<CollisionEventBuffer>().ValueRW.Count = count;
        }
    }

    /// <summary>
    /// Singleton carrying the collision event count for this frame.
    /// Game systems that need full event details read them directly from
    /// <see cref="SimulationSingleton"/>.
    /// </summary>
    public struct CollisionEventBuffer : IComponentData
    {
        /// <summary>Number of collision events this frame.</summary>
        public int Count;
    }
}
