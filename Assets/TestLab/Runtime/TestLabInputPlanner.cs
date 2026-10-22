namespace TestLab
{
    using System;
    using System.Collections.Generic;
    using UnityEngine;

    /// <summary>An entity as the planner sees it: wire-space ground position and kind.</summary>
    public readonly struct PlannerEntity
    {
        public readonly string Id;
        public readonly float X;
        public readonly float Y;
        public readonly bool IsEnemy;

        public PlannerEntity(string id, float x, float y, bool isEnemy)
        {
            Id = id;
            X = x;
            Y = y;
            IsEnemy = isEnemy;
        }
    }

    /// <summary>What the scripted source hands the game for one frame.</summary>
    public struct PlannedInput
    {
        public float MoveX;
        public float MoveY;
        public bool Cast;
        public bool Jump;
        public bool HasAim;
        public Vector3 Aim;
        public string StepMode;
        public string CastTarget;
    }

    /// <summary>
    /// Turns a <see cref="TestLabPlan"/> plus what the client currently sees into the controls for
    /// one frame. Pure: no Unity runtime, no clock, no randomness - the same inputs give the same
    /// controls, which is what makes a scenario replayable and testable in EditMode.
    /// </summary>
    public sealed class TestLabInputPlanner
    {
        private readonly TestLabPlan plan;
        private readonly double[] nextActionAt;

        public TestLabInputPlanner(TestLabPlan plan)
        {
            this.plan = plan ?? throw new ArgumentNullException(nameof(plan));
            this.nextActionAt = new double[plan.actions.Length];
            for (int i = 0; i < plan.actions.Length; i++)
            {
                this.nextActionAt[i] = plan.actions[i].t0;
            }
        }

        /// <summary>The movement segment active at <paramref name="t"/>, or null (idle).</summary>
        public TestLabStep StepAt(double t)
        {
            foreach (var step in this.plan.steps)
            {
                if (t >= step.t0 && t < step.t1)
                {
                    return step;
                }
            }

            return null;
        }

        /// <summary>
        /// Controls for scenario time <paramref name="t"/>. <paramref name="clientPositions"/> is
        /// indexed by client index (NaN = unknown). Actions fire at most once per call, on their
        /// own interval grid, so a slow frame never fires a burst.
        /// </summary>
        public PlannedInput Plan(
            double t, float selfX, float selfY, bool selfKnown,
            IReadOnlyList<PlannerEntity> entities, IReadOnlyList<Vector2> clientPositions)
        {
            var result = new PlannedInput { StepMode = TestLabStep.Idle };
            if (t < 0)
            {
                return result;
            }

            var step = this.StepAt(t);
            if (step != null)
            {
                result.StepMode = step.mode;
                Vector2 dir = Vector2.zero;
                switch (step.mode)
                {
                    case TestLabStep.Move:
                        dir = new Vector2(step.x, step.y);
                        break;
                    case TestLabStep.Circle:
                    {
                        // Tangent of a circle traversed at `rate` rad/s from the segment start:
                        // continuous turning, the case that exposes reconciliation lag.
                        double angle = (t - step.t0) * step.rate;
                        dir = new Vector2((float)Math.Cos(angle), (float)Math.Sin(angle));
                        break;
                    }

                    case TestLabStep.TowardPoint:
                        if (selfKnown)
                        {
                            dir = Toward(selfX, selfY, step.px, step.py, step.stop_distance);
                        }

                        break;
                    case TestLabStep.TowardNearestEnemy:
                    case TestLabStep.AwayFromNearestEnemy:
                        if (selfKnown && TryNearestEnemy(selfX, selfY, entities, float.MaxValue, out var enemy))
                        {
                            dir = Toward(selfX, selfY, enemy.X, enemy.Y, step.stop_distance);
                            if (step.mode == TestLabStep.AwayFromNearestEnemy)
                            {
                                dir = Normalized(new Vector2(selfX - enemy.X, selfY - enemy.Y));
                            }
                        }

                        break;
                    case TestLabStep.TowardClient:
                        if (selfKnown && clientPositions != null &&
                            step.target_client >= 0 && step.target_client < clientPositions.Count)
                        {
                            var target = clientPositions[step.target_client];
                            if (!float.IsNaN(target.x))
                            {
                                dir = Toward(selfX, selfY, target.x, target.y, step.stop_distance);
                            }
                        }

                        break;
                }

                dir = Normalized(dir);
                result.MoveX = dir.x;
                result.MoveY = dir.y;
            }

            for (int i = 0; i < this.plan.actions.Length; i++)
            {
                var action = this.plan.actions[i];
                if (t < action.t0 || t >= action.t1 || t < this.nextActionAt[i])
                {
                    continue;
                }

                // Next slot on the action's own grid strictly after t: a frame that arrives late
                // fires once and skips the slots it missed rather than catching up.
                double interval = Math.Max(0.05, action.interval_s);
                double slots = Math.Floor((t - action.t0) / interval) + 1;
                this.nextActionAt[i] = action.t0 + (slots * interval);

                switch (action.kind)
                {
                    case TestLabAction.Jump:
                        result.Jump = true;
                        break;
                    case TestLabAction.CastForward:
                        result.Cast = true;
                        break;
                    case TestLabAction.CastNearestEnemy:
                        if (selfKnown && TryNearestEnemy(selfX, selfY, entities, action.range, out var target))
                        {
                            result.Cast = true;
                            result.HasAim = true;
                            result.Aim = new Vector3(target.X, target.Y, 0f);
                            result.CastTarget = target.Id;
                        }

                        break;
                }
            }

            return result;
        }

        public static bool TryNearestEnemy(
            float x, float y, IReadOnlyList<PlannerEntity> entities, float range, out PlannerEntity nearest)
        {
            nearest = default;
            if (entities == null)
            {
                return false;
            }

            float best = range * range;
            bool found = false;
            for (int i = 0; i < entities.Count; i++)
            {
                var e = entities[i];
                if (!e.IsEnemy)
                {
                    continue;
                }

                float dx = e.X - x;
                float dy = e.Y - y;
                float d = (dx * dx) + (dy * dy);
                if (d <= best)
                {
                    best = d;
                    nearest = e;
                    found = true;
                }
            }

            return found;
        }

        private static Vector2 Toward(float x, float y, float tx, float ty, float stopDistance)
        {
            var delta = new Vector2(tx - x, ty - y);
            return delta.sqrMagnitude <= stopDistance * stopDistance ? Vector2.zero : delta;
        }

        private static Vector2 Normalized(Vector2 v)
        {
            float m = v.magnitude;
            return m > 1e-6f ? v / m : Vector2.zero;
        }
    }
}
