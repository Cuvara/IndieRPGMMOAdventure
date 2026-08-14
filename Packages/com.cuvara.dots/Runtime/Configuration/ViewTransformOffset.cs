using Unity.Mathematics;
using Unity.Entities;

namespace Cuvara.DOTS.Configuration
{
    /// <summary>
    /// Per-view offset applied on top of the entity's world transform, every frame.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Why this is a component and not just a spawn-time adjustment.</b> The transform sync
    /// overwrites the GameObject's position, rotation and scale from <c>LocalToWorld</c> on every
    /// frame. An offset applied only at spawn would therefore survive exactly one frame and then be
    /// silently erased — which looks like the offset "not working" and is very hard to read from the
    /// symptom. Keeping it on the entity lets the sync re-apply it, so it holds.
    /// </para>
    /// <para>
    /// Added to every view-linked entity, with identity values when no config supplied any, so the
    /// sync query stays a single query rather than one query per has-offset combination.
    /// </para>
    /// </remarks>
    public struct ViewTransformOffset : IComponentData
    {
        public float3 Position;
        public quaternion Rotation;
        public float Scale;

        /// <summary>No offset: the view sits exactly where its entity does.</summary>
        public static ViewTransformOffset Identity => new ViewTransformOffset
        {
            Position = float3.zero,
            Rotation = quaternion.identity,
            Scale = 1f,
        };
    }
}
