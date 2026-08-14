using Unity.Mathematics;

namespace Cuvara.DOTS.Views
{
    /// <summary>
    /// One entity's transform, copied out of chunk memory so the managed drain never touches ECS.
    /// </summary>
    /// <remarks>
    /// Blittable on purpose: the collection job writes these into a <c>NativeList</c>, and the
    /// main-thread loop reads the list back. Adding a managed field here would drag the whole
    /// collection step out of Burst.
    /// </remarks>
    public struct ViewTransformSample
    {
        public int ViewId;
        public float3 Position;
        public quaternion Rotation;
        public float Scale;
    }
}
