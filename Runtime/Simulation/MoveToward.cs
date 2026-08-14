using Unity.Entities;
using Unity.Mathematics;

namespace Cuvara.DOTS.Simulation
{
    /// <summary>
    /// Moves the entity toward a fixed world-space point at constant speed, stopping on arrival.
    /// </summary>
    /// <remarks>
    /// <see cref="StopDistance"/> exists because the implementation this was lifted from hardcoded
    /// an arrival epsilon of 0.1 world units. That number is a tuning value for one game's scale,
    /// not a constant a shared package may impose: at 0.1 an entity in a centimetre-scale scene
    /// never arrives, and one in a kilometre-scale scene stops visibly short. Zero means "stop only
    /// on exact arrival", which the overshoot clamp in <see cref="MoveTowardSystem"/> guarantees is
    /// reachable.
    /// </remarks>
    public struct MoveToward : IComponentData
    {
        public float3 Target;

        /// <summary>World units per second.</summary>
        public float Speed;

        /// <summary>Distance at which the entity is considered arrived and stops moving.</summary>
        public float StopDistance;
    }
}
