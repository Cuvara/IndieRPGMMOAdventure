namespace Cuvara.UIToolkit.Samples.ScreenFlow
{
    using Cuvara.UIToolkit.Core;
    using Cuvara.UIToolkit.Flow;
    using Cuvara.UIToolkit.VContainer;
    using global::VContainer;
    using global::VContainer.Unity;
    using UnityEngine;

    // A MonoBehaviour must live in a file named after it — Unity maps one MonoScript per file
    // by filename, so a scene referencing a MonoBehaviour declared in a differently-named file
    // serialises with m_Script: {fileID: 0} and loads as "the referenced script is missing".
    // It compiles cleanly either way, which is why this was only caught by opening a scene.

    /// <summary>The once-per-project wiring, and the only place screens are named.</summary>
    public sealed class ScreenFlowSampleScope : LifetimeScope
    {
        [SerializeField] private InspectorAssetLoader loader = new();

        protected override void Configure(IContainerBuilder builder)
        {
            builder.RegisterUIToolkit();                                   // RootUIDocument + view factory
            builder.RegisterInstance<IVisualTreeAssetLoader>(this.loader); // the host decides what a key means
            builder.RegisterScreenFlow();                                  // navigator, registry, scope factory

            // One line per screen. Compiler-checked, greppable, and nothing is constructed by
            // runtime Type — so no [Preserve] and no service locator anywhere.
            builder.RegisterScreen<RootScreenPresenter,   RootScreenView>  ("RootScreen");
            builder.RegisterScreen<SecondScreenPresenter, SecondScreenView>("SecondScreen");
            builder.RegisterPopup <ConfirmPopupPresenter, ConfirmPopupView>("ConfirmPopup",
                ScreenOptions.Modal | ScreenOptions.DimsBelow | ScreenOptions.CloseOnTapOutside);

            builder.RegisterEntryPoint<ScreenFlowSampleBootstrap>();
        }
    }

}
