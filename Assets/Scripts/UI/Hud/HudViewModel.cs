namespace Scripts.UI.Hud
{
    using Cuvara.UIToolkit.ViewModel;
    using Unity.Properties;

    /// <summary>
    /// The gameplay HUD's live state, as notifying properties the View binds to with
    /// runtime data binding (the package's hybrid convention —
    /// <c>Packages/com.cuvara.uitoolkit/Documentation~/HYBRID-DATA-BINDING.md</c>).
    /// </summary>
    /// <remarks>
    /// <para>Plain C#: no <c>VisualElement</c>, no <c>UIDocument</c>, no ECS type — this
    /// class compiles with neither Entities nor <c>com.cuvara.dots</c> installed, which is
    /// why it lives in <c>NDC.Scripts.UI</c> and not beside the bridge in the gated
    /// <c>NDC.Scripts.UI.Hud.Ecs</c> assembly. It is written by <c>HudPresenter</c> (the
    /// ECS sink) and read by <c>HudView</c>'s bindings; it is testable with NUnit alone.</para>
    ///
    /// <para>Every setter routes through <see cref="BindableViewModel.Set{T}"/>, so a write
    /// of an unchanged value raises nothing and the binding system re-evaluates nothing —
    /// the notify-on-change discipline the package contract makes mandatory. The bridge's
    /// one-shot catch-up pass re-pushes a value the sink already wrote; the guard is what
    /// makes that repeat push cost the UI nothing.</para>
    ///
    /// <para>Captions are strings and the fraction is a plain 0..1 float: converting a
    /// fraction to a <c>StyleLength</c>, or a count to a label string, is the View's job —
    /// UI Toolkit types never leak above the View.</para>
    /// </remarks>
    public sealed class HudViewModel : BindableViewModel
    {
        private string healthCaption = string.Empty;
        private float healthFraction;
        private string positionCaption = string.Empty;
        private int playersVisible;
        private int entitiesVisible;

        /// <summary>The "57/100" text over the health bar; "—" while no local player exists.</summary>
        [CreateProperty]
        public string HealthCaption
        {
            get => this.healthCaption;
            set => this.Set(ref this.healthCaption, value);
        }

        /// <summary>Local player health as 0..1. Turning it into a bar width is the View's job.</summary>
        [CreateProperty]
        public float HealthFraction
        {
            get => this.healthFraction;
            set => this.Set(ref this.healthFraction, value);
        }

        /// <summary>The local player's map position, preformatted "(12.3, 45.7)"; "—" while absent.</summary>
        [CreateProperty]
        public string PositionCaption
        {
            get => this.positionCaption;
            set => this.Set(ref this.positionCaption, value);
        }

        /// <summary>Replicated players currently mirrored client-side, the local player included.</summary>
        [CreateProperty]
        public int PlayersVisible
        {
            get => this.playersVisible;
            set => this.Set(ref this.playersVisible, value);
        }

        /// <summary>Every replicated entity currently mirrored client-side — players, mobs, all kinds.</summary>
        [CreateProperty]
        public int EntitiesVisible
        {
            get => this.entitiesVisible;
            set => this.Set(ref this.entitiesVisible, value);
        }
    }
}
