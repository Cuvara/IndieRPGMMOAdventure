namespace Tests.Editor
{
    using System;
    using System.Collections;
    using System.Collections.Generic;
    using System.IO;
    using System.Net.Http;
    using System.Net.Sockets;
    using System.Threading;
    using System.Threading.Tasks;
    using Cuvara.Netcode.Client;
    using Cuvara.Netcode.Codec;
    using Cysharp.Threading.Tasks;
    using global::Nakama;
    using NUnit.Framework;
    using Scripts.Nakama.Auth;
    using static Tests.Editor.ScriptedNetcode;
    using UnityEngine;
    using UnityEngine.TestTools;

    /// <summary>
    /// How <see cref="NakamaAuthProvider"/> reports a failed <c>gateway_token</c> RPC, judged
    /// by the one function netcode's reconnect loop asks:
    /// <see cref="ReconnectPolicy.IsPermanentFailure"/>. A permanent failure ends the loop after
    /// one attempt with 30 s of entity hold still open (audit CN-01); a transient one lets the
    /// next round try again. Nakama refusing the player is permanent; a network that has not
    /// come back yet is not.
    /// </summary>
    public sealed class NakamaAuthProviderTests
    {
        private static Exception Fails(NakamaAuthProvider provider, CancellationToken ct = default)
        {
            try
            {
                provider.GetJwtAsync(ct).GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                return ex;
            }

            Assert.Fail("GetJwtAsync was expected to fail and returned a token");
            return null;
        }

        private static NakamaAuthProvider ProviderOver(FakeNakamaHttp http) =>
            new NakamaAuthProvider(http.NewService());

        private static IEnumerable<TestCaseData> TransientFailures()
        {
            yield return new TestCaseData(new HttpRequestException("network down")).SetName("HttpRequestException");
            yield return new TestCaseData(new TimeoutException("rpc timed out")).SetName("TimeoutException");
            yield return new TestCaseData(new IOException("connection reset")).SetName("IOException");
            yield return new TestCaseData(new SocketException(10060)).SetName("SocketException");
            // A timeout surfaces as a cancellation the caller did not ask for.
            yield return new TestCaseData(new TaskCanceledException("timed out")).SetName("TaskCanceledException");
            yield return new TestCaseData(new ApiResponseException(500, "boom", -1)).SetName("HTTP 500");
            yield return new TestCaseData(new ApiResponseException(503, "unavailable", -1)).SetName("HTTP 503");
            yield return new TestCaseData(new ApiResponseException(429, "slow down", -1)).SetName("HTTP 429");
            yield return new TestCaseData(new ApiResponseException("connection refused")).SetName("no HTTP status");
        }

        [TestCaseSource(nameof(TransientFailures))]
        public void ATransientRpcFailure_IsNotPermanent(Exception failure)
        {
            var provider = ProviderOver(new FakeNakamaHttp().ThenFail(failure));

            var thrown = Fails(provider);

            Assert.That(ReconnectPolicy.IsPermanentFailure(thrown), Is.False,
                "the reconnect loop must try again; got " + thrown);
            Assert.That(thrown, Is.Not.InstanceOf<OperationCanceledException>(),
                "an OperationCanceledException makes the reconnect loop exit silently, with no state change");
        }

        [TestCase(401)]
        [TestCase(403)]
        public void ARefusal_IsPermanent(int status)
        {
            var provider = ProviderOver(new FakeNakamaHttp().ThenFail(new ApiResponseException(status, "no", 16)));

            var thrown = Fails(provider);

            Assert.That(ReconnectPolicy.IsPermanentFailure(thrown), Is.True);
            Assert.That(thrown.InnerException, Is.InstanceOf<ApiResponseException>());
        }

        [TestCase("")]
        [TestCase("not json")]
        [TestCase("{}")]
        public void AnInvalidOrEmptyPayload_IsPermanent(string payload)
        {
            var provider = ProviderOver(new FakeNakamaHttp().ThenPayload(payload));

            Assert.That(ReconnectPolicy.IsPermanentFailure(Fails(provider)), Is.True);
        }

        [Test]
        public void ACancelFromTheCallersToken_StaysACancel()
        {
            using (var cts = new CancellationTokenSource())
            {
                var http = new FakeNakamaHttp().Then(() =>
                {
                    cts.Cancel();
                    return Task.FromException<string>(new TaskCanceledException());
                });
                var provider = ProviderOver(http);

                Assert.That(Fails(provider, cts.Token), Is.InstanceOf<OperationCanceledException>());
            }
        }

        [Test]
        public void AfterATransientFailure_TheNextCallReturnsTheToken()
        {
            var http = new FakeNakamaHttp().ThenFail(new HttpRequestException("down")).ThenToken("jwt-2");
            var provider = ProviderOver(http);

            Fails(provider);
            var token = provider.GetJwtAsync(CancellationToken.None).GetAwaiter().GetResult();

            Assert.That(token, Is.EqualTo("jwt-2"));
            Assert.That(http.RpcCalls, Is.EqualTo(2));
        }

        // ── the sign-in that precedes the RPC (restore, refresh, device auth) ──

        private const string AuthTokenKey = "nakama.auth_token";
        private const string RefreshTokenKey = "nakama.refresh_token";
        private string savedAuthToken;
        private string savedRefreshToken;

        [SetUp]
        public void SaveSessionPrefs()
        {
            // The restore path reads PlayerPrefs; leave the developer's own persisted session alone.
            this.savedAuthToken = PlayerPrefs.GetString(AuthTokenKey, null);
            this.savedRefreshToken = PlayerPrefs.GetString(RefreshTokenKey, null);
            PlayerPrefs.DeleteKey(AuthTokenKey);
            PlayerPrefs.DeleteKey(RefreshTokenKey);
        }

        [TearDown]
        public void RestoreSessionPrefs()
        {
            PlayerPrefs.DeleteKey(AuthTokenKey);
            PlayerPrefs.DeleteKey(RefreshTokenKey);
            if (!string.IsNullOrEmpty(this.savedAuthToken)) PlayerPrefs.SetString(AuthTokenKey, this.savedAuthToken);
            if (!string.IsNullOrEmpty(this.savedRefreshToken)) PlayerPrefs.SetString(RefreshTokenKey, this.savedRefreshToken);
        }

        /// <summary>A persisted session whose auth token expired an hour ago and whose refresh token is still good.</summary>
        private static void PersistExpiredSession()
        {
            PlayerPrefs.SetString(AuthTokenKey, FakeNakamaHttp.Jwt("user-1", TimeSpan.FromHours(-1)));
            PlayerPrefs.SetString(RefreshTokenKey, FakeNakamaHttp.Jwt("user-1", TimeSpan.FromHours(24)));
        }

        [TestCase(401)]
        [TestCase(403)]
        public void ARefusedDeviceSignIn_IsPermanent(int status)
        {
            var http = new FakeNakamaHttp().ThenAuthFail(new ApiResponseException(status, "no", 16));

            var thrown = Fails(ProviderOver(http));

            Assert.That(ReconnectPolicy.IsPermanentFailure(thrown), Is.True, thrown.ToString());
            Assert.That(http.RpcCalls, Is.EqualTo(0), "no RPC after a refused sign-in");
        }

        [TestCaseSource(nameof(TransientFailures))]
        public void ATransientDeviceSignInFailure_IsNotPermanent_AndTheNextCallSucceeds(Exception failure)
        {
            var http = new FakeNakamaHttp().ThenAuthFail(failure).ThenToken("jwt-2");
            var provider = ProviderOver(http);

            var thrown = Fails(provider);

            Assert.That(ReconnectPolicy.IsPermanentFailure(thrown), Is.False, thrown.ToString());
            Assert.That(thrown, Is.Not.InstanceOf<OperationCanceledException>());
            Assert.That(provider.GetJwtAsync(CancellationToken.None).GetAwaiter().GetResult(), Is.EqualTo("jwt-2"));
        }

        [TestCase(401)]
        [TestCase(403)]
        public void AnExpiredSession_WhoseRefreshAndDeviceSignInAreRefused_IsPermanent(int status)
        {
            PersistExpiredSession();
            var http = new FakeNakamaHttp()
                .ThenRefreshFail(new ApiResponseException(401, "refresh token invalid", 16))
                .ThenAuthFail(new ApiResponseException(status, "no", 16));
            var provider = new NakamaAuthProvider(http.NewRestoringService());

            var thrown = Fails(provider);

            Assert.That(http.RefreshCalls, Is.EqualTo(1), "the refresh was tried first");
            Assert.That(http.AuthCalls, Is.EqualTo(1), "then the fall back to device sign-in");
            Assert.That(ReconnectPolicy.IsPermanentFailure(thrown), Is.True, thrown.ToString());
        }

        [Test]
        public void AnExpiredSession_WhoseRefreshAndDeviceSignInFailInTransit_IsNotPermanent()
        {
            PersistExpiredSession();
            var http = new FakeNakamaHttp()
                .ThenRefreshFail(new ApiResponseException(503, "unavailable", -1))
                .ThenAuthFail(new ApiResponseException(503, "unavailable", -1));
            var provider = new NakamaAuthProvider(http.NewRestoringService());

            var thrown = Fails(provider);

            Assert.That(ReconnectPolicy.IsPermanentFailure(thrown), Is.False, thrown.ToString());
        }

        [Test]
        public void AnExpiredSession_WhoseRefreshHitsADeadNetwork_IsNotPermanent()
        {
            PersistExpiredSession();
            var http = new FakeNakamaHttp().ThenRefreshFail(new HttpRequestException("network down"));
            var provider = new NakamaAuthProvider(http.NewRestoringService());

            var thrown = Fails(provider);

            Assert.That(ReconnectPolicy.IsPermanentFailure(thrown), Is.False, thrown.ToString());
            Assert.That(thrown, Is.Not.InstanceOf<OperationCanceledException>());
        }

        [Test]
        public void AnExpiredSession_RefusedOnRefreshButAcceptedOnDeviceSignIn_StillGetsAToken()
        {
            PersistExpiredSession();
            var http = new FakeNakamaHttp()
                .ThenRefreshFail(new ApiResponseException(401, "refresh token invalid", 16))
                .ThenToken("jwt-3");
            var provider = new NakamaAuthProvider(http.NewRestoringService());

            Assert.That(provider.GetJwtAsync(CancellationToken.None).GetAwaiter().GetResult(), Is.EqualTo("jwt-3"));
        }

        [Test]
        public void ACallerCancelDuringSignIn_StaysACancel()
        {
            using (var cts = new CancellationTokenSource())
            {
                var http = new FakeNakamaHttp();
                http.ThenAuthFail(new TaskCanceledException());
                cts.Cancel();

                Assert.That(Fails(ProviderOver(http), cts.Token), Is.InstanceOf<OperationCanceledException>());
            }
        }

        [Test]
        public void ACancelNobodyAskedFor_DuringSignIn_IsRetriedNotTakenForTheUserLeaving()
        {
            // The SDK reports a timeout, and a login superseded by a newer one, as a plain
            // OperationCanceledException. The reconnect loop treats that type as "the user left" and
            // returns with no state change, so the provider must not pass it on.
            var http = new FakeNakamaHttp().ThenAuthFail(new OperationCanceledException("superseded or timed out"));

            var thrown = Fails(ProviderOver(http));

            Assert.That(thrown, Is.Not.InstanceOf<OperationCanceledException>());
            Assert.That(ReconnectPolicy.IsPermanentFailure(thrown), Is.False);
        }

        // ── the reconnect loop itself ────────────────────────────────────

        [UnityTest]
        public IEnumerator ReconnectSurvivesATransientNakamaFailure_AndLandsOnTheSecondAttempt()
        {
            var http = new FakeNakamaHttp()
                .ThenToken("jwt-1")                                     // the first login
                .ThenFail(new HttpRequestException("Wi-Fi hand-off"))   // reconnect round 1: network still down
                .ThenToken("jwt-2");                                    // reconnect round 2
            var factory = new ScriptedFactory()
                .Then(GatewayOk()).Then(GameServerJoinOk())             // first login
                .Then(GatewayOk()).Then(GameServerJoinOk());            // the round that lands
            var log = new RecordingLog();
            var client = new NetworkClient(
                FastSettings(), factory, new JsonWireCodec(), log, new NakamaAuthProvider(http.NewService()));
            var attempts = new List<int>();
            Exception failed = null;
            var reconnected = 0;
            client.ReconnectAttemptStarted += a => attempts.Add(a);
            client.ReconnectFailed += ex => failed = ex;
            client.Reconnected += () => reconnected++;

            var done = false;
            Exception connectFailure = null;
            Join(client).ContinueWith(ex => { done = true; connectFailure = ex; }).Forget();
            for (var t = Deadline(); !done && DateTime.UtcNow < t;) yield return null;
            Assert.That(connectFailure, Is.Null, connectFailure?.ToString() + "\n" + log);
            Assert.That(client.State, Is.EqualTo(NetworkClientState.InWorld));

            factory.Created[1].Eof(); // the network drops

            for (var t = Deadline(); reconnected == 0 && failed == null && DateTime.UtcNow < t;) yield return null;

            Assert.That(failed, Is.Null, "ReconnectFailed fired: " + failed + "\n" + log);
            Assert.That(reconnected, Is.EqualTo(1), log.ToString());
            Assert.That(attempts, Is.EqualTo(new[] { 1, 2 }), "the loop must try again after a transient failure");
            Assert.That(client.State, Is.EqualTo(NetworkClientState.InWorld));
            client.Dispose();
        }

        [UnityTest]
        public IEnumerator ReconnectGivesUpAtOnce_WhenNakamaRefusesThePlayer()
        {
            var http = new FakeNakamaHttp()
                .ThenToken("jwt-1")
                .ThenFail(new ApiResponseException(401, "token invalid", 16));
            var factory = new ScriptedFactory()
                .Then(GatewayOk()).Then(GameServerJoinOk());
            var log = new RecordingLog();
            var client = new NetworkClient(
                FastSettings(), factory, new JsonWireCodec(), log, new NakamaAuthProvider(http.NewService()));
            var attempts = new List<int>();
            Exception failed = null;
            client.ReconnectAttemptStarted += a => attempts.Add(a);
            client.ReconnectFailed += ex => failed = ex;

            var done = false;
            Exception connectFailure = null;
            Join(client).ContinueWith(ex => { done = true; connectFailure = ex; }).Forget();
            for (var t = Deadline(); !done && DateTime.UtcNow < t;) yield return null;
            Assert.That(connectFailure, Is.Null, connectFailure?.ToString() + "\n" + log);

            factory.Created[1].Eof();

            for (var t = Deadline(); failed == null && DateTime.UtcNow < t;) yield return null;

            Assert.That(failed, Is.InstanceOf<ReconnectExhaustedException>(), log.ToString());
            Assert.That(attempts, Is.EqualTo(new[] { 1 }), "a real refusal is not retried");
            Assert.That(client.State, Is.EqualTo(NetworkClientState.Ended));
            client.Dispose();
        }

        private static async UniTask<Exception> Join(NetworkClient client)
        {
            try
            {
                await client.ConnectAsync("map_01", CancellationToken.None);
                return null;
            }
            catch (Exception ex)
            {
                return ex;
            }
        }
    }
}
