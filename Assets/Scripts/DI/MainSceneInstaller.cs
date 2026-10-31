namespace Scripts.DI
{
    using VContainer;
    using VContainer.Unity;
#if CUVARA_DOTS && CUVARA_DOTS_VCONTAINER && CUVARA_NETCODE && CUVARA_SHARED_GAMELOGIC
    using Scripts.DI.Dots;
    using UnityEngine;
#endif

    /// <summary>
    /// The registrations of <c>MainSceneScope</c>. Production and <c>ContainerResolutionTests</c>
    /// both call it, on a child of the root container.
    /// </summary>
    public static class MainSceneInstaller
    {
        public static void Install(IContainerBuilder builder)
        {
            // The session: device auth, gateway connect, harness markers. An entry point so the
            // scene carries no object for it and the scope's disposal ends the session.
            builder.RegisterEntryPoint<MainSessionDriver>();

            // Core v3 gameplay surface: content download, inventory commands on the command
            // channel, and the runtime-hosted panel (stat block, statuses, inventory).
            builder.RegisterEntryPoint<GameplayPanelDriver>();

#if CUVARA_DOTS && CUVARA_DOTS_VCONTAINER && CUVARA_NETCODE && CUVARA_SHARED_GAMELOGIC
            // Same pattern, same reason as the root installer's NetworkBootstrap callback: a build
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
