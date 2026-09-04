namespace Tests.Editor
{
    using NUnit.Framework;
    using Scripts.Benchmark;

    /// <summary>
    /// Command-line override parsing: presence, absence, malformed values falling back
    /// (never throwing — an unattended run must start), and the all-or-nothing ramp rule.
    /// </summary>
    public sealed class BenchmarkArgsTests
    {
        private static readonly BenchmarkPhase[] Fallback = { new BenchmarkPhase("fallback", 1, 1f) };

        [Test]
        public void ResolveFloat_ReadsValueAndFallsBack()
        {
            Assert.That(BenchmarkArgs.ResolveFloat(new[] { "-benchWarmup", "12.5" }, BenchmarkArgs.WarmupFlag, 10f), Is.EqualTo(12.5f));
            Assert.That(BenchmarkArgs.ResolveFloat(new[] { "-benchWarmup", "junk" }, BenchmarkArgs.WarmupFlag, 10f), Is.EqualTo(10f));
            Assert.That(BenchmarkArgs.ResolveFloat(new[] { "-benchWarmup", "-3" }, BenchmarkArgs.WarmupFlag, 10f), Is.EqualTo(10f));
            Assert.That(BenchmarkArgs.ResolveFloat(new[] { "-benchWarmup" }, BenchmarkArgs.WarmupFlag, 10f), Is.EqualTo(10f));
            Assert.That(BenchmarkArgs.ResolveFloat(null, BenchmarkArgs.WarmupFlag, 10f), Is.EqualTo(10f));
        }

        [Test]
        public void HasFlag_MatchesCaseInsensitively()
        {
            Assert.That(BenchmarkArgs.HasFlag(new[] { "-benchnoquit" }, BenchmarkArgs.NoQuitFlag), Is.True);
            Assert.That(BenchmarkArgs.HasFlag(new[] { "-other" }, BenchmarkArgs.NoQuitFlag), Is.False);
            Assert.That(BenchmarkArgs.HasFlag(null, BenchmarkArgs.NoQuitFlag), Is.False);
        }

        [Test]
        public void ResolvePhases_ParsesRampSpec()
        {
            var phases = BenchmarkArgs.ResolvePhases(
                new[] { BenchmarkArgs.PhasesFlag, "250:30,500:20.5" }, Fallback);

            Assert.That(phases, Has.Length.EqualTo(2));
            Assert.That(phases[0].EntityCount, Is.EqualTo(250));
            Assert.That(phases[0].Seconds, Is.EqualTo(30f));
            Assert.That(phases[0].Label, Is.EqualTo("ramp-250"));
            Assert.That(phases[1].EntityCount, Is.EqualTo(500));
            Assert.That(phases[1].Seconds, Is.EqualTo(20.5f));
        }

        [Test]
        public void ResolvePhases_AnyMalformedStepFallsBackWhole()
        {
            // A half-parsed ramp would silently measure the wrong workload.
            var phases = BenchmarkArgs.ResolvePhases(
                new[] { BenchmarkArgs.PhasesFlag, "250:30,junk,500:30" }, Fallback);
            Assert.That(phases, Is.SameAs(Fallback));

            Assert.That(BenchmarkArgs.ResolvePhases(new[] { BenchmarkArgs.PhasesFlag, "250:0" }, Fallback), Is.SameAs(Fallback));
            Assert.That(BenchmarkArgs.ResolvePhases(new[] { BenchmarkArgs.PhasesFlag, "-5:30" }, Fallback), Is.SameAs(Fallback));
            Assert.That(BenchmarkArgs.ResolvePhases(new[] { "-nothing" }, Fallback), Is.SameAs(Fallback));
            Assert.That(BenchmarkArgs.ResolvePhases(null, Fallback), Is.SameAs(Fallback));
        }
    }
}
