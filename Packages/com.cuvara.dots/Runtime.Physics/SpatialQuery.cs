using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Physics;

namespace Cuvara.DOTS.Physics
{
    /// <summary>
    /// ECS-friendly spatial query utilities using Unity.Physics <see cref="CollisionWorld"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Static methods for common queries: overlap sphere, raycast, closest entity.
    /// All take a <see cref="CollisionWorld"/> reference — obtain it from
    /// <see cref="PhysicsWorldSingleton"/> in a system.
    /// </para>
    /// <para>
    /// <b>No allocations.</b> Results are written to caller-provided
    /// <see cref="NativeList{T}"/> buffers.
    /// </para>
    /// </remarks>
    public static class SpatialQuery
    {
        /// <summary>
        /// Finds all physics bodies within <paramref name="radius"/> of
        /// <paramref name="center"/>. Results are appended to <paramref name="hits"/>.
        /// </summary>
        /// <returns>Number of hits found.</returns>
        public static int OverlapSphere(
            in CollisionWorld collisionWorld,
            float3 center,
            float radius,
            ref NativeList<DistanceHit> hits,
            CollisionFilter filter = default)
        {
            if (filter.Equals(default(CollisionFilter)))
                filter = CollisionFilter.Default;

            var input = new PointDistanceInput
            {
                Position = center,
                MaxDistance = radius,
                Filter = filter,
            };

            int before = hits.Length;
            collisionWorld.CalculateDistance(input, ref hits);
            return hits.Length - before;
        }

        /// <summary>
        /// Casts a ray from <paramref name="from"/> to <paramref name="to"/>.
        /// Returns true if something was hit.
        /// </summary>
        public static bool Raycast(
            in CollisionWorld collisionWorld,
            float3 from,
            float3 to,
            out Unity.Physics.RaycastHit hit,
            CollisionFilter filter = default)
        {
            if (filter.Equals(default(CollisionFilter)))
                filter = CollisionFilter.Default;

            var input = new RaycastInput
            {
                Start = from,
                End = to,
                Filter = filter,
            };

            return collisionWorld.CastRay(input, out hit);
        }

        /// <summary>
        /// Casts a ray and returns all hits along the path.
        /// </summary>
        public static int RaycastAll(
            in CollisionWorld collisionWorld,
            float3 from,
            float3 to,
            ref NativeList<Unity.Physics.RaycastHit> hits,
            CollisionFilter filter = default)
        {
            if (filter.Equals(default(CollisionFilter)))
                filter = CollisionFilter.Default;

            var input = new RaycastInput
            {
                Start = from,
                End = to,
                Filter = filter,
            };

            int before = hits.Length;
            collisionWorld.CastRay(input, ref hits);
            return hits.Length - before;
        }

        /// <summary>
        /// Finds the closest physics body to <paramref name="origin"/> within
        /// <paramref name="maxDistance"/>.
        /// </summary>
        public static bool ClosestBody(
            in CollisionWorld collisionWorld,
            float3 origin,
            float maxDistance,
            out DistanceHit closestHit,
            CollisionFilter filter = default)
        {
            if (filter.Equals(default(CollisionFilter)))
                filter = CollisionFilter.Default;

            var input = new PointDistanceInput
            {
                Position = origin,
                MaxDistance = maxDistance,
                Filter = filter,
            };

            return collisionWorld.CalculateDistance(input, out closestHit);
        }
    }
}
