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
    /// </remarks>
    [BurstCompile]
    internal partial struct CollectViewTransformsJob : IJobEntity
    {
        public NativeList<ViewTransformSample>.ParallelWriter Samples;

        private void Execute(in EntityViewLink link, in LocalToWorld transform)
        {
            Samples.AddNoResize(new ViewTransformSample
            {
                ViewId = link.ViewId,
                Position = transform.Position,
                Rotation = transform.Rotation,
                Scale = math.length(transform.Value.c0.xyz),
            });
        }
    }
}
