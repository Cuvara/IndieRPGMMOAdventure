using Unity.Entities;

namespace Cuvara.DOTS.Samples.HybridViews
{
    /// <summary>
    /// Makes an entity circle the origin so the transform sync has something to copy every frame.
    /// </summary>
    /// <remarks>
    /// Nothing in the package requires this component — it exists so a static demo does not look
    /// identical to a broken one. A view that never moves proves the spawn path and nothing else.
    /// </remarks>
    public struct OrbitMotion : IComponentData
    {
        public float Radius;

        /// <summary>Radians per second.</summary>
        public float Speed;

        /// <summary>Starting angle, so entities do not stack on top of each other.</summary>
        public float Phase;

        public float Height;
    }
}
