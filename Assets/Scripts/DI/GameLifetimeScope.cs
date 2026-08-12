namespace Scripts.DI
{
    using Cuvara.Netcode.Bootstrap;
    using Cuvara.Netcode.DI;
    using Scripts.Nakama.DI;
    using VContainer;
    using VContainer.Unity;

    public class GameLifetimeScope : LifetimeScope
    {
        protected override void Configure(IContainerBuilder builder)
        {
            base.Configure(builder);
            builder.RegisterNetworking();
            builder.RegisterNakama();

            // Registering the services is not enough to inject them. VContainer only
            // injects components it has been told about, so without this NetworkBootstrap
            // never receives the container, reports "no container found", builds its own
            // NetworkClient, and falls back to minting a development JWT — silently
            // bypassing the NakamaAuthProvider registered just above. Optional so a scene
            // without the component still builds a valid container.
            builder.RegisterComponentInHierarchy<NetworkBootstrap>().AsSelf();
        }
    }
}