namespace Scripts.UI.Hud
{
    using System;
    using Cuvara.UIToolkit.View;
    using Unity.Properties;
    using UnityEngine.UIElements;

    /// <summary>
    /// The gameplay HUD view: wires the UXML to <see cref="HudViewModel"/> with runtime
    /// data binding, once, in <see cref="Bind"/>. The ONLY layer that knows UI Toolkit exists.
    /// </summary>
    /// <remarks>
    /// <para>The other half of this <c>partial</c> is <c>Generated/HudView.uxml.g.cs</c>:
    /// one typed property per named element in <c>HudView.uxml</c>, resolved through
    /// <c>Require&lt;T&gt;</c> by <c>AssignQueries</c> — a UXML rename fails loudly at
    /// construction, before any binding is created. The UXML is enrolled in the codegen
    /// (the generated file's existence is the enrollment), so saving it regenerates the
    /// bindings and CI's drift check keeps the pair honest.</para>
    ///
    /// <para>There is no <c>Render</c> method: after <see cref="Bind"/>, a property write
    /// on the ViewModel reaches the elements through the binding system, on notification,
    /// with no call into this class. All bindings are <see cref="BindingMode.ToTarget"/> —
    /// data flows toward the UI only; this HUD has no commands, and if it grows any they
    /// go on <c>ScreenSubscriptions</c>, never into a binding.</para>
    ///
    /// <para>The UI-type conversions live here, as converters on the bindings: the
    /// ViewModel exposes a plain 0..1 float and plain ints; <c>StyleLength</c> and the
    /// "Players N" label format never leak above the View.</para>
    /// </remarks>
    public sealed partial class HudView : BaseUIToolkitView, IHudView
    {
        public HudView(VisualTreeAsset visualTreeAsset) : base(visualTreeAsset)
        {
            this.StretchToParent();
            this.AssignQueries(this.Root);
        }

        public void Bind(HudViewModel viewModel)
        {
            if (viewModel == null) throw new ArgumentNullException(nameof(viewModel));

            this.Root.dataSource = viewModel;

            this.HudHealthCaption.SetBinding(nameof(Label.text), ToTarget(nameof(HudViewModel.HealthCaption)));
            this.HudPosition.SetBinding(nameof(Label.text), ToTarget(nameof(HudViewModel.PositionCaption)));

            var fillBinding = ToTarget(nameof(HudViewModel.HealthFraction));
            fillBinding.sourceToUiConverters.AddConverter((ref float fraction) => new StyleLength(Length.Percent(fraction * 100f)));
            this.HudHealthFill.SetBinding("style.width", fillBinding);

            var playersBinding = ToTarget(nameof(HudViewModel.PlayersVisible));
            playersBinding.sourceToUiConverters.AddConverter((ref int count) => $"Players {count}");
            this.HudPlayers.SetBinding(nameof(Label.text), playersBinding);

            var entitiesBinding = ToTarget(nameof(HudViewModel.EntitiesVisible));
            entitiesBinding.sourceToUiConverters.AddConverter((ref int count) => $"Entities {count}");
            this.HudEntities.SetBinding(nameof(Label.text), entitiesBinding);
        }

        private static DataBinding ToTarget(string propertyName)
        {
            return new DataBinding
            {
                dataSourcePath = new PropertyPath(propertyName),
                bindingMode = BindingMode.ToTarget,
            };
        }
    }
}
