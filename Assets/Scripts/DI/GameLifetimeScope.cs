namespace Scripts.DI
{
    using Cuvara.Netcode.Bootstrap;
    using Cuvara.Netcode.Client;
    using Cuvara.Netcode.DI;
    using Scripts.Nakama;
    using Scripts.Nakama.DI;
    using Scripts.Session;
    using UnityEngine;
    using VContainer;
    using VContainer.Unity;
#if CUVARA_DOTS && CUVARA_DOTS_VCONTAINER
    using Scripts.DI.Dots;
#endif

    public class GameLifetimeScope : LifetimeScope
    {
        protected override void Configure(IContainerBuilder builder)
        {
            base.Configure(builder);

            // Backend addresses come from the command line / CUVARA_* environment — the flags
            // Tools/run-clients.sh passes — with localhost defaults for a plain Editor run. Resolved
            // once, here, because NetworkSettings and NakamaSettings are container instances and
            // the addresses have to be known before the client and the Nakama service exist.
            var backend = BackendCommandLine.Resolve("127.0.0.1", 8000, "map_01", "http://127.0.0.1:9101/status");
            var deviceId = BackendCommandLine.ResolveDeviceIdOrNull(backend, "mainscene");
            builder.RegisterInstance(new BackendSettings { Value = backend, DeviceId = deviceId });

            TransportSecurityReport.Warn(backend);

            builder.RegisterNetworking(new NetworkSettings
            {
                GatewayHost = backend.GatewayHost,
                GatewayPort = backend.GatewayPort,
                GatewayUseTls = backend.GatewayTls,
                GatewayTlsPinnedCertificate = TransportSecurityReport.LoadPinOrNull(backend),
            });

            // The device id is pinned on the settings, not only used once: the auth provider
            // re-authenticates on a cold reconnect and must land on the same account.
            builder.RegisterNakama(new NakamaSettings
            {
                Scheme = backend.NakamaScheme,
                Host = backend.NakamaHost,
                Port = backend.NakamaPort,
                ServerKey = backend.NakamaServerKey,
                DeviceId = deviceId,
            });

#if CUVARA_DOTS && CUVARA_DOTS_VCONTAINER
            // The DOTS view layer, its MessagePipe brokers, the simulation-model seam and the
            // session predictor. Root-scoped for the same reason RegisterNetworking is: pools and
            // registry outlive scene loads. The per-scene half is DotsWorldBridge, injected by
            // MainSceneScope. viewRoot is this scope's transform so spawned views live and die
            // with the container that owns their pools.
            builder.RegisterDots(viewRoot: transform);
#endif

            // Registering the services is not enough to inject them. VContainer only
            // injects components it has been told about, so without this NetworkBootstrap
            // never receives the container, reports "no container found", builds its own
            // NetworkClient, and falls back to minting a development JWT — silently
            // bypassing the NakamaAuthProvider registered just above.
            //
            // Done as a build callback rather than RegisterComponentInHierarchy because
            // that resolves eagerly and THROWS when the component is absent, which would
            // break every scene that does not host a NetworkBootstrap — the loading
            // scene, a menu, a test scene. Injecting on build is a no-op when there is
            // nothing to inject.
            builder.RegisterBuildCallback(container =>
            {
                var bootstrap = Object.FindAnyObjectByType<NetworkBootstrap>();
                if (bootstrap != null)
                {
                    container.Inject(bootstrap);
                }
            });
        }
    }
}