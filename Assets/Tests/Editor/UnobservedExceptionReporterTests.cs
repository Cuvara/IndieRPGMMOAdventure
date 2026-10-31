namespace Tests.Editor
{
    using System;
    using System.Text.RegularExpressions;
    using System.Threading.Tasks;
    using Cysharp.Threading.Tasks;
    using NUnit.Framework;
    using Scripts.DI;
    using UnityEngine;
    using UnityEngine.TestTools;

    /// <summary>The unobserved-exception hook: subscribe, log once with the prefix, unsubscribe.</summary>
    public sealed class UnobservedExceptionReporterTests
    {
        private UnobservedExceptionReporter reporter;

        [SetUp]
        public void SetUp() => this.reporter = new UnobservedExceptionReporter();

        [TearDown]
        public void TearDown() => this.reporter.Dispose();

        [Test]
        public void StartThenDispose_TogglesSubscription_AndIsIdempotent()
        {
            this.reporter.Start();
            this.reporter.Start();
            Assert.IsTrue(this.reporter.IsSubscribed);

            this.reporter.Dispose();
            this.reporter.Dispose();
            Assert.IsFalse(this.reporter.IsSubscribed);
        }

        [Test]
        public void ForgottenFaultedUniTask_IsLoggedOnce_WithPrefix()
        {
            this.reporter.Start();

            // Exactly one expected line per fault; any extra (default UniTask) line fails the test.
            LogAssert.Expect(LogType.Error, new Regex(@"^\[Unobserved\] UniTask: System\.InvalidOperationException: boom-1"));
            UniTask.FromException(new InvalidOperationException("boom-1")).Forget();
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void AfterDispose_UniTaskExceptionIsNotRoutedToTheReporter()
        {
            this.reporter.Start();
            this.reporter.Dispose();

            // UniTask's own default line returns; it is an exception log, not an [Unobserved] error.
            LogAssert.Expect(LogType.Exception, new Regex("boom-3"));
            UniTask.FromException(new InvalidOperationException("boom-3")).Forget();
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void UnobservedTask_IsLoggedOnce_AndCancellationIsIgnored()
        {
            LogAssert.Expect(LogType.Error, new Regex(@"^\[Unobserved\] Task: .*boom-4"));
            this.reporter.OnTaskUnobserved(
                this,
                new UnobservedTaskExceptionEventArgs(new AggregateException(new InvalidOperationException("boom-4"))));

            this.reporter.OnTaskUnobserved(
                this,
                new UnobservedTaskExceptionEventArgs(new AggregateException(new OperationCanceledException())));
            LogAssert.NoUnexpectedReceived();
        }
    }
}
