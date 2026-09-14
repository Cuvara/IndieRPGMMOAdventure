namespace Tests.Editor
{
    using System.Collections.Generic;
    using NUnit.Framework;
    using Scripts.Benchmark;

    /// <summary>
    /// The aggregation rules on synthetic samples: percentile interpolation, warm-up
    /// exclusion, phase attribution with the settle window, GC spike/collection counting,
    /// and the truncation-safe count parameter. Pure C# — no device, no player loop.
    /// </summary>
    public sealed class BenchmarkAggregationTests
    {
        private static readonly BenchmarkPhase[] TwoPhases =
        {
            new BenchmarkPhase("a", 10, 5f),
            new BenchmarkPhase("b", 20, 5f),
        };

        private static FrameSample Sample(float time, float ms, int phase, long gcBytes = 0, int collections = 0)
        {
            return new FrameSample
            {
                TimeSeconds = time,
                FrameMs = ms,
                GcAllocatedBytes = gcBytes,
                GcCollectionsCumulative = collections,
                PhaseIndex = phase,
            };
        }

        [Test]
        public void Percentile_InterpolatesLinearly()
        {
            var sorted = new List<float> { 1f, 2f, 3f, 4f };
            Assert.That(BenchmarkAggregation.Percentile(sorted, 0.5f), Is.EqualTo(2.5f).Within(1e-5f));
            Assert.That(BenchmarkAggregation.Percentile(sorted, 0f), Is.EqualTo(1f));
            Assert.That(BenchmarkAggregation.Percentile(sorted, 1f), Is.EqualTo(4f));
            // rank = 0.95 * 3 = 2.85 -> between 3 and 4.
            Assert.That(BenchmarkAggregation.Percentile(sorted, 0.95f), Is.EqualTo(3.85f).Within(1e-5f));
        }

        [Test]
        public void Percentile_SingleElementAndEmpty()
        {
            Assert.That(BenchmarkAggregation.Percentile(new List<float> { 7f }, 0.99f), Is.EqualTo(7f));
            Assert.That(BenchmarkAggregation.Percentile(new List<float>(), 0.5f), Is.EqualTo(0f));
        }

        [Test]
        public void Aggregate_ExcludesWarmupFromEverything()
        {
            var samples = new[]
            {
                Sample(0.5f, 100f, -1), // warm-up: huge frame, must not appear anywhere
                Sample(1.5f, 10f, 0),
                Sample(2.5f, 10f, 0),
                Sample(3.5f, 10f, 0),
            };

            BenchmarkAggregation.Aggregate(
                samples, samples.Length, TwoPhases, new[] { 1f, 6f }, settleSeconds: 0f,
                out var overall, out _);

            Assert.That(overall.FrameCount, Is.EqualTo(3));
            Assert.That(overall.MaxMs, Is.EqualTo(10f));
            Assert.That(overall.MeanMs, Is.EqualTo(10f).Within(1e-5f));
        }

        [Test]
        public void Aggregate_AttributesSamplesToTheirPhase()
        {
            var samples = new[]
            {
                Sample(1f, 10f, 0),
                Sample(2f, 12f, 0),
                Sample(6f, 20f, 1),
                Sample(7f, 22f, 1),
            };

            BenchmarkAggregation.Aggregate(
                samples, samples.Length, TwoPhases, new[] { 1f, 6f }, settleSeconds: 0f,
                out var overall, out var phases);

            Assert.That(overall.FrameCount, Is.EqualTo(4));
            Assert.That(phases[0].Aggregates.FrameCount, Is.EqualTo(2));
            Assert.That(phases[0].Aggregates.MaxMs, Is.EqualTo(12f));
            Assert.That(phases[1].Aggregates.FrameCount, Is.EqualTo(2));
            Assert.That(phases[1].Aggregates.MeanMs, Is.EqualTo(21f).Within(1e-5f));
            Assert.That(phases[0].Label, Is.EqualTo("a"));
            Assert.That(phases[1].EntityCount, Is.EqualTo(20));
        }

        [Test]
        public void Aggregate_SettleWindowExcludesPhaseStartButNotOverall()
        {
            var samples = new[]
            {
                Sample(6.5f, 50f, 1), // inside phase 1's settle window: spawn burst
                Sample(9f, 20f, 1),
                Sample(10f, 20f, 1),
            };

            BenchmarkAggregation.Aggregate(
                samples, samples.Length, TwoPhases, new[] { 1f, 6f }, settleSeconds: 2f,
                out var overall, out var phases);

            // The burst frame stays in the run's true total ...
            Assert.That(overall.FrameCount, Is.EqualTo(3));
            Assert.That(overall.MaxMs, Is.EqualTo(50f));
            // ... but not in the phase's steady state.
            Assert.That(phases[1].Aggregates.FrameCount, Is.EqualTo(2));
            Assert.That(phases[1].Aggregates.MaxMs, Is.EqualTo(20f));
        }

        [Test]
        public void Aggregate_CountsGcCollectionsAndSpikeFrames()
        {
            var samples = new[]
            {
                Sample(1f, 10f, 0, collections: 0),
                Sample(2f, 10f, 0, collections: 2), // two collections in one frame: one spike
                Sample(3f, 10f, 0, collections: 2),
                Sample(4f, 10f, 0, collections: 3), // one more: second spike
            };

            BenchmarkAggregation.Aggregate(
                samples, samples.Length, TwoPhases, new[] { 1f, 6f }, settleSeconds: 0f,
                out var overall, out _);

            Assert.That(overall.GcCollections, Is.EqualTo(3));
            Assert.That(overall.GcSpikeFrames, Is.EqualTo(2));
        }

        [Test]
        public void Aggregate_SumsAndMediansGcBytes()
        {
            var samples = new[]
            {
                Sample(1f, 10f, 0, gcBytes: 0),
                Sample(2f, 10f, 0, gcBytes: 100),
                Sample(3f, 10f, 0, gcBytes: 0),
                Sample(4f, 10f, 0, gcBytes: 0),
                Sample(5f, 10f, 0, gcBytes: 4000), // one loading hitch
            };

            BenchmarkAggregation.Aggregate(
                samples, samples.Length, TwoPhases, new[] { 1f, 6f }, settleSeconds: 0f,
                out var overall, out _);

            Assert.That(overall.GcAllocatedTotalBytes, Is.EqualTo(4100L));
            // Median is 0 — the steady state, unmoved by the hitch. That is the point.
            Assert.That(overall.GcAllocatedPerFrameMedianBytes, Is.EqualTo(0L));
        }

        [Test]
        public void Aggregate_RespectsCountOverBufferLength()
        {
            var samples = new[]
            {
                Sample(1f, 10f, 0),
                Sample(2f, 10f, 0),
                Sample(0f, 999f, 0), // stale slot past the logical count
            };

            BenchmarkAggregation.Aggregate(
                samples, 2, TwoPhases, new[] { 1f, 6f }, settleSeconds: 0f,
                out var overall, out _);

            Assert.That(overall.FrameCount, Is.EqualTo(2));
            Assert.That(overall.MaxMs, Is.EqualTo(10f));
        }

        [Test]
        public void Aggregate_EmptyRangeYieldsZeroedAggregates()
        {
            var samples = new[] { Sample(0.5f, 10f, -1) };

            BenchmarkAggregation.Aggregate(
                samples, samples.Length, TwoPhases, new[] { 1f, 6f }, settleSeconds: 0f,
                out var overall, out var phases);

            Assert.That(overall.FrameCount, Is.EqualTo(0));
            Assert.That(overall.MeanMs, Is.EqualTo(0f));
            Assert.That(phases[0].Aggregates.FrameCount, Is.EqualTo(0));
        }

        [Test]
        public void Aggregate_AverageFpsUsesWallClockSpan()
        {
            // 11 frames across exactly 1 second -> 10 fps (fenceposts, not frames/second-count).
            var samples = new FrameSample[11];
            for (var i = 0; i < samples.Length; i++)
            {
                samples[i] = Sample(1f + i * 0.1f, 100f, 0);
            }

            BenchmarkAggregation.Aggregate(
                samples, samples.Length, TwoPhases, new[] { 1f, 6f }, settleSeconds: 0f,
                out var overall, out _);

            Assert.That(overall.AverageFps, Is.EqualTo(10f).Within(1e-3f));
        }
    }
}
