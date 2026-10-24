namespace Tests.Editor
{
    using System;
    using System.Collections.Generic;
    using System.Text;
    using System.Threading;
    using Cuvara.Netcode.Auth;
    using Cuvara.Netcode.Client;
    using Cuvara.Netcode.Diagnostics;
    using Cuvara.Netcode.Protocol;
    using Cuvara.Netcode.Transport;
    using Cysharp.Threading.Tasks;
    using NUnit.Framework;

    /// <summary>
    /// A scripted gateway and game server for driving the real <see cref="NetworkClient"/> in
    /// EditMode: each transport the client dials answers from a responder function, and a test
    /// pushes server frames (a kick, an end of stream) with <see cref="RespondingTransport.Deliver"/>.
    /// The shape is the one netcode's own recovery tests use; every await completes synchronously,
    /// so nothing here depends on the editor tick.
    /// </summary>
    internal static class ScriptedNetcode
    {
        public static DateTime Deadline() => DateTime.UtcNow.AddSeconds(10);

        public static NetworkSettings FastSettings() => new NetworkSettings
        {
            JoinAttempts = 1,
            JoinRetryDelay = TimeSpan.Zero,
            RetryJitter = TimeSpan.Zero,
            ReconnectDelay = TimeSpan.FromSeconds(1),
            ReconnectMaxDelay = TimeSpan.FromSeconds(8),
            ReconnectBudget = TimeSpan.FromSeconds(25),
            ReconnectAttempts = 8,
            DelayScheduler = (delay, ct) =>
            {
                ct.ThrowIfCancellationRequested();
                return UniTask.CompletedTask;
            },
            // Heartbeats never fire; the loops park on this forever so no test waits on the editor tick.
            HeartbeatScheduler = (delay, ct) => new UniTaskCompletionSource().Task,
        };

        public static byte[] Frame(MsgType type, string payloadJson) =>
            Encoding.UTF8.GetBytes("{\"type\":" + (int)type + ",\"payload\":" + payloadJson + "}");

        public static byte[] Kick(string reason) => Frame(MsgType.Kick, "{\"reason\":\"" + reason + "\"}");

        public static Func<byte, List<byte[]>> GatewayOk() => type =>
        {
            switch ((MsgType)type)
            {
                case MsgType.Auth:
                    return new List<byte[]> { Frame(MsgType.AuthResp, "{\"ok\":true,\"user_id\":\"u1\"}") };
                case MsgType.EnterWorld:
                    return new List<byte[]>
                    {
                        Frame(MsgType.EnterWorldResp,
                            "{\"server_addr\":\"127.0.0.1:9000\",\"join_token\":\"tok\",\"transport\":\"kcp\"}"),
                    };
                default:
                    return null;
            }
        };

        public static Func<byte, List<byte[]>> GameServerJoinOk() => type =>
            (MsgType)type == MsgType.JoinToken
                ? new List<byte[]> { Frame(MsgType.JoinTokenResp, "{\"ok\":true,\"user_id\":\"u1\",\"tick_rate\":60}") }
                : null;

        public sealed class StubAuth : IAuthProvider
        {
            public int Calls { get; private set; }

            public UniTask<string> GetJwtAsync(CancellationToken ct)
            {
                this.Calls++;
                return UniTask.FromResult("jwt" + this.Calls);
            }
        }

        public sealed class RecordingLog : INetLog
        {
            private readonly List<string> lines = new List<string>();

            public void Info(string message) { lock (this.lines) this.lines.Add("I " + message); }

            public void Warn(string message) { lock (this.lines) this.lines.Add("W " + message); }

            public void Error(string message, Exception exception = null)
            {
                lock (this.lines) this.lines.Add("E " + message + (exception == null ? "" : " :: " + exception.Message));
            }

            public override string ToString() { lock (this.lines) return string.Join("\n", this.lines); }
        }

        public sealed class RespondingTransport : ITransport
        {
            private readonly Func<byte, List<byte[]>> respond;
            private readonly Queue<byte[]> inbound = new Queue<byte[]>();
            private UniTaskCompletionSource<byte[]> parked;

            public RespondingTransport(Func<byte, List<byte[]>> respond) => this.respond = respond;

            public string RemoteEndPoint => "scripted";

            public bool IsConnected => !this.Closed;

            public bool Closed { get; private set; }

            public UniTask ConnectAsync(string host, int port, CancellationToken ct) => UniTask.CompletedTask;

            public UniTask<byte[]> ReadFrameAsync(CancellationToken ct)
            {
                if (this.inbound.Count > 0) return UniTask.FromResult(this.inbound.Dequeue());

                this.parked = new UniTaskCompletionSource<byte[]>();
                var waiting = this.parked;
                ct.Register(() => waiting.TrySetCanceled(ct));
                return waiting.Task;
            }

            public UniTask WriteFrameAsync(byte[] body, CancellationToken ct)
            {
                var responses = this.respond(ReadType(body));
                if (responses != null)
                {
                    foreach (var response in responses) this.Deliver(response);
                }

                return UniTask.CompletedTask;
            }

            /// <summary>The peer half-closes: the parked read returns a clean end of stream.</summary>
            public void Eof() => this.Deliver(null);

            public void Close() => this.Closed = true;

            public void Dispose() => this.Closed = true;

            /// <summary>Pushes a server-initiated frame (a kick, a disconnect).</summary>
            public void Deliver(byte[] frame)
            {
                var waiting = this.parked;
                if (waiting != null)
                {
                    this.parked = null;
                    waiting.TrySetResult(frame);
                }
                else
                {
                    this.inbound.Enqueue(frame);
                }
            }

            private static byte ReadType(byte[] body)
            {
                var json = Encoding.UTF8.GetString(body);
                const string marker = "\"type\":";
                var start = json.IndexOf(marker, StringComparison.Ordinal) + marker.Length;
                var end = start;
                while (end < json.Length && char.IsDigit(json[end])) end++;
                return byte.Parse(json.Substring(start, end - start));
            }
        }

        public sealed class ScriptedFactory : ITransportFactory
        {
            private readonly List<Func<byte, List<byte[]>>> script = new List<Func<byte, List<byte[]>>>();

            public List<RespondingTransport> Created { get; } = new List<RespondingTransport>();

            public ScriptedFactory Then(Func<byte, List<byte[]>> responder)
            {
                this.script.Add(responder);
                return this;
            }

            public ITransport Create(TransportKind kind)
            {
                Assert.That(this.Created.Count, Is.LessThan(this.script.Count),
                    "the flow dialed more connections than the script expected");
                var transport = new RespondingTransport(this.script[this.Created.Count]);
                this.Created.Add(transport);
                return transport;
            }
        }
    }
}
