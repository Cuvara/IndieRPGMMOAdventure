namespace Scripts.DI
{
    using System;
    using System.Threading;
    using System.Threading.Tasks;
    using Cuvara.Netcode.Client;
    using Cysharp.Threading.Tasks;
    using Scripts.Nakama;
    using Scripts.Session;
    using UnityEngine;
    using VContainer.Unity;

    /// <summary>
    /// Backend addresses and the device identity this process plays as, resolved once by
    /// <c>GameLifetimeScope</c> from the command line / <c>CUVARA_*</c> environment and shared
    /// with whatever needs them.
    /// </summary>
    public sealed class BackendSettings
    {
        public BackendCommandLine.Settings Value;

        /// <summary>Device id to authenticate with; null means the machine's own identifier.</summary>
        public string DeviceId;
    }

    /// <summary>
    /// The MainScene session: authenticates the device with Nakama, connects to the map through
    /// the gateway, and reports each step with the markers the multi-client harness reads. Runs
    /// as a VContainer entry point of <c>MainSceneScope</c>, so it starts with the scene and is
    /// cancelled and disconnected when the scope is disposed.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A container entry point rather than a scene component: the scene needs no object added,
    /// the dependencies (<c>NetworkClient</c>, <c>NakamaSessionService</c>, the resolved backend)
    /// arrive by injection instead of <c>FindObjectOfType</c>, and the scope's disposal is the one
    /// place a session end is guaranteed to run. <c>IStartable</c> plus an owned
    /// <see cref="CancellationTokenSource"/> rather than <c>IAsyncStartable</c>, whose return type
    /// depends on VContainer's UniTask-integration define.
    /// </para>
    /// <para>
    /// The sequence itself is <see cref="MainSessionFlow"/> (pure, tested); this class adapts the
    /// real stack to its endpoint seam. Input and presentation are <c>DotsWorldBridge</c>'s and
    /// start on their own once <c>NetworkClient.State</c> reaches <c>InWorld</c> and the bridge's
    /// prewarm completes. Reconnects are the client's policy; the bridge hooks <c>Reconnected</c>.
    /// </para>
    /// </remarks>
    public sealed class MainSessionDriver : IStartable, IDisposable
    {
        private const string DeviceIdPrefix = "mainscene";

        private readonly NetworkClient client;
        private readonly NakamaSessionService nakama;
        private readonly BackendSettings backend;
        private readonly CancellationTokenSource lifetime = new CancellationTokenSource();
        private readonly int instanceId = Interlocked.Increment(ref instances);
        private static int instances;
        private bool disposed;

        public MainSessionDriver(NetworkClient client, NakamaSessionService nakama, BackendSettings backend)
        {
            this.client = client ?? throw new ArgumentNullException(nameof(client));
            this.nakama = nakama ?? throw new ArgumentNullException(nameof(nakama));
            this.backend = backend ?? throw new ArgumentNullException(nameof(backend));
        }

        /// <summary>The sequence this driver ran; null until <see cref="Start"/>.</summary>
        public MainSessionFlow Flow { get; private set; }

        public void Start()
        {
            var settings = this.backend.Value;
            Debug.Log(
                $"{MainSessionFlow.Tag} backend gateway={settings.GatewayHost}:{settings.GatewayPort} " +
                $"nakama={settings.NakamaBaseUrl} map={settings.MapId} device={this.backend.DeviceId ?? "<machine>"}");

            // Probe, kept on purpose: a session that logs "Cancelled" straight after starting is
            // either a token cancelled before its first await or a cancel from somewhere else,
            // and this line plus the one in Dispose tell the two apart from a player log.
            Debug.Log(
                $"{MainSessionFlow.Tag} session driver #{this.instanceId} start: disposed={this.disposed} " +
                $"tokenCancelled={this.lifetime.IsCancellationRequested} clientState={this.client.State}");

            this.client.Reconnected += this.OnReconnected;
            this.client.ReconnectFailed += this.OnReconnectFailed;
            this.client.StateChanged += this.OnStateChanged;

            this.RunAsync(this.lifetime.Token).Forget();
        }

        private async UniTaskVoid RunAsync(CancellationToken cancellationToken)
        {
            this.Flow = new MainSessionFlow();
            await this.Flow.RunAsync(
                new Endpoint(this.client, this.nakama),
                this.backend.DeviceId,
                this.backend.Value.MapId,
                Debug.Log,
                Debug.LogError,
                cancellationToken);
        }

        private void OnStateChanged(NetworkClientState state) => Debug.Log($"{MainSessionFlow.Tag} State -> {state}");

        private void OnReconnected() => Debug.Log($"{MainSessionFlow.Tag} Reconnected as {this.client.UserId}");

        private void OnReconnectFailed(Exception exception) =>
            Debug.LogWarning($"{MainSessionFlow.Tag} Reconnect gave up: {exception?.Message}");

        public void Dispose()
        {
            if (this.disposed) return;
            this.disposed = true;

            Debug.Log(
                $"{MainSessionFlow.Tag} session driver #{this.instanceId} disposed (phase {this.Flow?.CurrentPhase.ToString() ?? "not started"}) at:\n" +
                Environment.StackTrace);

            this.client.Reconnected -= this.OnReconnected;
            this.client.ReconnectFailed -= this.OnReconnectFailed;
            this.client.StateChanged -= this.OnStateChanged;

            this.lifetime.Cancel();
            this.lifetime.Dispose();

            // The scope owned the session; the root-scoped client outlives it and must not keep a
            // dead scene's connection open into the next one.
            this.client.Disconnect();
        }

        /// <summary>Adapts the Nakama service and the netcode client to the flow's seam.</summary>
        private sealed class Endpoint : MainSessionFlow.IEndpoint
        {
            private readonly NetworkClient client;
            private readonly NakamaSessionService nakama;

            public Endpoint(NetworkClient client, NakamaSessionService nakama)
            {
                this.client = client;
                this.nakama = nakama;
            }

            public string UserId => this.client.UserId;

            public async Task<string> AuthenticateDeviceAsync(string deviceId, CancellationToken cancellationToken)
            {
                // Explicit device auth first, so the IAuthProvider the client resolves finds a
                // valid session for THIS device and only mints the gateway token.
                var session = await this.nakama.AuthenticateDeviceAsync(deviceId, cancellationToken);
                return session.UserId;
            }

            public async Task ConnectAsync(string mapId, CancellationToken cancellationToken)
            {
                // The provider overload: the registered NakamaAuthProvider turns the session into
                // a gateway JWT, and the client keeps the provider for its own reconnects.
                await this.client.ConnectAsync(mapId, cancellationToken);
            }
        }
    }
}
