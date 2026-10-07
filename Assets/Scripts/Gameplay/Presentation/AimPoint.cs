namespace Scripts.Gameplay.Presentation
{
    using Shared.GameLogic.Components;
    using UnityEngine;

    /// <summary>
    /// Aiming arithmetic for skillshots (ADR-29): the pointer ray onto the ground, and the
    /// launch geometry the server uses for a projectile.
    /// </summary>
    public static class AimPoint
    {
        /// <summary>
        /// Height above the caster's feet a projectile leaves from, and above the aim point it
        /// flies toward. The server launches from <c>feet + 1.0</c> toward
        /// <c>(aim_x, aim_y, aim_z + 1.0)</c>; predicting from anywhere else would make every
        /// handover jump by the difference.
        /// </summary>
        public const float LaunchHeight = 1.0f;

        /// <summary>
        /// Intersects <paramref name="ray"/> (Unity space) with the horizontal plane at Unity
        /// height <paramref name="planeHeight"/> and returns the hit as a WIRE point
        /// (<c>aim_x, aim_y, aim_z</c>). False when the ray is parallel to the plane or points
        /// away from it.
        /// </summary>
        public static bool TryGroundAim(Ray ray, float planeHeight, out Vec3 wireAim)
        {
            wireAim = default;
            var dy = ray.direction.y;
            if (Mathf.Abs(dy) < 1e-6f) return false;

            var t = (planeHeight - ray.origin.y) / dy;
            if (!(t > 0f) || float.IsInfinity(t)) return false;

            var hit = ray.origin + ray.direction * t;
            wireAim = WireAxes.ToWire(hit);
            return true;
        }

        /// <summary>The predicted launch point: the caster's feet raised by <see cref="LaunchHeight"/>.</summary>
        public static Vec3 LaunchOrigin(in Vec3 feet) => new Vec3(feet.X, feet.Y, feet.Z + LaunchHeight);

        /// <summary>The point a projectile flies toward: the aim point raised by <see cref="LaunchHeight"/>.</summary>
        public static Vec3 LaunchTarget(in Vec3 aim) => new Vec3(aim.X, aim.Y, aim.Z + LaunchHeight);
    }
}
