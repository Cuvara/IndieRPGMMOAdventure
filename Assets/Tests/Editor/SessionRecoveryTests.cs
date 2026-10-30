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

        // -- first-connect retry status ---------------------------------

        [Test]
        public void FirstConnectRetry_ShowsConnecting_ThenInWorldClears_ThenEndedOffersRetry()
        {
            var (recovery, _) = Recovery();
            var view = new FakeView();
            using (new SessionStatusPresenter(recovery, view, CancellationToken.None))
            {
                recovery.ConnectRetrying(2, 5);

                Assert.That(view.Last.Kind, Is.EqualTo(SessionStatusKind.Connecting));
                Assert.That(view.Last.IsVisible, Is.True);
                Assert.That(view.Last.CanRetry, Is.False);
                Assert.That(view.Last.Caption, Is.EqualTo("Connecting... (attempt 2)"));

                recovery.ConnectRetrying(3, 5);
                Assert.That(view.Last.Caption, Is.EqualTo("Connecting... (attempt 3)"));

                recovery.InWorld();
                Assert.That(view.Last.Kind, Is.EqualTo(SessionStatusKind.Active));
                Assert.That(view.Last.IsVisible, Is.False);

                recovery.Ended("gateway unreachable");
                Assert.That(view.Last.Kind, Is.EqualTo(SessionStatusKind.Ended));
                Assert.That(view.Last.CanRetry, Is.True);
            }
        }

        [Test]
        public void ConnectRetry_AfterTheSessionWasInWorld_IsLeftToTheReconnectStatus()
        {
            var (recovery, _) = Recovery();
            recovery.InWorld();
            recovery.Reconnecting();

            recovery.ConnectRetrying(1, 5);

            Assert.That(recovery.Status.Kind, Is.EqualTo(SessionStatusKind.Reconnecting));
        }

        [Test]
        public void ConnectRetry_AfterInWorldWhileStillActive_StaysActive()
        {
            // The only state where wasInWorld decides: without it the Active kind would pass the
            // kind check and show Connecting over a session that is already playing.
            var (recovery, _) = Recovery();
            recovery.InWorld();

            recovery.ConnectRetrying(1, 5);

            Assert.That(recovery.Status.Kind, Is.EqualTo(SessionStatusKind.Active));
            Assert.That(recovery.Status.IsVisible, Is.False);
        }

        [Test]
        public async Task ConnectRetry_DuringManualRetry_ShowsConnecting_ThenInWorldClears()
        {
            var (recovery, endpoint) = Recovery();
            recovery.InWorld();
            recovery.Ended("gateway unreachable");
            var gate = new TaskCompletionSource<bool>();
            endpoint.HoldConnect = gate.Task;

            var retry = recovery.RetryAsync(CancellationToken.None);
            Assert.That(recovery.Status.Kind, Is.EqualTo(SessionStatusKind.Reconnecting));

            recovery.ConnectRetrying(2, 5);
            Assert.That(recovery.Status.Kind, Is.EqualTo(SessionStatusKind.Connecting));
            Assert.That(recovery.Status.Caption, Is.EqualTo("Connecting... (attempt 2)"));
            Assert.That(recovery.Status.CanRetry, Is.False);

            recovery.ConnectRetrying(3, 5);
            Assert.That(recovery.Status.Caption, Is.EqualTo("Connecting... (attempt 3)"));

            gate.SetResult(true);
            await retry;
            Assert.That(recovery.Status.Kind, Is.EqualTo(SessionStatusKind.Active));
        }

        [Test]
        public void ConnectRetry_WhenEnded_DoesNotHideTheRetryButton()
        {
            var (recovery, _) = Recovery();
            recovery.Ended("refused");

            recovery.ConnectRetrying(1, 5);

            Assert.That(recovery.Status.Kind, Is.EqualTo(SessionStatusKind.Ended));
            Assert.That(recovery.Status.CanRetry, Is.True);
        }
    }
}
