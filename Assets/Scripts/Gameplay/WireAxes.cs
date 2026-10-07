namespace Scripts.Gameplay
{
    using Shared.GameLogic.Components;
    using UnityEngine;

    /// <summary>
    /// The one conversion between the server's axes and Unity's (ADR-28 decision 1): wire
    /// <c>x</c>/<c>y</c> are the ground plane and <c>z</c> is height, so wire <c>(x, y, z)</c>
    /// is Unity <c>(x, z, y)</c>.
    /// </summary>
    /// <remarks>
    /// Every place that turns a snapshot, a predicted position, a map file or an aim point into a
    /// Unity position goes through here, so the swap is spelled once. The mapping is its own
    /// inverse in shape (swap the last two components), which is why both directions are the
    /// same three-line function and why a round trip is exact, bit for bit.
    /// </remarks>
    public static class WireAxes
    {
        /// <summary>Wire <c>(x, y, z)</c> to Unity <c>(x, z, y)</c>.</summary>
        public static Vector3 ToUnity(float x, float y, float z) => new Vector3(x, z, y);

        /// <inheritdoc cref="ToUnity(float, float, float)"/>
        public static Vector3 ToUnity(in Vec3 wire) => new Vector3(wire.X, wire.Z, wire.Y);

        /// <summary>Unity <c>(x, y, z)</c> to wire <c>(x, z, y)</c>: Unity's up axis becomes wire height.</summary>
        public static Vec3 ToWire(Vector3 unity) => new Vec3(unity.x, unity.z, unity.y);
    }
}
