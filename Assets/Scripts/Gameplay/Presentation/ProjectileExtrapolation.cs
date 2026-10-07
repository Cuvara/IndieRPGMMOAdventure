namespace Scripts.Gameplay.Presentation
{
    using Shared.GameLogic.Components;

    /// <summary>
    /// Where to draw a replicated projectile between snapshots: its last reported position
    /// advanced along its replicated velocity (EntitySnapshot <c>vel_x/y/z</c>, ADR-29
    /// decision 1) by the time since that report.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Why extrapolate rather than interpolate.</b> A projectile flies in a straight line at
    /// constant velocity until it hits something, so the snapshot plus velocity predicts its
    /// path exactly up to the hit. Interpolating would draw it a full interpolation delay
    /// behind its real position — at 20 units/s, a couple of units — and the local player's own
    /// predicted projectile, which is drawn at the present, would jump backwards at handover.
    /// </para>
    /// <para>
    /// The extrapolation is capped (<see cref="MaxSeconds"/>): a projectile whose snapshots
    /// stopped (it hit, or left interest) must not keep flying on the screen.
    /// </para>
    /// </remarks>
    public static class ProjectileExtrapolation
    {
        /// <summary>Longest extrapolation, seconds. Several snapshot intervals at 15-60 Hz.</summary>
        public const float MaxSeconds = 0.25f;

        /// <summary>
        /// <paramref name="position"/> advanced by <paramref name="velocity"/> for
        /// <paramref name="ageSeconds"/>, clamped to [0, <paramref name="maxSeconds"/>].
        /// </summary>
        public static Vec3 Evaluate(in Vec3 position, in Vec3 velocity, float ageSeconds, float maxSeconds = MaxSeconds)
        {
            var t = ageSeconds;
            if (!(t > 0f)) t = 0f;
            if (t > maxSeconds) t = maxSeconds;
            return new Vec3(position.X + velocity.X * t, position.Y + velocity.Y * t, position.Z + velocity.Z * t);
        }
    }
}
