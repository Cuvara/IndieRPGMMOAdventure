namespace Tests.Editor
{
    using System;
    using System.Threading;
    using NUnit.Framework;
    using Scripts.Nakama.Auth;

    /// <summary>
    /// <see cref="OperationGeneration"/> as plain C#: the guard every Nakama login
    /// runs after its HTTP call returns and before it applies the session. Pins the
    /// three outcomes a completed call can meet — still current, cancelled,
    /// superseded — and that a cancel is reported as a cancel even when both apply.
    /// No delays anywhere: nothing here depends on the editor loop.
    /// </summary>
    public class OperationGenerationTests
    {
        [Test]
        public void ANewOperation_IsCurrent_UntilTheNextBegins()
        {
            var gen = new OperationGeneration();
            var first = gen.Begin();
            Assert.That(gen.IsCurrent(first), Is.True);
            Assert.That(gen.Current, Is.EqualTo(first));

            var second = gen.Begin();
            Assert.That(second, Is.GreaterThan(first));
            Assert.That(gen.IsCurrent(first), Is.False, "the older login lost");
            Assert.That(gen.IsCurrent(second), Is.True);
        }

        [Test]
        public void TheCurrentOperation_PassesTheGuard()
        {
            var gen = new OperationGeneration();
            var g = gen.Begin();
            Assert.DoesNotThrow(() => gen.ThrowIfStale(g, CancellationToken.None));
        }

        [Test]
        public void ASupersededCompletion_IsDiscarded_AsACancellation()
        {
            // Login A's HTTP call is in flight; the player starts login B; A's call
            // then completes. A must not become the session.
            var gen = new OperationGeneration();
            var a = gen.Begin();
            gen.Begin();
            var ex = Assert.Throws<OperationCanceledException>(() => gen.ThrowIfStale(a, CancellationToken.None));
            Assert.That(ex.Message, Does.Contain("superseded"));
        }

        [Test]
        public void SignOut_InvalidatesEverythingInFlight_WithoutStartingAnything()
        {
            var gen = new OperationGeneration();
            var login = gen.Begin();
            gen.Invalidate();
            Assert.That(gen.IsCurrent(login), Is.False, "a login completing after sign-out must be dropped");
            Assert.Throws<OperationCanceledException>(() => gen.ThrowIfStale(login, CancellationToken.None));
        }

        [Test]
        public void ACancelledCaller_IsReportedAsCancelled_EvenWhenAlsoSuperseded()
        {
            var gen = new OperationGeneration();
            var a = gen.Begin();
            gen.Begin();
            using var cts = new CancellationTokenSource();
            cts.Cancel();
            var ex = Assert.Throws<OperationCanceledException>(() => gen.ThrowIfStale(a, cts.Token));
            Assert.That(ex.CancellationToken, Is.EqualTo(cts.Token), "the caller's own token, so its catch matches");
        }

        [Test]
        public void ACancelAfterCompletion_StillBlocksTheApply()
        {
            // The case F09 named: the token is checked before the HTTP call, the
            // call completes, the user cancelled meanwhile. The post-await guard is
            // what keeps ApplySession from running.
            var gen = new OperationGeneration();
            var g = gen.Begin();
            using var cts = new CancellationTokenSource();
            Assert.DoesNotThrow(() => gen.ThrowIfStale(g, cts.Token), "before the cancel: fine");
            cts.Cancel();
            Assert.Throws<OperationCanceledException>(() => gen.ThrowIfStale(g, cts.Token));
        }

        [Test]
        public void GenerationsNeverRepeat()
        {
            var gen = new OperationGeneration();
            var seen = new System.Collections.Generic.HashSet<int>();
            for (var i = 0; i < 1000; i++)
            {
                Assert.That(seen.Add(gen.Begin()), Is.True);
            }
        }
    }
}
