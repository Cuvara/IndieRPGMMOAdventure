#if CUVARA_DOTS && CUVARA_NETCODE && CUVARA_UITOOLKIT_ENTITIES
namespace Tests.Editor
{
    using System.Collections.Generic;
    using NUnit.Framework;
    using Scripts.UI.Hud;
    using Scripts.UI.Hud.Ecs;

    /// <summary>
    /// <see cref="HudPresenter"/> as plain C#: it binds the view once, translates pushes
    /// onto the ViewModel, and an identical push is silent. No <c>VisualElement</c>
    /// anywhere — the view is a spy, which is exactly what <c>IHudView</c> is for.
    /// </summary>
    public class HudPresenterTests
    {
        private sealed class SpyView : IHudView
        {
            public HudViewModel Bound;
            public int BindCalls;

            public void Bind(HudViewModel viewModel)
            {
                this.Bound = viewModel;
                this.BindCalls++;
            }
        }

        private static HudSnapshot Snapshot() => new("57/100", 0.57f, "(1.0, 2.0)", 2, 3, true);

        [Test]
        public void Construction_BindsTheViewToTheViewModel_Once()
        {
            var view = new SpyView();
            var viewModel = new HudViewModel();

            _ = new HudPresenter(view, viewModel);

            Assert.That(view.Bound, Is.SameAs(viewModel));
            Assert.That(view.BindCalls, Is.EqualTo(1));
        }

        [Test]
        public void Push_WritesEveryProperty()
        {
            var viewModel = new HudViewModel();
            var presenter = new HudPresenter(new SpyView(), viewModel);

            presenter.Push(Snapshot());

            Assert.That(viewModel.HealthCaption, Is.EqualTo("57/100"));
            Assert.That(viewModel.HealthFraction, Is.EqualTo(0.57f));
            Assert.That(viewModel.PositionCaption, Is.EqualTo("(1.0, 2.0)"));
            Assert.That(viewModel.PlayersVisible, Is.EqualTo(2));
            Assert.That(viewModel.EntitiesVisible, Is.EqualTo(3));
        }

        [Test]
        public void AnIdenticalPush_RaisesNoNotification()
        {
            var viewModel = new HudViewModel();
            var presenter = new HudPresenter(new SpyView(), viewModel);
            presenter.Push(Snapshot());

            var raised = new List<string>();
            viewModel.propertyChanged += (_, args) => raised.Add(args.propertyName);

            // The bridge's catch-up pass replays the current value to every sink; the
            // ViewModel's Set guard is what keeps that replay free for the UI.
            presenter.Push(Snapshot());

            Assert.That(raised, Is.Empty);
        }
    }
}
#endif
