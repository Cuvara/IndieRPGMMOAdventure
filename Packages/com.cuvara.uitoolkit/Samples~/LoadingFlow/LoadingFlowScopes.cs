namespace Cuvara.UIToolkit.Samples.LoadingFlow
{
    using Cuvara.UIToolkit.Core;
    using Cuvara.UIToolkit.Flow;
    using Cuvara.UIToolkit.VContainer;
    using global::VContainer;
    using global::VContainer.Unity;

    /// <summary>Loading scene no longer uses the navigator — LoadingScreenDriver (MonoBehaviour)
    /// drives the progress directly. This scope is kept for reference but is not on the scene.</summary>
    public class LoadingFlowLoadingScope : LifetimeScope
    {
        protected override void Configure(IContainerBuilder builder) { }
    }

    public class LoadingFlowMainScope : LifetimeScope
    {
        protected override void Configure(IContainerBuilder builder)
        {
            builder.RegisterUIToolkit();
            builder.RegisterInstance<IVisualTreeAssetLoader>(new ResourceAssetLoader());
            builder.RegisterScreenFlow();

            // Full screens
            builder.RegisterScreen<MainScreenPresenter, MainScreenView>("MainScreen");
            builder.RegisterScreen<SecondScreenPresenter, SecondScreenView>("SecondScreen");

            // Modal popups (overlay layer, dims below, tap-outside-to-close)
            builder.RegisterPopup<InfoPopupPresenter, InfoPopupView>("InfoPopup",
                ScreenOptions.Modal | ScreenOptions.DimsBelow | ScreenOptions.CloseOnTapOutside);
            builder.RegisterPopup<ConfirmPopupPresenter, ConfirmPopupView>("ConfirmPopup",
                ScreenOptions.Modal | ScreenOptions.DimsBelow | ScreenOptions.CloseOnTapOutside);
            builder.RegisterPopup<SettingsPopupPresenter, SettingsPopupView>("SettingsPopup",
                ScreenOptions.Modal | ScreenOptions.DimsBelow | ScreenOptions.CloseOnTapOutside);
            builder.RegisterPopup<InventoryPopupPresenter, InventoryPopupView>("InventoryPopup",
                ScreenOptions.Modal | ScreenOptions.DimsBelow | ScreenOptions.CloseOnTapOutside);

            builder.RegisterEntryPoint<MainBootstrap>();
        }
    }
}
