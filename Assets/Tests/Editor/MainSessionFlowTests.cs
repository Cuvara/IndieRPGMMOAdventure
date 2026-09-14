namespace Tests.Editor
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using NUnit.Framework;
    using Scripts.Session;

    /// <summary>
    /// The MainScene session sequence against a fake endpoint: phases, the harness marker lines
    /// byte for byte, and what a failure or a cancel leaves behind.
    /// </summary>
    public sealed class MainSessionFlowTests
    {
        private sealed class FakeEndpoint : MainSessionFlow.IEndpoint
        {
            public readonly List<string> Calls = new List<string>();
            public string AuthUserId = "user-1";
            public string DeviceIdSeen;
            public string MapSeen;
            public Exception AuthError;
            public Exception ConnectError;
            public TaskCompletionSource<bool> HoldConnect;

            public string UserId { get; set; } = string.Empty;

            // Completes synchronously on purpose: a blocking EditMode test under Unity's
            // SynchronizationContext would deadlock on a continuation posted to the main thread.
            public Task<string> AuthenticateDeviceAsync(string deviceId, CancellationToken cancellationToken)
            {
                this.Calls.Add("auth");
                this.DeviceIdSeen = deviceId;
                if (this.AuthError != null) return Task.FromException<string>(this.AuthError);
                return Task.FromResult(this.AuthUserId);
            }

            public async Task ConnectAsync(string mapId, CancellationToken cancellationToken)
            {
                this.Calls.Add("connect");
                this.MapSeen = mapId;
                if (this.HoldConnect != null)
                {
                    using (cancellationToken.Register(() => this.HoldConnect.TrySetCanceled(cancellationToken)))
                    {
                        await this.HoldConnect.Task;
                    }
                }

                if (this.ConnectError != null) throw this.ConnectError;
                this.UserId = this.ConnectedUserId ?? this.AuthUserId;
            }

            public string ConnectedUserId;

            // ---- party / dungeon ----
            public string PartyIdToReturn = "party-1";
            public bool CreateSeen;
            public string JoinIdSeen;
            public string DungeonContentSeen;
            public string DungeonPartySeen;
            public Exception PartyError;

            public Task<string> EnsurePartyAsync(bool create, string partyIdToJoin, CancellationToken cancellationToken)
            {
                this.Calls.Add(create ? "party-create" : "party-join");
                this.CreateSeen = create;
                this.JoinIdSeen = partyIdToJoin;
                if (this.PartyError != null) return Task.FromException<string>(this.PartyError);
                return Task.FromResult(this.PartyIdToReturn);
            }

            public Task ConnectToDungeonAsync(string contentId, string partyId, CancellationToken cancellationToken)
            {
                this.Calls.Add("connect-dungeon");
                this.DungeonContentSeen = contentId;
                this.DungeonPartySeen = partyId;
                if (this.ConnectError != null) return Task.FromException(this.ConnectError);
                this.UserId = this.ConnectedUserId ?? this.AuthUserId;
                return Task.CompletedTask;
            }
        }

        private FakeEndpoint endpoint;
        private List<string> log;
        private List<string> errors;
        private SynchronizationContext savedContext;

        [SetUp]
        public void SetUp()
        {
            // Continuations run inline on the test thread: the cancel test completes a pending
            // task and asserts right after, which the editor's deferring context would break.
            this.savedContext = SynchronizationContext.Current;
            SynchronizationContext.SetSynchronizationContext(null);
            this.endpoint = new FakeEndpoint();
            this.log = new List<string>();
            this.errors = new List<string>();
        }

        [TearDown]
        public void TearDown() => SynchronizationContext.SetSynchronizationContext(this.savedContext);

        private Task Run(
            MainSessionFlow flow,
            string deviceId = "dev-1",
            string map = "map_01",
            bool createParty = false,
            string partyIdToJoin = null,
            string dungeon = null,
            CancellationToken ct = default) =>
            flow.RunAsync(
                this.endpoint, deviceId, map, createParty, partyIdToJoin, dungeon,
                this.log.Add, this.errors.Add, ct);

        // ---- party and dungeon (ADR-26) -------------------------------------------------

        /// <summary>
        /// The default path must be untouched by the party work: no party call, and a map
        /// connect. Every existing caller passes neither flag.
        /// </summary>
        [Test]
        public void WithoutAPartyRequest_NoPartyCallIsMade()
        {
            var flow = new MainSessionFlow();

            this.Run(flow).GetAwaiter().GetResult();

            CollectionAssert.AreEqual(new[] { "auth", "connect" }, this.endpoint.Calls);
            Assert.That(flow.PartyId, Is.Empty);
        }

        [Test]
        public void CreatingAParty_HappensBeforeTheWorld_AndTheDungeonUsesIt()
        {
            var flow = new MainSessionFlow();
            this.endpoint.PartyIdToReturn = "party-abc";

            this.Run(flow, createParty: true, dungeon: "dungeon_01").GetAwaiter().GetResult();

            // Order matters: a dungeon instance is keyed by the party, so there is nothing to
            // enter until the party exists.
            CollectionAssert.AreEqual(new[] { "auth", "party-create", "connect-dungeon" }, this.endpoint.Calls);
            Assert.That(this.endpoint.DungeonContentSeen, Is.EqualTo("dungeon_01"));
            Assert.That(this.endpoint.DungeonPartySeen, Is.EqualTo("party-abc"));
            Assert.That(flow.PartyId, Is.EqualTo("party-abc"));
        }

        [Test]
        public void JoiningAParty_PassesTheIdThrough()
        {
            var flow = new MainSessionFlow();

            this.Run(flow, partyIdToJoin: "party-xyz", dungeon: "dungeon_01").GetAwaiter().GetResult();

            CollectionAssert.AreEqual(new[] { "auth", "party-join", "connect-dungeon" }, this.endpoint.Calls);
            Assert.That(this.endpoint.CreateSeen, Is.False);
            Assert.That(this.endpoint.JoinIdSeen, Is.EqualTo("party-xyz"));
        }

        /// <summary>
        /// A dungeon with no party FAILS. It must not fall back to the map: a client
        /// configured for a dungeon and quietly dropped into the open world is a
        /// misconfiguration that survives the test run that should have caught it.
        /// </summary>
        [Test]
        public void ADungeonWithNoParty_FailsRatherThanEnteringTheMap()
        {
            var flow = new MainSessionFlow();

            this.Run(flow, dungeon: "dungeon_01").GetAwaiter().GetResult();

            Assert.That(flow.CurrentPhase, Is.EqualTo(MainSessionFlow.Phase.Failed));
            CollectionAssert.DoesNotContain(this.endpoint.Calls, "connect");
            CollectionAssert.DoesNotContain(this.endpoint.Calls, "connect-dungeon");
        }

        [Test]
        public void APartyWithoutADungeon_StillEntersTheMap()
        {
            var flow = new MainSessionFlow();

            this.Run(flow, createParty: true).GetAwaiter().GetResult();

            // A party is not only for dungeons; forming one in the open world is legitimate.
            CollectionAssert.AreEqual(new[] { "auth", "party-create", "connect" }, this.endpoint.Calls);
        }

        [Test]
        public void HappyPath_AuthenticatesThenConnects_AndPrintsTheHarnessMarkers()
        {
            var flow = new MainSessionFlow();

            this.Run(flow).GetAwaiter().GetResult();

            Assert.That(flow.CurrentPhase, Is.EqualTo(MainSessionFlow.Phase.InWorld));
            Assert.That(flow.UserId, Is.EqualTo("user-1"));
            CollectionAssert.AreEqual(new[] { "auth", "connect" }, this.endpoint.Calls);
            Assert.That(this.endpoint.DeviceIdSeen, Is.EqualTo("dev-1"));
            Assert.That(this.endpoint.MapSeen, Is.EqualTo("map_01"));

            // Byte-identical to what Tools/verify-multiclient.sh greps for and what the netcode
            // DOTS sample prints: "Auth OK" with a "user_id=<id>" token, and "IN WORLD".
            CollectionAssert.Contains(this.log, "[DOTSNet] Authenticating device=dev-1");
            CollectionAssert.Contains(this.log, "[DOTSNet] Auth OK, user_id=user-1");
            CollectionAssert.Contains(this.log, "[DOTSNet] IN WORLD as user-1");
            CollectionAssert.IsEmpty(this.errors);
        }

        [Test]
        public void MarkerConstants_MatchTheHarnessContract()
        {
            StringAssert.Contains("Auth OK", MainSessionFlow.AuthOkPrefix);
            StringAssert.EndsWith("user_id=", MainSessionFlow.AuthOkPrefix);
            StringAssert.Contains("IN WORLD", MainSessionFlow.InWorldPrefix);
            StringAssert.StartsWith("[DOTSNet] ", MainSessionFlow.AuthOkPrefix);
            StringAssert.StartsWith("[DOTSNet] ", MainSessionFlow.InWorldPrefix);
        }

        [Test]
        public void NullDeviceId_IsPassedThrough_AndReportedAsMachine()
        {
            var flow = new MainSessionFlow();

            this.Run(flow, deviceId: null).GetAwaiter().GetResult();

            Assert.That(this.endpoint.DeviceIdSeen, Is.Null);
            CollectionAssert.Contains(this.log, "[DOTSNet] Authenticating device=<machine>");
            Assert.That(flow.CurrentPhase, Is.EqualTo(MainSessionFlow.Phase.InWorld));
        }

        [Test]
        public void AuthFailure_IsFatal_AndNeverConnects()
        {
            this.endpoint.AuthError = new InvalidOperationException("401 Unauthorized");
            var flow = new MainSessionFlow();

            this.Run(flow).GetAwaiter().GetResult();

            Assert.That(flow.CurrentPhase, Is.EqualTo(MainSessionFlow.Phase.Failed));
            Assert.That(flow.Error, Is.SameAs(this.endpoint.AuthError));
            CollectionAssert.AreEqual(new[] { "auth" }, this.endpoint.Calls, "no connect after a failed auth");
            Assert.That(this.errors.Count, Is.EqualTo(1));
            StringAssert.StartsWith("[DOTSNet] FATAL: ", this.errors[0]);
            StringAssert.Contains("401 Unauthorized", this.errors[0]);
            Assert.That(this.log.Exists(l => l.Contains("Auth OK")), Is.False);
            Assert.That(this.log.Exists(l => l.Contains("IN WORLD")), Is.False);
        }

        [Test]
        public void ConnectFailure_IsFatal_AfterAuthOk()
        {
            this.endpoint.ConnectError = new TimeoutException("gateway");
            var flow = new MainSessionFlow();

            this.Run(flow).GetAwaiter().GetResult();

            Assert.That(flow.CurrentPhase, Is.EqualTo(MainSessionFlow.Phase.Failed));
            CollectionAssert.Contains(this.log, "[DOTSNet] Auth OK, user_id=user-1");
            Assert.That(this.log.Exists(l => l.Contains("IN WORLD")), Is.False);
            StringAssert.Contains("gateway", this.errors[0]);
        }

        [Test]
        public void CancelDuringConnect_EndsCancelled_NotFailed()
        {
            this.endpoint.HoldConnect = new TaskCompletionSource<bool>();
            using var cts = new CancellationTokenSource();
            var flow = new MainSessionFlow();

            var run = this.Run(flow, ct: cts.Token);
            Assert.That(run.IsCompleted, Is.False, "held at connect");
            CollectionAssert.Contains(this.endpoint.Calls, "connect");
            Assert.That(flow.CurrentPhase, Is.EqualTo(MainSessionFlow.Phase.Connecting));

            cts.Cancel();
            run.GetAwaiter().GetResult();

            Assert.That(flow.CurrentPhase, Is.EqualTo(MainSessionFlow.Phase.Cancelled));
            Assert.That(flow.Error, Is.Null);
            CollectionAssert.Contains(this.log, "[DOTSNet] Cancelled");
            CollectionAssert.IsEmpty(this.errors);
        }

        [Test]
        public void ForeignCancellation_IsFatal_NotCancelled()
        {
            // A superseded login generation or a library's own timeout token throws
            // OperationCanceledException while the session token is fine. That is a failure with
            // a cause to print, not a quiet "Cancelled".
            this.endpoint.AuthError = new OperationCanceledException("operation 1 was superseded by operation 2");
            var flow = new MainSessionFlow();

            this.Run(flow).GetAwaiter().GetResult();

            Assert.That(flow.CurrentPhase, Is.EqualTo(MainSessionFlow.Phase.Failed));
            Assert.That(this.errors.Count, Is.EqualTo(1));
            StringAssert.Contains("superseded by operation 2", this.errors[0]);
            StringAssert.Contains("session token was NOT cancelled", this.errors[0]);
            Assert.That(this.log.Exists(l => l == "[DOTSNet] Cancelled"), Is.False);
        }

        [Test]
        public void PreCancelled_DoesNothing()
        {
            using var cts = new CancellationTokenSource();
            cts.Cancel();
            var flow = new MainSessionFlow();

            this.Run(flow, ct: cts.Token).GetAwaiter().GetResult();

            Assert.That(flow.CurrentPhase, Is.EqualTo(MainSessionFlow.Phase.Cancelled));
            CollectionAssert.IsEmpty(this.endpoint.Calls);
        }

        [Test]
        public void InWorldLine_ReportsTheIdTheSessionIsActuallyInWorldAs()
        {
            // Auth and the gateway may spell the user differently; the line reports the client's.
            this.endpoint.AuthUserId = "nakama-id";
            this.endpoint.ConnectedUserId = "gateway-id";
            var flow = new MainSessionFlow();

            this.Run(flow).GetAwaiter().GetResult();

            CollectionAssert.Contains(this.log, "[DOTSNet] Auth OK, user_id=nakama-id");
            CollectionAssert.Contains(this.log, "[DOTSNet] IN WORLD as gateway-id");
        }

        [Test]
        public void RunningTwice_IsRefused()
        {
            var flow = new MainSessionFlow();
            this.Run(flow).GetAwaiter().GetResult();

            Assert.Throws<InvalidOperationException>(() => this.Run(flow).GetAwaiter().GetResult());
        }
    }
}
