namespace Tests.Editor
{
    using System;
    using System.Collections;
    using System.Collections.Generic;
    using System.Text.RegularExpressions;
    using System.Threading;
    using System.Threading.Tasks;
    using Cuvara.Netcode.Client;
    using Cuvara.Netcode.Codec;
    using Cysharp.Threading.Tasks;
    using NUnit.Framework;
    using Scripts.DI;
    using Scripts.Session;
    using UnityEngine;
    using UnityEngine.TestTools;
    using static Tests.Editor.ScriptedNetcode;

    /// <summary>
    /// <see cref="MainSessionDriver"/> over the real <see cref="NetworkClient"/> and a scripted
    /// backend: what the player is shown when the session ends (audit CN-02) and what dispose
    /// leaves behind (audit CN-10). Only the endpoint (Nakama, party, roster) and the view are
    /// fakes; the driver's event wiring and its teardown are the production code.
    /// </summary>
    public sealed class MainSessionDriverTests
    {
        /// <summary>The endpoint the driver adapts, reduced to what the netcode client needs.</summary>
        private sealed class ClientEndpoint : MainSessionFlow.IEndpoint
        {
            private readonly NetworkClient client;

            public int Connects;

            /// <summary>When set, ConnectAsync waits here (and honours the token).</summary>
            public TaskCompletionSource<bool> Hold;

            public CancellationToken TokenSeen;

            public ClientEndpoint(NetworkClient client) => this.client = client;

            public string UserId => this.client.UserId;

            public Task<string> AuthenticateDeviceAsync(string deviceId, CancellationToken ct) =>
                Task.FromResult("u1");

            public async Task ConnectAsync(string mapId, CancellationToken ct)
            {
                this.Connects++;
                this.TokenSeen = ct;
                if (this.Hold != null)
                {
                    using (ct.Register(() => this.Hold.TrySetCanceled(ct)))
                    {
                        await this.Hold.Task;
                    }
                }

                await this.client.ConnectAsync(mapId, ct);
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

            public SessionStatus Last => this.Shown.Count == 0 ? null : this.Shown[this.Shown.Count - 1];

            public void Show(SessionStatus status) => this.Shown.Add(status);

            public void Click() => this.RetryClicked?.Invoke();
        }

        /// <summary>A credential for the first login, then a refusal: the second call cannot succeed.</summary>
        private sealed class RefusingAuth : Cuvara.Netcode.Auth.IAuthProvider
        {
            private int calls;

            public UniTask<string> GetJwtAsync(CancellationToken ct) =>
                ++this.calls == 1
                    ? UniTask.FromResult("jwt1")
                    : throw new InvalidOperationException("Nakama refused the session");
        }

        private sealed class Rig
        {
            public ScriptedFactory Factory;
            public NetworkClient Client;
            public ClientEndpoint Endpoint;
            public FakeView View = new FakeView();
            public MainSessionDriver Driver;
            public RecordingLog Log = new RecordingLog();

            public static Rig Create(ScriptedFactory factory, Cuvara.Netcode.Auth.IAuthProvider auth = null)
            {
                var rig = new Rig { Factory = factory };
                rig.Client = new NetworkClient(
                    FastSettings(), factory, new JsonWireCodec(), rig.Log, auth ?? new StubAuth());
                rig.Endpoint = new ClientEndpoint(rig.Client);
                var backend = new BackendSettings
                {
                    Value = new BackendCommandLine.Settings { MapId = "map_01", GatewayHost = "gw", GatewayPort = 1 },
                };
                rig.Driver = new MainSessionDriver(rig.Client, backend, () => rig.Endpoint, rig.View);
                return rig;
            }
        }

        private static ScriptedFactory OneLogin() =>
            new ScriptedFactory().Then(GatewayOk()).Then(GameServerJoinOk());

        private static IEnumerator Until(Func<bool> condition, Func<string> describe)
        {
            for (var deadline = Deadline(); !condition() && DateTime.UtcNow < deadline;) yield return null;
            Assert.That(condition(), Is.True, describe());
        }

        // ── CN-02: the player is told, and can retry ─────────────────────

        [UnityTest]
        public IEnumerator AKickedSession_ShowsTheCause_AndRetryReachesInWorld()
        {
            var rig = Rig.Create(OneLogin().Then(GatewayOk()).Then(GameServerJoinOk()));
            rig.Driver.Start();
            yield return Until(() => rig.Driver.Flow?.CurrentPhase == MainSessionFlow.Phase.InWorld, () => rig.Log.ToString());
            Assert.That(rig.View.Last.IsVisible, Is.False, "nothing to show while playing");

            // The server evicts this login: the client stops, and nothing will bring it back.
            rig.Factory.Created[1].Deliver(Kick("duplicate_login"));

            yield return Until(() => rig.Client.State == NetworkClientState.Ended, () => rig.Log.ToString());
            Assert.That(rig.View.Last.Kind, Is.EqualTo(SessionStatusKind.Ended));
            Assert.That(rig.View.Last.Caption, Does.Contain("duplicate_login"));
            Assert.That(rig.View.Last.CanRetry, Is.True);

            var firstFlow = rig.Driver.Flow;
            rig.View.Click();

            yield return Until(
                () => rig.Client.State == NetworkClientState.InWorld && !rig.View.Last.IsVisible,
                () => rig.View.Last + "\n" + rig.Log);
            Assert.That(rig.Endpoint.Connects, Is.EqualTo(2));
            Assert.That(rig.Driver.Flow, Is.Not.SameAs(firstFlow), "retry runs a fresh MainSessionFlow");
            Assert.That(rig.Driver.Flow.CurrentPhase, Is.EqualTo(MainSessionFlow.Phase.InWorld));

            rig.Driver.Dispose();
        }

        [UnityTest]
        public IEnumerator AnExhaustedReconnect_ShowsTheReasonItGaveUp()
        {
            var rig = Rig.Create(OneLogin(), new RefusingAuth());
            rig.Driver.Start();
            yield return Until(() => rig.Driver.Flow?.CurrentPhase == MainSessionFlow.Phase.InWorld, () => rig.Log.ToString());

            rig.Factory.Created[1].Eof(); // the network drops; the credential is then refused

            yield return Until(() => rig.View.Last.Kind == SessionStatusKind.Ended, () => rig.Log.ToString());
            Assert.That(rig.View.Last.Cause, Does.Contain("Nakama refused the session"));
            Assert.That(rig.View.Last.CanRetry, Is.True);

            rig.Driver.Dispose();
        }

        [UnityTest]
        public IEnumerator AFailedLogin_ShowsTheFailure()
        {
            var rig = Rig.Create(new ScriptedFactory().Then(GatewayAuthRefuses("invalid token")));
            LogAssert.Expect(LogType.Error, new Regex("FATAL.*invalid token", RegexOptions.Singleline));
            rig.Driver.Start();

            yield return Until(() => rig.View.Last.Kind == SessionStatusKind.Ended, () => rig.Log.ToString());
            Assert.That(rig.Driver.Flow.CurrentPhase, Is.EqualTo(MainSessionFlow.Phase.Failed));
            Assert.That(rig.View.Last.Cause, Does.Contain("invalid token"));

            rig.Driver.Dispose();
        }

        // ── CN-10: dispose ordering ──────────────────────────────────────

        [UnityTest]
        public IEnumerator Dispose_CancelsTheInFlightFlow_AndDisconnectsTheClient()
        {
            var rig = Rig.Create(OneLogin());
            rig.Endpoint.Hold = new TaskCompletionSource<bool>();
            rig.Driver.Start();
            yield return Until(() => rig.Endpoint.Connects == 1, () => "the flow never reached connect");

            rig.Driver.Dispose();

            Assert.That(rig.Endpoint.TokenSeen.IsCancellationRequested, Is.True, "the session token is cancelled");
            yield return Until(
                () => rig.Driver.Flow.CurrentPhase == MainSessionFlow.Phase.Cancelled,
                () => "the flow ended as " + rig.Driver.Flow.CurrentPhase);
            Assert.That(rig.Client.State, Is.EqualTo(NetworkClientState.Ended), "the root-scoped client is not left connected");
        }

        [UnityTest]
        public IEnumerator Dispose_DetachesTheHandlersBeforeItDisconnects()
        {
            var rig = Rig.Create(OneLogin());
            rig.Driver.Start();
            yield return Until(() => rig.Driver.Flow?.CurrentPhase == MainSessionFlow.Phase.InWorld, () => rig.Log.ToString());
            var logged = new List<string>();
            void Capture(string message, string stack, LogType type) => logged.Add(message);
            Application.logMessageReceived += Capture;
            var shownBefore = rig.View.Shown.Count;
            try
            {
                rig.Driver.Dispose();
            }
            finally
            {
                Application.logMessageReceived -= Capture;
            }

            Assert.That(rig.Client.State, Is.EqualTo(NetworkClientState.Ended), "Disconnect() ran");
            Assert.That(logged, Has.None.Contains("State -> Ended"),
                "Disconnect() ended the client while the driver was still subscribed: the handlers must be detached first");
            Assert.That(rig.View.Shown.Count, Is.EqualTo(shownBefore), "the view is not touched during teardown");
        }

        [UnityTest]
        public IEnumerator DisposingTwice_IsHarmless()
        {
            var rig = Rig.Create(OneLogin());
            rig.Driver.Start();
            yield return Until(() => rig.Driver.Flow?.CurrentPhase == MainSessionFlow.Phase.InWorld, () => rig.Log.ToString());

            rig.Driver.Dispose();

            Assert.DoesNotThrow(() => rig.Driver.Dispose());
        }

        private static Func<byte, List<byte[]>> GatewayAuthRefuses(string error) => type =>
            (Cuvara.Netcode.Protocol.MsgType)type == Cuvara.Netcode.Protocol.MsgType.Auth
                ? new List<byte[]>
                {
                    Frame(Cuvara.Netcode.Protocol.MsgType.AuthResp, "{\"ok\":false,\"error\":\"" + error + "\"}"),
                }
                : null;
    }
}
