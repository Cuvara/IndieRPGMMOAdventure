namespace Scripts.DI
{
    using VContainer;
    using VContainer.Unity;
#if CUVARA_DOTS && CUVARA_DOTS_VCONTAINER && CUVARA_NETCODE && CUVARA_SHARED_GAMELOGIC
    using Scripts.DI.Dots;
    using UnityEngine;
#endif

    public class MainSceneScope : LifetimeScope
    {
        protected override void Configure(IContainerBuilder builder)
        {
            base.Configure(builder);

#if CUVARA_DOTS && CUVARA_DOTS_VCONTAINER && CUVARA_NETCODE && CUVARA_SHARED_GAMELOGIC
            // Same pattern, same reason as GameLifetimeScope's NetworkBootstrap callback: a build
            // callback injects the component when it is present and is a no-op when it is not,
            // where RegisterComponentInHierarchy resolves eagerly and would throw in any scene
            // without a DotsWorldBridge.
            builder.RegisterBuildCallback(container =>
            {
                var bridge = Object.FindAnyObjectByType<DotsWorldBridge>();
                if (bridge != null)
                {
                    container.Inject(bridge);
                }
            });
#endif
        }
    }
}
