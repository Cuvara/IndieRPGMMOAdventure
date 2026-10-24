namespace Tests.Editor
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using NUnit.Framework;
    using Scripts.Session;

    /// <summary>
    /// What the player is told when the session ends, and what Retry does (audit CN-02), without
    /// a panel: <see cref="SessionRecovery"/> and <see cref="SessionStatusPresenter"/> against a
    /// fake endpoint.
    /// </summary>
    public sealed class SessionRecoveryTests
    {
        private sealed class FakeEndpoint : MainSessionFlow.IEndpoint
        {
            public Exception ConnectError;
            public Task HoldConnect = Task.CompletedTask;
            public int Connects;

            public string UserId { get; private set; } = string.Empty;

            public Task<string> AuthenticateDeviceAsync(string deviceId, CancellationToken ct) =>
                Task.FromResult("user-1");

            public async Task ConnectAsync(string mapId, CancellationToken ct)
            {
                this.Connects++;
                await this.HoldConnect;
                if (this.ConnectError != null) throw this.ConnectError;
                this.UserId = "user-1";
            }

            public Task<string> EnsurePartyAsync(bool create, string partyIdToJoin, CancellationToken ct) =>
                Task.FromResult(string.Empty);

            public Task<CharacterChoice> SelectCharacterAsync(string userId, CancellationToken ct) =>
                Task.FromResult(CharacterChoice.Default);

            public Task ConnectToDungeonAsync(string contentId, string partyId, CancellationToken ct) =>
                Task.CompletedTask;
        }

        private sealed class FakeView : ISessionStatusView
        {
            public readonly List<SessionStatus> Shown = new List<SessionStatus>();

            public event Action RetryClicked;

            public SessionStatus Last => this.Shown[this.Shown.Count - 1];

            public void Show(SessionStatus status) => this.Shown.Add(status);

            public void Click() => this.RetryClicked?.Invoke();
        }

        private static (SessionRecovery recovery, FakeEndpoint endpoint) Recovery(FakeEndpoint endpoint = null)
        {
            endpoint = endpoint ?? new FakeEndpoint();
            var recovery = new SessionRecovery(async ct =>
            {
                var flow = new MainSessionFlow();
                await flow.RunAsync(endpoint, "dev", "map_01", false, null, null, _ => { }, _ => { }, ct);
                return flow;
            });
            return (recovery, endpoint);
        }

        [Test]
        public void ANewSession_ShowsNothing()
        {
            var (recovery, _) = Recovery();

            Assert.That(recovery.Status.IsVisible, Is.False);
        }

        [Test]
        public void EndedByTheServer_IsVisibleWithItsCauseAndOffersRetry()
        {
            var (recovery, _) = Recovery();

            recovery.Ended("Kicked (duplicate_login)");

            Assert.That(recovery.Status.Kind, Is.EqualTo(SessionStatusKind.Ended));
            Assert.That(recovery.Status.IsVisible, Is.True);
            Assert.That(recovery.Status.CanRetry, Is.True);
            Assert.That(recovery.Status.Caption, Does.Contain("duplicate_login"));
        }

        [Test]
        public void AFailedSessionFlow_EndsTheSessionWithTheExceptionMessage()
        {
            var endpoint = new FakeEndpoint { ConnectError = new InvalidOperationException("gateway said no") };
            var (recovery, _) = Recovery(endpoint);
            var flow = new MainSessionFlow();
            flow.RunAsync(endpoint, "dev", "map_01", false, null, null, _ => { }, _ => { }, CancellationToken.None)
                .GetAwaiter().GetResult();

            recovery.FlowFinished(flow);

            Assert.That(flow.CurrentPhase, Is.EqualTo(MainSessionFlow.Phase.Failed));
            Assert.That(recovery.Status.Kind, Is.EqualTo(SessionStatusKind.Ended));
            Assert.That(recovery.Status.Cause, Is.EqualTo("gateway said no"));
        }

        [Test]
        public void ReconnectingIsShownWithoutRetry()
        {
            var (recovery, _) = Recovery();

            recovery.Reconnecting();

            Assert.That(recovery.Status.Kind, Is.EqualTo(SessionStatusKind.Reconnecting));
            Assert.That(recovery.Status.CanRetry, Is.False);
        }

        [Test]
        public void Retry_RunsAFreshFlowThatReachesInWorld_AndClearsTheOverlay()
        {
            var (recovery, endpoint) = Recovery();
            recovery.Ended("Kicked (duplicate_login)");

            recovery.RetryAsync(CancellationToken.None).GetAwaiter().GetResult();

            Assert.That(endpoint.Connects, Is.EqualTo(1));
            Assert.That(endpoint.UserId, Is.EqualTo("user-1"), "the fresh flow connected");
            Assert.That(recovery.Status.IsVisible, Is.False);
        }

        [Test]
        public void AFailedRetry_EndsAgainWithTheNewCauseAndCanBeRetried()
        {
            var endpoint = new FakeEndpoint { ConnectError = new InvalidOperationException("still down") };
            var (recovery, _) = Recovery(endpoint);
            recovery.Ended("first");

            recovery.RetryAsync(CancellationToken.None).GetAwaiter().GetResult();

            Assert.That(recovery.Status.Kind, Is.EqualTo(SessionStatusKind.Ended));
            Assert.That(recovery.Status.Cause, Is.EqualTo("still down"));
            Assert.That(recovery.Status.CanRetry, Is.True);

            endpoint.ConnectError = null;
            recovery.RetryAsync(CancellationToken.None).GetAwaiter().GetResult();
            Assert.That(recovery.Status.IsVisible, Is.False);
        }

        [Test]
        public void Retry_WhenNotEnded_DoesNothing()
        {
            var (recovery, endpoint) = Recovery();

            recovery.RetryAsync(CancellationToken.None).GetAwaiter().GetResult();

            Assert.That(endpoint.Connects, Is.EqualTo(0));
        }

        [Test]
        public void ADoubleRetry_RunsOneFlow()
        {
            var hold = new TaskCompletionSource<bool>();
            var endpoint = new FakeEndpoint { HoldConnect = hold.Task };
            var (recovery, _) = Recovery(endpoint);
            recovery.Ended("lost");

            var first = recovery.RetryAsync(CancellationToken.None);
            var second = recovery.RetryAsync(CancellationToken.None);
            Assert.That(recovery.Status.CanRetry, Is.False, "the button is off while a retry runs");
            hold.SetResult(true);
            Task.WhenAll(first, second).GetAwaiter().GetResult();

            Assert.That(endpoint.Connects, Is.EqualTo(1));
        }

        // ── presenter ────────────────────────────────────────────────────

        [Test]
        public void ThePresenter_MirrorsStatusChangesToTheView_AndRetryClickReachesInWorld()
        {
            var (recovery, endpoint) = Recovery();
            var view = new FakeView();
            using (new SessionStatusPresenter(recovery, view, CancellationToken.None))
            {
                Assert.That(view.Last.IsVisible, Is.False, "the view starts hidden");

                recovery.Ended("Kicked (duplicate_login)");
                Assert.That(view.Last.Caption, Does.Contain("duplicate_login"));
                Assert.That(view.Last.CanRetry, Is.True);

                view.Click();

                Assert.That(endpoint.Connects, Is.EqualTo(1));
                Assert.That(view.Last.IsVisible, Is.False, "retry reached the world and hid the overlay");
            }
        }

        [Test]
        public void ADisposedPresenter_NoLongerTouchesTheView()
        {
            var (recovery, endpoint) = Recovery();
            var view = new FakeView();
            new SessionStatusPresenter(recovery, view, CancellationToken.None).Dispose();
            var shown = view.Shown.Count;

            recovery.Ended("late");
            view.Click();

            Assert.That(view.Shown.Count, Is.EqualTo(shown));
            Assert.That(endpoint.Connects, Is.EqualTo(0));
        }
    }
}
