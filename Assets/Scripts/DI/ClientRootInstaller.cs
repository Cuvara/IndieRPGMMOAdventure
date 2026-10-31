namespace Scripts.DI
{
    using Cuvara.Netcode.Bootstrap;
    using Cuvara.Netcode.Client;
    using Cuvara.Netcode.Codec;
    using Cuvara.Netcode.DI;
    using Cuvara.Netcode.Transport;
    using Scripts.Nakama;
    using Scripts.Nakama.DI;
    using UnityEngine;
    using VContainer;
    using VContainer.Unity;
#if CUVARA_DOTS && CUVARA_DOTS_VCONTAINER
    using Scripts.DI.Dots;
#endif

    /// <summary>
    /// The root-scope registrations of <c>GameLifetimeScope</c>, as a function of already-resolved
    /// arguments. Production and <c>ContainerResolutionTests</c> both call it, so the tested
    /// composition cannot drift from the shipped one.
    /// </summary>
    public static class ClientRootInstaller
    {
        public static void Install(IContainerBuilder builder, RootInstallerArgs args)
        {
            var backend = args.Backend.Value;

            // Logs exceptions nobody observed, once, with an [Unobserved] prefix.
            builder.RegisterEntryPoint<UnobservedExceptionReporter>(Lifetime.Singleton);
            builder.RegisterInstance(args.Backend);

            // The encoding is an argument, not a property on NetworkSettings: RegisterNetworking
            // picks the codec from it and registers one IWireCodec. Registering a second one
            // afterwards does not override it — it makes VContainer fail the whole container
            // build with "Conflict implementation type" — so this is the only place it can be
            // decided. Its default in the package is Json, for source compatibility; this client
            // asks for protobuf, because that is what the golden vectors cover and the only
            // encoding a sealed session can use.
            var networkSettings = new NetworkSettings
            {
                GatewayHost = backend.GatewayHost,
                GatewayPort = backend.GatewayPort,
                GatewayUseTls = backend.GatewayTls,
                GatewayTlsPinnedCertificate = args.GatewayPinnedCertificate,
                RequireSealedSession = backend.Sealed,
            };

            // Realtime gameplay is KCP/UDP only. The transport factory is passed explicitly so the
            // KCP datagram key from -cuvara-transport-key / CUVARA_TRANSPORT_KEY reaches the
            // gameplay transport (null = plaintext, dev) together with the gateway's TLS options.
            // Passing it here is the supported substitution (a second ITransportFactory
            // registration fails the container build). It uses only the constructor every pinned
            // netcode version has; from netcode 0.47.0 NetworkSettings.TransportKey carries the
            // same value for RegisterNetworking's own default factory.
            builder.RegisterNetworking(
                networkSettings,
                encoding: backend.EncodingIsProtobuf ? WireEncoding.Protobuf : WireEncoding.Json,
                transports: new DefaultTransportFactory(
                    backend.TransportKey, networkSettings.BuildGatewayTlsOptions()));

            // The single holder of the gameplay input source: the devices by default, a scripted
            // source while a testlab run overrides it. Replaces the static GameplayInput.Source.
            builder.Register<GameplayInputRouter>(Lifetime.Singleton).As<IGameplayInputSource>().AsSelf();

            // The device id is pinned on the settings, not only used once: the auth provider
            // re-authenticates on a cold reconnect and must land on the same account.
            builder.RegisterNakama(new NakamaSettings
            {
                Scheme = backend.NakamaScheme,
                Host = backend.NakamaHost,
                Port = backend.NakamaPort,
                ServerKey = backend.NakamaServerKey,
                // Null unless -cuvara-nakama-tls-cert named a PEM. Null means Unity's own
                // validation, which is right for http and for a CA-issued certificate on
                // https, and which refuses a self-signed one -- see ADR-24.
                PinnedCertificate = args.NakamaPinnedCertificate,
                DeviceId = args.Backend.DeviceId,
            });

            // Core v3 content (stats, statuses, abilities, items), downloaded from the game
            // server's /content once per process and shared by every scene: the HUD names stats
            // by key with it, the inventory names items, the cast key reads the projectile.
            builder.Register<Scripts.Gameplay.Content.GameContentService>(Lifetime.Singleton);

#if CUVARA_DOTS && CUVARA_DOTS_VCONTAINER
            // The DOTS view layer, its MessagePipe brokers, the simulation-model seam and the
            // session predictor. Root-scoped for the same reason RegisterNetworking is: pools and
            // registry outlive scene loads. The per-scene half is DotsWorldBridge, injected by
            // MainSceneScope. viewRoot is the root scope's transform so spawned views live and die
            // with the container that owns their pools.
            builder.RegisterDots(
                viewRoot: args.ViewRoot,
                world: args.World,
                mode: args.Mode,
                library: args.Library,
                loader: args.Loader);
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
