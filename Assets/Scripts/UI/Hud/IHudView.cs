namespace Scripts.UI.Hud
{
    /// <summary>What a HUD presenter is allowed to say to the HUD view.</summary>
    /// <remarks>
    /// The one method mirrors the EcsHud sample's <c>IVitalsView</c>: the presenter hands
    /// over a ViewModel once and never learns whether the View renders it imperatively or
    /// through <c>SetBinding</c> — the data binding stays a View-internal detail, and the
    /// presenter stays testable as plain C# with no scene, no <c>UIDocument</c>, no
    /// <c>VisualElement</c>.
    /// </remarks>
    public interface IHudView
    {
        /// <summary>Makes <paramref name="viewModel"/> the state this view displays, now and as it changes.</summary>
        void Bind(HudViewModel viewModel);
    }
}
