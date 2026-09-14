namespace Scripts.UI.Hud.Ecs
{
    using System;
    using Cuvara.UIToolkit.Ecs;
    using Scripts.UI.Hud;

    /// <summary>
    /// The sink: receives boundary <see cref="HudSnapshot"/>s from the bridge and writes
    /// them onto the bindable <see cref="HudViewModel"/>.
    /// </summary>
    /// <remarks>
    /// It implements <see cref="IViewModelSink{TViewModel}"/> — the bridge knows it as
    /// "a sink", never as a presenter. Note what it does NOT reference: <c>UIDocument</c>,
    /// <c>VisualElement</c>, <c>DataBinding</c>, UXML, USS, or any ECS type beyond the
    /// snapshot. It sets five plain properties; the <c>Set</c> guard in
    /// <c>BindableViewModel</c> means an identical push (the bridge's catch-up pass, say)
    /// raises nothing and costs the UI nothing.
    /// </remarks>
    public sealed class HudPresenter : IViewModelSink<HudSnapshot>
    {
        private readonly HudViewModel viewModel;

        public HudPresenter(IHudView view, HudViewModel viewModel)
        {
            if (view == null) throw new ArgumentNullException(nameof(view));

            this.viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
            view.Bind(this.viewModel);
        }

        public void Push(in HudSnapshot snapshot)
        {
            this.viewModel.HealthCaption = snapshot.HealthCaption;
            this.viewModel.HealthFraction = snapshot.HealthFraction;
            this.viewModel.PositionCaption = snapshot.PositionCaption;
            this.viewModel.PlayersVisible = snapshot.PlayersVisible;
            this.viewModel.EntitiesVisible = snapshot.EntitiesVisible;
        }
    }
}
