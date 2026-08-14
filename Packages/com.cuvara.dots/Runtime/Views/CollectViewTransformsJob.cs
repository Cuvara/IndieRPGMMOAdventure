using Cuvara.DOTS.Configuration;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

namespace Cuvara.DOTS.Views
{
    /// <summary>Reads chunk memory into blittable samples. Order of samples is not meaningful.</summary>
    /// <remarks>
    /// <para>
    /// Reads <see cref="LocalToWorld"/>, not <c>LocalTransform</c>. <c>LocalTransform</c> is relative
    /// to the entity's parent, so a view driven from it would sit at local coordinates the moment
    /// anything is parented — correct-looking for root entities and wrong for everything else.
    /// <see cref="LocalToWorld"/> is the world matrix <c>TransformSystemGroup</c> computed earlier in
    /// the same frame, and it is right in both cases.
    /// </para>
    /// <para>
    /// Scale is recovered as the length of the matrix's first basis vector, which is the uniform
    /// scale the views use. A non-uniform or sheared transform would not survive that, and this
    /// deliberately does not pretend otherwise: <see cref="ViewTransformSample"/> carries one float.
    /// </para>
    /// <para>
    /// The <see cref="ViewTransformOffset"/> is composed in here rather than applied on the managed
    /// side: this is the Bursted half, and the managed drain should stay a flat write.
    /// <b>Re-applying it every frame is required, not an optimisation.</b> This sync overwrites the
    /// GameObject's position, rotation and scale on every frame, so an offset applied once at spawn
    /// survives exactly one frame and is then silently erased — which a consumer reads as "offsets
    /// don't work", not as a lifetime bug.
    /// </para>
    /// </remarks>
    [BurstCompile]
    internal partial struct CollectViewTransformsJob : IJobEntity
    {
        public NativeList<ViewTransformSample>.ParallelWriter Samples;

        private void Execute(in EntityViewLink link, in LocalToWorld transform, in ViewTransformOffset offset)
        {
            // Position offset is rotated by the entity's rotation, so it is a local offset ("half a
            // metre in front of the entity") rather than a world one, which is what an art offset
            // authored against a prefab means.
            var rotation = transform.Rotation;

            Samples.AddNoResize(new ViewTransformSample
            {
                ViewId = link.ViewId,
                Position = transform.Position + math.mul(rotation, offset.Position),
                Rotation = math.mul(rotation, offset.Rotation),
                Scale = math.length(transform.Value.c0.xyz) * offset.Scale,
            });
        }
    }
}
