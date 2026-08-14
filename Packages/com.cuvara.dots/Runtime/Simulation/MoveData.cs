using Unity.Entities;
using Unity.Mathematics;

namespace Cuvara.DOTS.Simulation
{
    /// <summary>
    /// Constant-velocity movement inside an axis-aligned box, reflecting off each face.
    /// </summary>
    /// <remarks>
    /// The bounds live on the component rather than in a singleton so two entities can bounce in
    /// different volumes — a world singleton would make every bouncing entity share one box, which
    /// is a scene's decision and not a package's.
    /// </remarks>
    public struct MoveData : IComponentData
    {
        public float3 Velocity;
        public float3 BoundsMin;
        public float3 BoundsMax;
    }
}
