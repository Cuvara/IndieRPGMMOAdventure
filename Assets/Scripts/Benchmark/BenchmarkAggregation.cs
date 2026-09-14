namespace Scripts.Benchmark
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// Reduces a buffer of <see cref="FrameSample"/>s to <see cref="BenchmarkAggregates"/> —
    /// pure static math over plain arrays, no Unity API, so every rule here is testable on
    /// any runner without a device or an Editor.
    /// </summary>
    /// <remarks>
    /// <para>The exclusion rules, in one place:</para>
    /// <list type="bullet">
    /// <item><b>Warm-up</b>: samples with <c>PhaseIndex &lt; 0</c> (recorded inside the
    /// warm-up window) enter NO aggregate.</item>
    /// <item><b>Overall</b>: every post-warm-up sample, settle windows included — the run's
    /// true total, spikes and all.</item>
    /// <item><b>Per phase</b>: that phase's samples minus the first
    /// <c>settleSeconds</c> after its boundary, so a ramp step's spawn burst does not land
    /// in the steady-state numbers of the phase it starts.</item>
    /// </list>
    /// <para>Allocation is fine here: aggregation runs once, after sampling has stopped.</para>
    /// </remarks>
    public static class BenchmarkAggregation
    {
        /// <summary>
        /// Aggregates <paramref name="count"/> samples from <paramref name="samples"/>.
        /// <paramref name="phaseStartTimes"/> holds each phase's boundary time (seconds since
        /// recording start), parallel to <paramref name="phases"/>.
        /// </summary>
        public static void Aggregate(
            FrameSample[] samples,
            int count,
            BenchmarkPhase[] phases,
            float[] phaseStartTimes,
            float settleSeconds,
            out BenchmarkAggregates overall,
            out BenchmarkPhaseResult[] phaseResults)
        {
            if (samples == null) throw new ArgumentNullException(nameof(samples));
            if (phases == null) throw new ArgumentNullException(nameof(phases));
            if (phaseStartTimes == null) throw new ArgumentNullException(nameof(phaseStartTimes));
            if (phases.Length != phaseStartTimes.Length)
            {
                throw new ArgumentException(
                    $"phases ({phases.Length}) and phaseStartTimes ({phaseStartTimes.Length}) must be parallel.");
            }

            count = Math.Min(count, samples.Length);

            overall = AggregateWhere(samples, count, static (in FrameSample s, int _, float __) => s.PhaseIndex >= 0, 0, 0f);

            phaseResults = new BenchmarkPhaseResult[phases.Length];
            for (var p = 0; p < phases.Length; p++)
            {
                var cutoff = phaseStartTimes[p] + settleSeconds;
                phaseResults[p] = new BenchmarkPhaseResult
                {
                    Label = phases[p].Label,
                    EntityCount = phases[p].EntityCount,
                    StartTimeSeconds = phaseStartTimes[p],
                    ConfiguredSeconds = phases[p].Seconds,
                    Aggregates = AggregateWhere(
                        samples,
                        count,
                        static (in FrameSample s, int phase, float cut) => s.PhaseIndex == phase && s.TimeSeconds >= cut,
                        p,
                        cutoff),
                };
            }
        }

        /// <summary>
        /// Linear-interpolated percentile of an ascending-sorted list;
        /// <paramref name="quantile"/> in 0..1. The 0.5 quantile of {1,2,3,4} is 2.5, not 2 —
        /// the convention NumPy's default and most profiling tools share, so these numbers
        /// compare against external tooling without a footnote.
        /// </summary>
        public static float Percentile(IReadOnlyList<float> sortedAscending, float quantile)
        {
            if (sortedAscending == null) throw new ArgumentNullException(nameof(sortedAscending));
            if (sortedAscending.Count == 0) return 0f;
            if (quantile <= 0f) return sortedAscending[0];
            if (quantile >= 1f) return sortedAscending[sortedAscending.Count - 1];

            var rank = quantile * (sortedAscending.Count - 1);
            var lower = (int)rank;
            var upper = Math.Min(lower + 1, sortedAscending.Count - 1);
            var fraction = rank - lower;
            return sortedAscending[lower] + (sortedAscending[upper] - sortedAscending[lower]) * fraction;
        }

        private delegate bool SamplePredicate(in FrameSample sample, int phase, float cutoff);

        private static BenchmarkAggregates AggregateWhere(
            FrameSample[] samples, int count, SamplePredicate include, int phase, float cutoff)
        {
            var frameMs = new List<float>();
            var gcBytes = new List<long>();
            var result = new BenchmarkAggregates();
            var minTime = float.MaxValue;
            var maxTime = float.MinValue;
            double meanSum = 0;
            double gpuSum = 0;
            var gpuFrames = 0;

            for (var i = 0; i < count; i++)
            {
                ref var s = ref samples[i];
                if (!include(in s, phase, cutoff))
                {
                    continue;
                }

                frameMs.Add(s.FrameMs);
                gcBytes.Add(s.GcAllocatedBytes);
                meanSum += s.FrameMs;
                result.GcAllocatedTotalBytes += s.GcAllocatedBytes;
                result.GcAllocationCountTotal += s.GcAllocationCount;
                if (s.FrameMs > result.MaxMs) result.MaxMs = s.FrameMs;
                if (s.TimeSeconds < minTime) minTime = s.TimeSeconds;
                if (s.TimeSeconds > maxTime) maxTime = s.TimeSeconds;
                if (s.GpuMs > 0f)
                {
                    gpuSum += s.GpuMs;
                    gpuFrames++;
                }

                // A collection "inside the aggregate" is a delta against the PREVIOUS buffer
                // sample even when that sample was excluded: the cumulative counter carries
                // across the boundary, and losing the first frame's delta would undercount a
                // collection that ran exactly on a phase boundary.
                if (i > 0 && s.GcCollectionsCumulative > samples[i - 1].GcCollectionsCumulative)
                {
                    result.GcCollections += s.GcCollectionsCumulative - samples[i - 1].GcCollectionsCumulative;
                    result.GcSpikeFrames++;
                }
            }

            result.FrameCount = frameMs.Count;
            if (frameMs.Count == 0)
            {
                return result;
            }

            frameMs.Sort();
            gcBytes.Sort();

            result.MeanMs = (float)(meanSum / frameMs.Count);
            result.MedianMs = Percentile(frameMs, 0.5f);
            result.P95Ms = Percentile(frameMs, 0.95f);
            result.P99Ms = Percentile(frameMs, 0.99f);
            result.GcAllocatedPerFrameMedianBytes = gcBytes[gcBytes.Count / 2];
            result.GpuMeanMs = gpuFrames > 0 ? (float)(gpuSum / gpuFrames) : 0f;

            var span = maxTime - minTime;
            result.AverageFps = span > 0f ? (frameMs.Count - 1) / span : 0f;
            return result;
        }
    }
}
