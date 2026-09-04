namespace Scripts.DI
{
    using Cuvara.Netcode.Bootstrap;
    using Cuvara.Netcode.DI;
    using Scripts.Nakama.DI;
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
            builder.RegisterNetworking();
            builder.RegisterNakama();

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