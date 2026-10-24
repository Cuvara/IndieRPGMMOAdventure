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

        public int RpcCalls { get; private set; }

        public int AuthCalls { get; private set; }

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

        public Task<string> SendAsync(string method, Uri uri, IDictionary<string, string> headers, byte[] body,
            int timeout, CancellationToken? cancellationToken)
        {
            var path = uri.AbsolutePath;
            if (path.Contains("/authenticate/device"))
            {
                this.AuthCalls++;
                var jwt = Jwt("user-1");
                return Task.FromResult("{\"token\":\"" + jwt + "\",\"refresh_token\":\"" + jwt + "\"}");
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

        /// <summary>A service over this transport that plays as device <c>dev-1</c> (no restored session).</summary>
        public NakamaSessionService NewService() =>
            new NakamaSessionService(new NakamaSettings { DeviceId = "dev-1" }, this);

        private static string Jwt(string userId)
        {
            var exp = DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeSeconds();
            var payload = "{\"uid\":\"" + userId + "\",\"usn\":\"tester\",\"exp\":" + exp + "}";
            return Segment("{\"alg\":\"HS256\",\"typ\":\"JWT\"}") + "." + Segment(payload) + ".sig";
        }

        private static string Segment(string json) =>
            Convert.ToBase64String(Encoding.UTF8.GetBytes(json)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }
}
