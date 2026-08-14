using Cuvara.DOTS.Netcode;
using NUnit.Framework;
using Unity.Mathematics;

namespace Cuvara.DOTS.Tests.Netcode
{
    /// <summary>
    /// The 2D → 3D placement, which is the one piece of the reference implementation that was a bare
    /// literal and is now a decision the caller makes.
    /// </summary>
    public sealed class SnapshotSpaceMappingTests
    {
        [Test]
        public void XZPlane_PutsServerYOnUnityZ_AndDoesNotLift()
        {
            // The lift the sample folded into this (0.5f) belongs to ViewConfig.PositionOffset —
            // it is per-art, not per-world. Asserting the zero is asserting that separation.
            var mapping = SnapshotSpaceMapping.XZPlane;

            Assert.AreEqual(new float3(3f, 0f, 7f), mapping.ToWorld(3f, 7f));
        }

        [Test]
        public void XYPlane_PutsServerYOnUnityY()
        {
            Assert.AreEqual(new float3(3f, 7f, 0f), SnapshotSpaceMapping.XYPlane.ToWorld(3f, 7f));
        }

        [Test]
        public void Origin_ShiftsTheWholePlane()
        {
            var mapping = new SnapshotSpaceMapping(
                new float3(1f, 0f, 0f),
                new float3(0f, 0f, 1f),
                new float3(100f, 2f, -50f));

            Assert.AreEqual(new float3(103f, 2f, -43f), mapping.ToWorld(3f, 7f));
        }

        [Test]
        public void Default_IsNotPopulated_SoTheAdapterCanSubstitute()
        {
            // A default mapping collapses every entity onto the origin, which reads as a networking
            // fault rather than a configuration one. IsPopulated is what lets DotsEntityView
            // substitute XZPlane instead of presenting a pile at the origin.
            Assert.IsFalse(default(SnapshotSpaceMapping).IsPopulated);
            Assert.IsTrue(SnapshotSpaceMapping.XZPlane.IsPopulated);
        }

        [Test]
        public void Basis_MayBeNonAxisAligned()
        {
            // Not a use case anyone has asked for; it is here because the type promises a basis
            // rather than a swizzle, and a promise nothing tests is a comment.
            var mapping = new SnapshotSpaceMapping(
                new float3(0f, 0f, -1f),
                new float3(1f, 0f, 0f),
                float3.zero);

            Assert.AreEqual(new float3(7f, 0f, -3f), mapping.ToWorld(3f, 7f));
        }
    }
}
