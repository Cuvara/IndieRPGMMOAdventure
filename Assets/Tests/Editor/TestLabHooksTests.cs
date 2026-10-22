using System;
using System.Collections.Generic;
using Cuvara.Netcode.Snapshot;
using NUnit.Framework;
using TestLab;
using UnityEngine;

namespace Tests.Editor
{
    /// <summary>
    /// TestLab cost bounds and the T14 hooks: the frame statistics and the jump detector must not
    /// grow with the run's length (a one-hour soak was contaminated by exactly that), and the new
    /// plan actions and entity lifecycle events behave as docs/TESTLAB.md says.
    /// </summary>
    public sealed class TestLabHooksTests
    {
        private static TestLabPlan Plan(string body) =>
            TestLabPlan.Parse("{\"schema\":\"rpg.testlab.plan.v1\",\"scenario_id\":\"t\"" + body + "}");

        // Deterministic frame times, 2..60 ms with occasional long frames; no System.Random state.
        private static double FrameMs(long i) => 2.0 + ((i * 7919L) % 5800L) / 100.0 + (i % 997 == 0 ? 400.0 : 0.0);

        [Test]
        public void FrameStats_AMillionFrames_RetainsAndAllocatesNothingMore()
        {
            var stats = new FrameStats();
            for (long i = 0; i < 1_000; i++)
            {
                stats.Add(FrameMs(i));
            }

            stats.RecentPercentile(95);
            stats.Percentile(95);
            int retainedEarly = stats.RetainedValues;

            // Measured with the Unity Test Framework's GC.Alloc recorder: on this Mono runtime
            // GC.GetAllocatedBytesForCurrentThread always returns 0, so it cannot see anything.
            // Control: the same constraint does see a 4 KiB allocation, so "allocates nothing"
            // below is a measurement and not a blind probe.
            Assert.That(() => { GC.KeepAlive(new byte[4096]); },
                UnityEngine.TestTools.Constraints.Is.AllocatingGCMemory(), "allocation probe does not work here");

            Assert.That(() =>
            {
                for (long i = 1_000; i < 1_000_000; i++)
                {
                    stats.Add(FrameMs(i));
                    if (i % 1_000 == 0)
                    {
                        // The overlay's once-a-second statistic, ~1000 times over the run.
                        stats.RecentPercentile(95);
                    }
                }

                stats.Percentile(50);
                stats.Percentile(95);
                stats.Percentile(99);
            }, UnityEngine.TestTools.Constraints.ConstraintExtensions.AllocatingGCMemory(UnityEngine.TestTools.Constraints.Is.Not),
                "Add / RecentPercentile / Percentile must not allocate");

            Assert.That(stats.Count, Is.EqualTo(1_000_000));
            Assert.That(stats.RetainedValues, Is.EqualTo(retainedEarly), "memory must not grow with frames");
        }

        [Test]
        public void FrameStats_RecentPercentile_IsExactOverTheWindow()
        {
            var stats = new FrameStats();
            var all = new List<double>();
            for (long i = 0; i < 5_000; i++)
            {
                double ms = FrameMs(i);
                stats.Add(ms);
                all.Add(ms);
            }

            var window = all.GetRange(all.Count - FrameStats.WindowFrames, FrameStats.WindowFrames);
            window.Sort();
            int rank = (int)Math.Ceiling(0.95 * window.Count);
            Assert.That(stats.RecentPercentile(95), Is.EqualTo(window[rank - 1]));
        }

        [Test]
        public void FrameStats_RunPercentile_IsAtMostOneBucketAboveExact_MeanAndMaxExact()
        {
            var stats = new FrameStats();
            var all = new List<double>();
            double sum = 0;
            for (long i = 0; i < 100_000; i++)
            {
                double ms = FrameMs(i);
                stats.Add(ms);
                all.Add(ms);
                sum += ms;
            }

            all.Sort();
            foreach (double p in new[] { 50.0, 95.0, 99.0 })
            {
                double exact = all[(int)Math.Ceiling(p / 100.0 * all.Count) - 1];
                double got = stats.Percentile(p);
                Assert.That(got, Is.GreaterThanOrEqualTo(exact), $"p{p} below exact");
                Assert.That(got - exact, Is.LessThan(FrameStats.BucketMs), $"p{p} more than one bucket above exact");
            }

            Assert.That(stats.Max(), Is.EqualTo(all[all.Count - 1]));
            Assert.That(stats.Mean(), Is.EqualTo(sum / all.Count).Within(1e-9));
        }

        [Test]
        public void JumpDetector_HoldsOnlyTwoFramesOfEntities()
        {
            var detector = new RenderJumpDetector(maxSpeed: 5f, factor: 3f, slack: 0.25f);
            // 100k distinct ids, a few per frame: the old detector kept every id forever.
            for (int frame = 1; frame <= 50_000; frame++)
            {
                detector.Observe("mob-" + (frame * 2), false, 0, 0, frame, 1.0 / 60.0, out _);
                detector.Observe("mob-" + ((frame * 2) + 1), false, 0, 0, frame, 1.0 / 60.0, out _);
            }

            Assert.That(detector.Tracked, Is.LessThanOrEqualTo(4));
        }

        [Test]
        public void Plan_AcceptsTheHookActions()
        {
            var plan = Plan(",\"actions\":[" +
                            "{\"t0\":1,\"kind\":\"stall\",\"ms\":300}," +
                            "{\"t0\":2,\"kind\":\"quit\"}," +
                            "{\"t0\":3,\"kind\":\"reconnect\"}," +
                            "{\"t0\":0,\"t1\":5,\"kind\":\"cast_at_point\",\"x\":40,\"y\":-3}," +
                            "{\"t0\":0,\"t1\":5,\"kind\":\"cast_spam\",\"interval_ms\":20}]");
            Assert.That(plan.actions[0].ms, Is.EqualTo(300));
            Assert.That(TestLabAction.IsOneShot(plan.actions[1].kind), Is.True);
            Assert.That(plan.actions[3].x, Is.EqualTo(40f));
            Assert.That(plan.actions[4].interval_ms, Is.EqualTo(20f));
        }

        [Test]
        public void Planner_OneShotActions_NeverBecomeInput()
        {
            var planner = new TestLabInputPlanner(Plan(",\"actions\":[{\"t0\":0,\"t1\":10,\"kind\":\"stall\"},{\"t0\":0,\"t1\":10,\"kind\":\"quit\"}]"));
            var input = planner.Plan(1, 0, 0, true, new List<PlannerEntity>(), null);
            Assert.That(input.Cast || input.Jump, Is.False);
        }

        [Test]
        public void Planner_CastAtPoint_AimsAtThePoint_EvenOutOfRange()
        {
            var planner = new TestLabInputPlanner(Plan(",\"actions\":[{\"t0\":0,\"t1\":10,\"kind\":\"cast_at_point\",\"x\":500,\"y\":-7,\"interval_s\":1}]"));
            var input = planner.Plan(0.5, 0, 0, true, new List<PlannerEntity>(), null);
            Assert.That(input.Cast, Is.True);
            Assert.That(input.HasAim, Is.True);
            Assert.That(input.Aim, Is.EqualTo(new Vector3(500, -7, 0)));
            Assert.That(input.CastKind, Is.EqualTo("cast_at_point"));
        }

        [Test]
        public void Planner_CastSpam_FiresOnItsMillisecondGrid_AtMostOncePerFrame()
        {
            var planner = new TestLabInputPlanner(Plan(",\"actions\":[{\"t0\":0,\"t1\":10,\"kind\":\"cast_spam\",\"interval_ms\":20}]"));
            var none = new List<PlannerEntity>();
            int casts = 0;
            // 1 s of frames at 100 fps, offset half a frame off the grid: a 20 ms grid fires every
            // other frame.
            for (int f = 0; f < 100; f++)
            {
                if (planner.Plan((f * 0.01) + 0.005, 0, 0, true, none, null).Cast)
                {
                    casts++;
                }
            }

            Assert.That(casts, Is.EqualTo(50));
        }

        [Test]
        public void Lifecycle_AddedOnFirstRender_RemovedByListAndByKeyframe()
        {
            var events = new List<string>();
            var lifecycle = new EntityLifecycle((kind, fields) => events.Add(kind + " {" + fields + "}"));

            lifecycle.OnSnapshot(100, true, Ents(("m1", "mob", 1f, 2f), ("p1", "player", 0f, 0f)), null);
            Assert.That(events, Is.Empty, "added is written at the first render, not at arrival");

            lifecycle.OnRendered("m1", 1.5f, 2f, 7);
            lifecycle.OnRendered("m1", 1.6f, 2f, 8);
            Assert.That(events, Has.Count.EqualTo(1));
            Assert.That(events[0], Is.EqualTo(
                "entity_added {\"id\":\"m1\",\"type\":\"mob\",\"tick\":100,\"x\":1,\"y\":2,\"rx\":1.5,\"ry\":2,\"frame\":7}"));

            // p1 removed before it was ever drawn: added (rendered null) then removed.
            lifecycle.OnSnapshot(101, false, Ents(), new List<string> { "p1" });
            Assert.That(events[1], Does.StartWith("entity_added {\"id\":\"p1\"").And.Contain("\"rx\":null"));
            Assert.That(events[2], Does.StartWith("entity_removed {\"id\":\"p1\"").And.Contain("\"reason\":\"removed\""));

            // A keyframe without m1 removes it.
            lifecycle.OnSnapshot(150, true, Ents(("m2", "mob", 0f, 0f)), null);
            Assert.That(events[3], Does.StartWith("entity_removed {\"id\":\"m1\"").And.Contain("\"tick\":150").And.Contain("\"reason\":\"keyframe\""));
            Assert.That(lifecycle.Live, Is.EqualTo(1));
            Assert.That(lifecycle.Reset(), Is.EqualTo(1));
            Assert.That(lifecycle.Live, Is.EqualTo(0));
        }

        private static List<ResolvedEntity> Ents(params (string Id, string Type, float X, float Y)[] items)
        {
            var list = new List<ResolvedEntity>();
            foreach (var e in items)
            {
                list.Add(new ResolvedEntity(e.Id, e.Type, e.X, e.Y, 100, 100));
            }

            return list;
        }
    }
}
