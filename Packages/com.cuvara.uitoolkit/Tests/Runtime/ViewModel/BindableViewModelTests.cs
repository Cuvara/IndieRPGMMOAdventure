namespace Cuvara.UIToolkit.Tests
{
    using System.Collections.Generic;
    using Cuvara.UIToolkit.ViewModel;
    using NUnit.Framework;
    using UnityEngine.UIElements;

    /// <summary>
    /// <see cref="BindableViewModel"/>, tested as the plain C# class it is — no panel, no
    /// element, no binding system.
    /// </summary>
    /// <remarks>
    /// The tests that matter most are the negative ones: a base that raised on every write
    /// would pass every "did the event fire" test and still hand the binding system
    /// redundant re-evaluations — the per-frame work the mandatory-notify rule exists to
    /// avoid. See <c>Documentation~/HYBRID-DATA-BINDING.md</c>.
    /// </remarks>
    public class BindableViewModelTests
    {
        /// <summary>The smallest real subclass: one value-type and one reference-type property.</summary>
        private sealed class TestViewModel : BindableViewModel
        {
            private int    health;
            private string caption;

            public int Health
            {
                get => this.health;
                set => this.Set(ref this.health, value);
            }

            public string Caption
            {
                get => this.caption;
                set => this.Set(ref this.caption, value);
            }

            public bool SetHealthReportingChange(int value) => this.Set(ref this.health, value, nameof(this.Health));
        }

        private static List<string> Record(BindableViewModel viewModel)
        {
            var raised = new List<string>();
            viewModel.propertyChanged += (_, args) => raised.Add(args.propertyName);
            return raised;
        }

        [Test]
        public void ChangingAValueTypeProperty_RaisesWithTheCallerMemberName()
        {
            var viewModel = new TestViewModel();
            var raised    = Record(viewModel);

            viewModel.Health = 42;

            Assert.That(raised, Is.EqualTo(new[] { nameof(TestViewModel.Health) }),
                "[CallerMemberName] must carry the property's own name into the event");
        }

        [Test]
        public void ChangingAReferenceTypeProperty_RaisesWithTheCallerMemberName()
        {
            var viewModel = new TestViewModel();
            var raised    = Record(viewModel);

            viewModel.Caption = "50/100";

            Assert.That(raised, Is.EqualTo(new[] { nameof(TestViewModel.Caption) }));
        }

        [Test]
        public void WritingAnEqualValue_RaisesNothing()
        {
            var viewModel = new TestViewModel { Health = 42 };
            var raised    = Record(viewModel);

            viewModel.Health = 42;

            Assert.That(raised, Is.Empty, "an equal write must never reach the binding system");
        }

        [Test]
        public void WritingNullOverNull_RaisesNothing()
        {
            var viewModel = new TestViewModel();
            var raised    = Record(viewModel);

            viewModel.Caption = null;

            Assert.That(raised, Is.Empty, "null to null is not a change");
        }

        [Test]
        public void WritingTheSameStringReference_RaisesNothing()
        {
            const string caption = "same reference";

            var viewModel = new TestViewModel { Caption = caption };
            var raised    = Record(viewModel);

            viewModel.Caption = caption;

            Assert.That(raised, Is.Empty);
        }

        [Test]
        public void WritingAnEqualButDistinctString_RaisesNothing()
        {
            // EqualityComparer<string>.Default compares content, not reference — two equal
            // strings from different allocations are still "no change".
            var viewModel = new TestViewModel { Caption = "50/100" };
            var raised    = Record(viewModel);

            viewModel.Caption = string.Concat("50/", "100");

            Assert.That(raised, Is.Empty);
        }

        [Test]
        public void StringNullToValue_AndValueToNull_BothRaise()
        {
            var viewModel = new TestViewModel();
            var raised    = Record(viewModel);

            viewModel.Caption = "alive";
            viewModel.Caption = null;

            Assert.That(raised, Has.Count.EqualTo(2));
        }

        [Test]
        public void Set_ReturnsTrueOnChange_AndFalseOnEqualValue()
        {
            var viewModel = new TestViewModel();

            Assert.That(viewModel.SetHealthReportingChange(10), Is.True, "a real change must return true");
            Assert.That(viewModel.SetHealthReportingChange(10), Is.False, "an equal write must return false");
            Assert.That(viewModel.SetHealthReportingChange(11), Is.True);
        }

        [Test]
        public void EverySubscriberReceivesTheRaise_AndAnUnsubscribedOneDoesNot()
        {
            var viewModel = new TestViewModel();

            var first  = 0;
            var second = 0;

            System.EventHandler<BindablePropertyChangedEventArgs> firstHandler = (_, _) => ++first;
            viewModel.propertyChanged += firstHandler;
            viewModel.propertyChanged += (_, _) => ++second;

            viewModel.Health = 1;
            viewModel.propertyChanged -= firstHandler;
            viewModel.Health = 2;

            Assert.That(first, Is.EqualTo(1));
            Assert.That(second, Is.EqualTo(2));
        }

        [Test]
        public void WithNoSubscribers_ChangingAProperty_DoesNotThrow()
        {
            var viewModel = new TestViewModel();

            Assert.DoesNotThrow(() => viewModel.Health = 7);
        }
    }
}
