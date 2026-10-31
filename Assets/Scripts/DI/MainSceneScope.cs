namespace Scripts.DI
{
    using VContainer;
    using VContainer.Unity;

    public class MainSceneScope : LifetimeScope
    {
        protected override void Configure(IContainerBuilder builder)
        {
            base.Configure(builder);
            MainSceneInstaller.Install(builder);
        }
    }
}
