namespace TestLab
{
    using System;
    using System.Collections.Generic;

    /// <summary>One rendered discontinuity: an entity moved further in one frame than it can.</summary>
    public readonly struct RenderJump
    {
        public readonly string Id;
        public readonly bool IsLocal;
        public readonly float Step;
        public readonly float Allowed;
        public readonly double Dt;

        public RenderJump(string id, bool isLocal, float step, float allowed, double dt)
        {
            Id = id;
            IsLocal = isLocal;
            Step = step;
            Allowed = allowed;
            Dt = dt;
        }
    }

    /// <summary>
    /// Per-frame rendered-position continuity. An entity's rendered step in one frame is allowed
    /// up to <c>maxSpeed x dt x factor + slack</c>; more is a jump (a snap, a teleport, an
    /// extrapolation correction). Pure, so the threshold logic is testable in EditMode.
    /// </summary>
    /// <remarks>
    /// The first frame an entity is seen sets its position and is never a jump: spawning is not
    /// moving. An entity that disappears and comes back starts over for the same reason.
    /// </remarks>
    public sealed class RenderJumpDetector
    {
        private readonly Dictionary<string, (float X, float Y, int Frame)> last =
            new Dictionary<string, (float X, float Y, int Frame)>();

        private readonly float maxSpeed;
        private readonly float factor;
        private readonly float slack;

        public RenderJumpDetector(float maxSpeed, float factor, float slack)
        {
            this.maxSpeed = maxSpeed;
            this.factor = factor;
            this.slack = slack;
        }

        public long Jumps { get; private set; }

        public long LocalJumps { get; private set; }

        public float MaxStep { get; private set; }

        public float MaxStepSpeed { get; private set; }

        /// <summary>
        /// Feed one rendered position for <paramref name="frame"/>. Returns true and the jump when
        /// the step from the previous frame exceeds the allowance.
        /// </summary>
        public bool Observe(string id, bool isLocal, float x, float y, int frame, double dt, out RenderJump jump)
        {
            jump = default;
            bool continuous = this.last.TryGetValue(id, out var prev) && prev.Frame == frame - 1;
            this.last[id] = (x, y, frame);
            if (!continuous || dt <= 0)
            {
                return false;
            }

            float dx = x - prev.X;
            float dy = y - prev.Y;
            float step = (float)Math.Sqrt((dx * dx) + (dy * dy));
            if (step > this.MaxStep)
            {
                this.MaxStep = step;
            }

            float speed = (float)(step / dt);
            if (speed > this.MaxStepSpeed)
            {
                this.MaxStepSpeed = speed;
            }

            float allowed = (float)(this.maxSpeed * dt * this.factor) + this.slack;
            if (step <= allowed)
            {
                return false;
            }

            this.Jumps++;
            if (isLocal)
            {
                this.LocalJumps++;
            }

            jump = new RenderJump(id, isLocal, step, allowed, dt);
            return true;
        }
    }
}
