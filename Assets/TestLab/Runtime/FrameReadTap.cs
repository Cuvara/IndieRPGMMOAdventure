namespace TestLab
{
    using System;
    using System.Reflection;
    using System.Threading;
    using Cuvara.Netcode.Client;
    using Cuvara.Netcode.Transport;
    using Cysharp.Threading.Tasks;

    /// <summary>
    /// Sees every gameplay frame body as the KCP transport hands it up: its size and the moment it
    /// was read. Test-only; the production client has no such hook.
    /// </summary>
    /// <remarks>
    /// <para><b>Why reflection.</b> Netcode exposes neither a per-frame byte count nor a receive
    /// timestamp (<c>WireFrame</c> carries type, payload and encoding only;
    /// <c>INetworkMetrics.RecordBytes</c> has no caller). The client hands its
    /// <see cref="ITransportFactory"/> to <c>RegisterNetworking</c>, and <see cref="NetworkClient"/>
    /// keeps it in a private field and passes it to every <c>GameSessionClient</c> it creates. This
    /// replaces that field's value with a factory that wraps the KCP transport, so the next join (and
    /// every reconnect after it) reads through <see cref="TappedTransport"/>. Nothing in a production
    /// assembly changes, and a player without <c>CUVARA_TESTLAB</c> does not contain this type.</para>
    /// <para><b>What a byte count here is.</b> One frame body as returned by
    /// <see cref="ITransport.ReadFrameAsync"/>: after KCP reassembly and the transport key's
    /// AES-CFB, before the sealed session is opened and before decoding; without the 4-byte length
    /// prefix and without KCP/UDP headers.</para>
    /// <para><b>Side effect, accepted.</b> <c>GameSessionClient.DescribeJoinTimeout</c> recognises
    /// a <c>KcpTransport</c> by type to say "no UDP datagram came back"; through the wrapper a
    /// join timeout uses its generic message instead. Nothing else in netcode tests the transport's
    /// type.</para>
    /// <para><b>Leave flush forwarded.</b> From netcode 0.48.0 the KCP transport is an
    /// <c>IFlushableTransport</c>, and a graceful leave waits for its send queue to be acknowledged
    /// only through that interface. The wrapper implements it and forwards to the KCP transport, so a
    /// testlab <c>quit</c> leaves exactly as the shipping client does.</para>
    /// <para><b>Threading.</b> The read loop's continuations run on the PlayerLoop (the KCP pump is
    /// <c>UniTask.Delay(..., PlayerLoopTiming.Update)</c>), and the frame is decoded, resolved and
    /// merged synchronously after the read, so the last read when a snapshot is published is that
    /// snapshot's frame. The fields are still written with <see cref="Volatile"/> so a reader never
    /// sees a torn sequence number.</para>
    /// </remarks>
    public sealed class FrameReadTap
    {
        private long reads;
        private long lastReadUs = -1;
        private int lastBytes = -1;
        private TappedTransport current;
        private int tappedSessions;

        /// <summary>Gameplay transports created through the tap (one per join or reconnect).</summary>
        public int TappedSessions => Volatile.Read(ref this.tappedSessions);

        /// <summary>Frames read through the tap since it was installed.</summary>
        public long Reads => Volatile.Read(ref this.reads);

        /// <summary><see cref="LabClock"/> time of the last frame read, or -1.</summary>
        public long LastReadUs => Volatile.Read(ref this.lastReadUs);

        /// <summary>Body length of the last frame read, or -1.</summary>
        public int LastBytes => Volatile.Read(ref this.lastBytes);

        /// <summary>
        /// Simulates a dead link on the current gameplay session: closes the KCP transport and
        /// makes its pending read fail with a <see cref="TransportException"/>. Netcode sees a
        /// <c>TransportError</c> close, which <c>ReconnectPolicy</c> answers with its own automatic
        /// reconnect - the production recovery path, run unchanged. Returns false when the current
        /// session was not opened through the tap.
        /// </summary>
        /// <summary>
        /// The current session's KCP transport, or null. Netcode 0.48.0's receive counters
        /// (<c>GameSessionClient.TransportDatagramsRead</c> and friends) find it with
        /// <c>_transport as KcpTransport</c>, which the wrapper defeats - they read 0 in a testlab
        /// player - so the runner reads the counters here instead.
        /// </summary>
        public KcpTransport CurrentKcp => Volatile.Read(ref this.current)?.Inner as KcpTransport;

        public bool DropLink()
        {
            var transport = Volatile.Read(ref this.current);
            if (transport == null || !transport.IsConnected)
            {
                return false;
            }

            transport.Drop();
            return true;
        }

        internal void OnFrameRead(int bytes)
        {
            Volatile.Write(ref this.lastReadUs, LabClock.NowUs());
            Volatile.Write(ref this.lastBytes, bytes);
            Interlocked.Increment(ref this.reads);
        }

        /// <summary>
        /// Wraps <paramref name="client"/>'s transport factory. Returns <c>null</c> on success, else
        /// why it could not (the caller records it and the byte column stays empty).
        /// </summary>
        public string TryInstall(NetworkClient client)
        {
            FieldInfo field = null;
            foreach (var f in typeof(NetworkClient).GetFields(BindingFlags.Instance | BindingFlags.NonPublic))
            {
                if (f.FieldType == typeof(ITransportFactory))
                {
                    field = f;
                    break;
                }
            }

            if (field == null)
            {
                return "NetworkClient has no ITransportFactory field";
            }

            if (!(field.GetValue(client) is ITransportFactory inner))
            {
                return "NetworkClient's transport factory is null";
            }

            if (inner is TappedFactory)
            {
                return null;
            }

            field.SetValue(client, new TappedFactory(inner, this));
            return null;
        }

        private sealed class TappedFactory : ITransportFactory
        {
            private readonly ITransportFactory inner;
            private readonly FrameReadTap tap;

            public TappedFactory(ITransportFactory inner, FrameReadTap tap)
            {
                this.inner = inner;
                this.tap = tap;
            }

            public ITransport Create(TransportKind kind)
            {
                var transport = this.inner.Create(kind);
                // Gameplay only: the gateway hop carries no snapshots.
                if (kind != TransportKind.Kcp)
                {
                    return transport;
                }

                var tapped = new TappedTransport(transport, this.tap);
                Volatile.Write(ref this.tap.current, tapped);
                Interlocked.Increment(ref this.tap.tappedSessions);
                return tapped;
            }
        }

        // IFlushableTransport (netcode 0.48.0): WireConnection's graceful leave waits for the KCP
        // send queue to be acknowledged only when the transport says it can (`is
        // IFlushableTransport`). A wrapper that hid the interface would turn every testlab leave back
        // into the race 0.48.0 fixed - the disconnect frame queued, the socket closed - so the
        // wrapper forwards it.
        private sealed class TappedTransport : ITransport, IFlushableTransport
        {
            private readonly ITransport inner;

            internal ITransport Inner => this.inner;
            private readonly FrameReadTap tap;
            private int dropped;

            public TappedTransport(ITransport inner, FrameReadTap tap)
            {
                this.inner = inner;
                this.tap = tap;
            }

            public string RemoteEndPoint => this.inner.RemoteEndPoint;

            public bool IsConnected => this.inner.IsConnected;

            public UniTask ConnectAsync(string host, int port, CancellationToken cancellationToken) =>
                this.inner.ConnectAsync(host, port, cancellationToken);

            public async UniTask<byte[]> ReadFrameAsync(CancellationToken cancellationToken)
            {
                var body = await this.inner.ReadFrameAsync(cancellationToken);
                if (Volatile.Read(ref this.dropped) != 0)
                {
                    throw new TransportException("testlab: gameplay link dropped on purpose (plan action 'reconnect')");
                }

                if (body != null)
                {
                    this.tap.OnFrameRead(body.Length);
                }

                return body;
            }

            public UniTask WriteFrameAsync(byte[] body, CancellationToken cancellationToken) =>
                this.inner.WriteFrameAsync(body, cancellationToken);

            public void Close() => this.inner.Close();

            public UniTask<bool> FlushAsync(CancellationToken cancellationToken) =>
                this.inner is IFlushableTransport flushable
                    ? flushable.FlushAsync(cancellationToken)
                    : UniTask.FromResult(true);

            internal void Drop()
            {
                Volatile.Write(ref this.dropped, 1);
                this.inner.Close();
            }

            public void Dispose() => this.inner.Dispose();
        }
    }
}
