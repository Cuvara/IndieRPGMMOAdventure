namespace Tests.Editor
{
    using System;
    using System.Collections.Generic;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;
    using global::Nakama;
    using Scripts.Nakama;

    /// <summary>
    /// Nakama's HTTP transport, scripted: device authentication succeeds with a real-looking
    /// session, and each <c>gateway_token</c> RPC takes the next scripted outcome. Everything
    /// above the transport is the production SDK client, so the exceptions a test provokes are
    /// the ones the SDK raises (<see cref="ApiResponseException"/> with its status code), not
    /// the test's idea of them.
    /// </summary>
    internal sealed class FakeNakamaHttp : IHttpAdapter
    {
        private readonly Queue<Func<Task<string>>> rpcOutcomes = new Queue<Func<Task<string>>>();
        private readonly Queue<Func<Task<string>>> authOutcomes = new Queue<Func<Task<string>>>();
        private readonly Queue<Func<Task<string>>> refreshOutcomes = new Queue<Func<Task<string>>>();

        public int RpcCalls { get; private set; }

        public int AuthCalls { get; private set; }

        public int RefreshCalls { get; private set; }

        public ILogger Logger { get; set; }

        public TransientExceptionDelegate TransientExceptionDelegate => _ => false;

        /// <summary>The RPC answers with <paramref name="token"/>.</summary>
        public FakeNakamaHttp ThenToken(string token) =>
            this.Then(() => Task.FromResult("{\"payload\":\"{\\\"token\\\":\\\"" + token + "\\\"}\"}"));

        /// <summary>The RPC answers with a raw payload string, escaped into the envelope here.</summary>
        public FakeNakamaHttp ThenPayload(string payload) =>
            this.Then(() => Task.FromResult(
                "{\"payload\":\"" + payload.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"}"));

        /// <summary>The RPC fails with <paramref name="failure"/>.</summary>
        public FakeNakamaHttp ThenFail(Exception failure) =>
            this.Then(() => Task.FromException<string>(failure));

        public FakeNakamaHttp Then(Func<Task<string>> outcome)
        {
            this.rpcOutcomes.Enqueue(outcome);
            return this;
        }

        /// <summary>The next device authentication fails with <paramref name="failure"/> (later ones succeed).</summary>
        public FakeNakamaHttp ThenAuthFail(Exception failure)
        {
            this.authOutcomes.Enqueue(() => Task.FromException<string>(failure));
            return this;
        }

        /// <summary>The next session refresh fails with <paramref name="failure"/>.</summary>
        public FakeNakamaHttp ThenRefreshFail(Exception failure)
        {
            this.refreshOutcomes.Enqueue(() => Task.FromException<string>(failure));
            return this;
        }

        public Task<string> SendAsync(string method, Uri uri, IDictionary<string, string> headers, byte[] body,
            int timeout, CancellationToken? cancellationToken)
        {
            var path = uri.AbsolutePath;
            if (path.Contains("/authenticate/device"))
            {
                this.AuthCalls++;
                if (this.authOutcomes.Count > 0) return this.authOutcomes.Dequeue()();
                var jwt = Jwt("user-1", TimeSpan.FromHours(1));
                return Task.FromResult("{\"token\":\"" + jwt + "\",\"refresh_token\":\"" + jwt + "\"}");
            }

            if (path.Contains("/session/refresh"))
            {
                this.RefreshCalls++;
                if (this.refreshOutcomes.Count == 0)
                {
                    throw new InvalidOperationException("the test scripted no session refresh outcome");
                }

                return this.refreshOutcomes.Dequeue()();
            }

            if (path.Contains("/rpc/gateway_token"))
            {
                this.RpcCalls++;
                if (this.rpcOutcomes.Count == 0)
                {
                    throw new InvalidOperationException("the test scripted no further gateway_token outcome");
                }

                return this.rpcOutcomes.Dequeue()();
            }

            throw new InvalidOperationException("unscripted Nakama request: " + method + " " + uri);
        }

        /// <summary>A service over this transport with no pinned device id, so it restores a persisted session.</summary>
        public NakamaSessionService NewRestoringService() =>
            new NakamaSessionService(new NakamaSettings(), this);

        /// <summary>A service over this transport that plays as device <c>dev-1</c> (no restored session).</summary>
        public NakamaSessionService NewService() =>
            new NakamaSessionService(new NakamaSettings { DeviceId = "dev-1" }, this);

        /// <summary>A session token for <paramref name="userId"/> that expires in <paramref name="lifetime"/> (negative: already expired).</summary>
        public static string Jwt(string userId, TimeSpan lifetime)
        {
            var exp = DateTimeOffset.UtcNow.Add(lifetime).ToUnixTimeSeconds();
            var payload = "{\"uid\":\"" + userId + "\",\"usn\":\"tester\",\"exp\":" + exp + "}";
            return Segment("{\"alg\":\"HS256\",\"typ\":\"JWT\"}") + "." + Segment(payload) + ".sig";
        }

        private static string Segment(string json) =>
            Convert.ToBase64String(Encoding.UTF8.GetBytes(json)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }
}
