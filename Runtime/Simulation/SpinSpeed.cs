using Unity.Entities;

namespace Cuvara.DOTS.Simulation
{
    /// <summary>Constant rotation about the entity's Y axis, in radians per second.</summary>
    /// <remarks>
    /// Rotation only — <see cref="SpinSystem"/> never writes position, so an entity may carry this
    /// alongside <see cref="MoveToward"/> or <see cref="MoveData"/> without the two fighting over
    /// the same field.
    /// </remarks>
    public struct SpinSpeed : IComponentData
    {
        public float RadiansPerSecond;
    }
}
