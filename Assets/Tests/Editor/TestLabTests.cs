using System.Collections.Generic;
using NUnit.Framework;
using TestLab;
using UnityEngine;

namespace Tests.Editor
{
    /// <summary>
    /// The testlab's pure parts: plan parsing, the input planner and the rendered-jump detector.
    /// The runner itself needs a live session and is exercised by the testlab's rendered runs.
    /// </summary>
    public sealed class TestLabTests
    {
        private static TestLabPlan Plan(string body) =>
            TestLabPlan.Parse("{\"schema\":\"rpg.testlab.plan.v1\",\"scenario_id\":\"t\"" + body + "}");

        [Test]
        public void Plan_WithAnotherSchema_IsRefused()
        {
            Assert.Throws<System.FormatException>(() => TestLabPlan.Parse("{\"schema\":\"x\"}"));
            Assert.Throws<System.FormatException>(() => TestLabPlan.Parse(""));
        }

        [Test]
        public void Plan_WithAnUnknownMode_IsRefused()
        {
            Assert.Throws<System.FormatException>(() =>
                Plan(",\"steps\":[{\"t0\":0,\"t1\":1,\"mode\":\"teleport\"}]"));
            Assert.Throws<System.FormatException>(() =>
                Plan(",\"actions\":[{\"t0\":0,\"t1\":1,\"kind\":\"dance\"}]"));
        }

        [Test]
        public void Planner_BeforeStartAndOutsideSteps_IsIdle()
        {
            var planner = new TestLabInputPlanner(Plan(",\"steps\":[{\"t0\":2,\"t1\":4,\"mode\":\"move\",\"x\":1,\"y\":0}]"));
            var none = new List<PlannerEntity>();
            Assert.That(planner.Plan(-1, 0, 0, true, none, null).MoveX, Is.EqualTo(0f));
            Assert.That(planner.Plan(1, 0, 0, true, none, null).MoveX, Is.EqualTo(0f));
            Assert.That(planner.Plan(3, 0, 0, true, none, null).MoveX, Is.EqualTo(1f));
            Assert.That(planner.Plan(4, 0, 0, true, none, null).MoveX, Is.EqualTo(0f));
        }

        [Test]
        public void Planner_MoveIsNormalised_DiagonalIncluded()
        {
            var planner = new TestLabInputPlanner(Plan(",\"steps\":[{\"t0\":0,\"t1\":9,\"mode\":\"move\",\"x\":3,\"y\":3}]"));
            var input = planner.Plan(1, 0, 0, true, new List<PlannerEntity>(), null);
            Assert.That(new Vector2(input.MoveX, input.MoveY).magnitude, Is.EqualTo(1f).Within(1e-5));
            Assert.That(input.MoveX, Is.EqualTo(input.MoveY).Within(1e-6));
        }

        [Test]
        public void Planner_TowardAndAwayFromNearestEnemy_IgnoresPlayers()
        {
            var entities = new List<PlannerEntity>
            {
                new PlannerEntity("p2", 1, 0, false),
                new PlannerEntity("m1", 0, 5, true),
                new PlannerEntity("m2", 0, -9, true),
            };
            var toward = new TestLabInputPlanner(Plan(",\"steps\":[{\"t0\":0,\"t1\":9,\"mode\":\"toward_nearest_enemy\"}]"));
            var away = new TestLabInputPlanner(Plan(",\"steps\":[{\"t0\":0,\"t1\":9,\"mode\":\"away_from_nearest_enemy\"}]"));

            Assert.That(toward.Plan(1, 0, 0, true, entities, null).MoveY, Is.EqualTo(1f).Within(1e-6));
            Assert.That(away.Plan(1, 0, 0, true, entities, null).MoveY, Is.EqualTo(-1f).Within(1e-6));
        }

        [Test]
        public void Planner_TowardPoint_StopsWithinStopDistance()
        {
            var planner = new TestLabInputPlanner(Plan(
                ",\"steps\":[{\"t0\":0,\"t1\":9,\"mode\":\"toward_point\",\"px\":10,\"py\":0,\"stop_distance\":1}]"));
            var none = new List<PlannerEntity>();
            Assert.That(planner.Plan(1, 0, 0, true, none, null).MoveX, Is.EqualTo(1f).Within(1e-6));
            Assert.That(planner.Plan(1, 9.5f, 0, true, none, null).MoveX, Is.EqualTo(0f));
        }

        [Test]
        public void Planner_Cast_FiresOncePerInterval_AimsAtTheNearestEnemyInRange()
        {
            var planner = new TestLabInputPlanner(Plan(
                ",\"actions\":[{\"t0\":0,\"t1\":10,\"kind\":\"cast_nearest_enemy\",\"interval_s\":1,\"range\":6}]"));
            var entities = new List<PlannerEntity> { new PlannerEntity("m1", 3, 4, true), new PlannerEntity("m2", 20, 0, true) };

            var first = planner.Plan(0.1, 0, 0, true, entities, null);
            Assert.That(first.Cast, Is.True);
            Assert.That(first.CastTarget, Is.EqualTo("m1"));
            Assert.That(first.Aim.x, Is.EqualTo(3f));
            Assert.That(planner.Plan(0.5, 0, 0, true, entities, null).Cast, Is.False, "same interval slot");

            // A late frame fires once and skips the slots it missed.
            Assert.That(planner.Plan(3.7, 0, 0, true, entities, null).Cast, Is.True);
            Assert.That(planner.Plan(3.9, 0, 0, true, entities, null).Cast, Is.False);
            Assert.That(planner.Plan(4.0, 0, 0, true, entities, null).Cast, Is.True);
        }

        [Test]
        public void Planner_CastWithNoEnemyInRange_DoesNotFire()
        {
            var planner = new TestLabInputPlanner(Plan(
                ",\"actions\":[{\"t0\":0,\"t1\":10,\"kind\":\"cast_nearest_enemy\",\"interval_s\":1,\"range\":2}]"));
            var entities = new List<PlannerEntity> { new PlannerEntity("m1", 3, 4, true) };
            Assert.That(planner.Plan(0.1, 0, 0, true, entities, null).Cast, Is.False);
        }

        [Test]
        public void JumpDetector_SpawnIsNotAJump_AStepBeyondTheAllowanceIs()
        {
            var detector = new RenderJumpDetector(maxSpeed: 5f, factor: 3f, slack: 0.25f);
            double dt = 1.0 / 60.0; // allowance = 5 * dt * 3 + 0.25 = 0.5

            Assert.That(detector.Observe("m1", false, 100, 100, 1, dt, out _), Is.False, "first sighting");
            Assert.That(detector.Observe("m1", false, 100.4f, 100, 2, dt, out _), Is.False);
            Assert.That(detector.Observe("m1", false, 101.4f, 100, 3, dt, out var jump), Is.True);
            Assert.That(jump.Step, Is.EqualTo(1f).Within(1e-4));
            Assert.That(detector.Jumps, Is.EqualTo(1));

            // A gap in frames (entity not seen) restarts continuity.
            Assert.That(detector.Observe("m1", false, 200, 100, 9, dt, out _), Is.False);
        }

        [Test]
        public void Control_HalfWrittenFile_IsNotAControl()
        {
            Assert.That(TestLabControl.TryParse("{\"start_at_unix_ms\":12", out _), Is.False);
            Assert.That(TestLabControl.TryParse("{\"start_at_unix_ms\":12}", out var c), Is.True);
            Assert.That(c.start_at_unix_ms, Is.EqualTo(12));
        }

        [Test]
        public void FrameStats_NearestRankPercentiles()
        {
            var stats = new FrameStats();
            for (int i = 1; i <= 100; i++)
            {
                stats.Add(i);
            }

            Assert.That(stats.Percentile(50), Is.EqualTo(50));
            Assert.That(stats.Percentile(99), Is.EqualTo(99));
            Assert.That(stats.LongFrames, Is.EqualTo(100 - 33));
        }
    }
}
