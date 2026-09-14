namespace Tests.Editor
{
    using System.Collections.Generic;
    using NUnit.Framework;
    using Scripts.UI.Hud;

    /// <summary>
    /// <see cref="HudViewModel"/> as plain C# — no panel, no scene: the notify-on-change
    /// discipline the binding system depends on. The wiring half (a live panel applying
    /// the notification) is <c>HudViewBindingTests</c> in PlayMode.
    /// </summary>
    public class HudViewModelTests
    {
        private HudViewModel viewModel;
        private List<string> raised;

        [SetUp]
        public void SetUp()
        {
            this.viewModel = new HudViewModel();
            this.raised = new List<string>();
            this.viewModel.propertyChanged += (_, args) => this.raised.Add(args.propertyName);
        }

        [Test]
        public void AChangedWrite_RaisesPropertyChanged_WithThePropertyName()
        {
            this.viewModel.HealthCaption = "57/100";
            this.viewModel.HealthFraction = 0.57f;
            this.viewModel.PositionCaption = "(1.0, 2.0)";
            this.viewModel.PlayersVisible = 3;
            this.viewModel.EntitiesVisible = 5;

            Assert.That(this.raised, Is.EqualTo(new[]
            {
                nameof(HudViewModel.HealthCaption),
                nameof(HudViewModel.HealthFraction),
                nameof(HudViewModel.PositionCaption),
                nameof(HudViewModel.PlayersVisible),
                nameof(HudViewModel.EntitiesVisible),
            }));
        }

        [Test]
        public void AnEqualWrite_RaisesNothing()
        {
            this.viewModel.HealthCaption = "57/100";
            this.viewModel.PlayersVisible = 3;
            this.raised.Clear();

            // The bridge's catch-up pass re-pushes identical values; the Set guard is what
            // makes that repeat push cost the binding system nothing.
            this.viewModel.HealthCaption = "57/100";
            this.viewModel.HealthFraction = 0f;
            this.viewModel.PlayersVisible = 3;

            Assert.That(this.raised, Is.Empty);
        }

        [Test]
        public void DefaultCaptions_AreEmpty_NeverNull()
        {
            Assert.That(this.viewModel.HealthCaption, Is.Empty);
            Assert.That(this.viewModel.PositionCaption, Is.Empty);
        }
    }
}
