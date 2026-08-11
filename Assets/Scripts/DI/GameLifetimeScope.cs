namespace Scripts.DI
{
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
        }
    }
}