namespace Tests.Editor
{
    using NUnit.Framework;
    using Scripts.Gameplay;
    using Scripts.Gameplay.Presentation;
    using Shared.GameLogic.Components;
    using UnityEngine;

    /// <summary>
    /// ADR-28's axis convention, wire (x, y, z) -> Unity (x, z, y), and the aim/launch
    /// arithmetic built on it.
    /// </summary>
    public sealed class WireAxesTests
    {
        [Test]
        public void WireHeight_IsUnityUp_AndTheGroundPlaneIsUnityXZ()
        {
            Assert.That(WireAxes.ToUnity(1f, 2f, 3f), Is.EqualTo(new Vector3(1f, 3f, 2f)));
            Assert.That(WireAxes.ToUnity(new Vec3(-4f, 5f, 0.5f)), Is.EqualTo(new Vector3(-4f, 0.5f, 5f)));
            Assert.That(WireAxes.ToWire(new Vector3(1f, 3f, 2f)), Is.EqualTo(new Vec3(1f, 2f, 3f)));
        }

        [Test]
        public void RoundTrip_IsExact()
        {
            var wire = new Vec3(123.456f, -0.001f, 7.25f);
            Assert.That(WireAxes.ToWire(WireAxes.ToUnity(wire)), Is.EqualTo(wire));
        }

        [Test]
        public void GroundAim_HitsThePlaneAtTheGivenHeight_InWireSpace()
        {
            // Looking straight down from 10 units above Unity (3, _, 4), plane at Unity y = 2.
            var ray = new Ray(new Vector3(3f, 10f, 4f), Vector3.down);

            Assert.That(AimPoint.TryGroundAim(ray, 2f, out var aim), Is.True);
            Assert.That(aim.X, Is.EqualTo(3f).Within(1e-5f));
            Assert.That(aim.Y, Is.EqualTo(4f).Within(1e-5f), "Unity z is wire y");
            Assert.That(aim.Z, Is.EqualTo(2f).Within(1e-5f), "Unity y is wire z");
        }

        [Test]
        public void GroundAim_RefusesParallelAndBackwardRays()
        {
            Assert.That(AimPoint.TryGroundAim(new Ray(Vector3.up, Vector3.forward), 0f, out _), Is.False);
            Assert.That(AimPoint.TryGroundAim(new Ray(Vector3.up, Vector3.up), 0f, out _), Is.False);
        }

        [Test]
        public void Launch_IsOneUnitAboveFeetAndAim_AsTheServerDoes()
        {
            Assert.That(AimPoint.LaunchHeight, Is.EqualTo(1f));
            Assert.That(AimPoint.LaunchOrigin(new Vec3(1f, 2f, 0.5f)), Is.EqualTo(new Vec3(1f, 2f, 1.5f)));
            Assert.That(AimPoint.LaunchTarget(new Vec3(9f, 8f, 0f)), Is.EqualTo(new Vec3(9f, 8f, 1f)));
        }
    }
}
