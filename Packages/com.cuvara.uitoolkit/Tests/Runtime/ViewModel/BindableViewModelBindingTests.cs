namespace Cuvara.UIToolkit.Tests
{
    using System.Collections;
    using Cuvara.UIToolkit.ViewModel;
    using Cysharp.Threading.Tasks;
    using NUnit.Framework;
    using Unity.Properties;
    using UnityEngine;
    using UnityEngine.TestTools;
    using UnityEngine.UIElements;

    /// <summary>
    /// <see cref="BindableViewModel"/> under the REAL binding system: a live panel, a
    /// <see cref="DataBinding"/> with a <c>nameof</c> path, <see cref="BindingMode.ToTarget"/>.
    /// </summary>
    /// <remarks>
    /// <para>This is the wiring half of the hybrid convention's testing story — the
    /// ViewModel half is <see cref="BindableViewModelTests"/>, plain C# with no panel. The
    /// binding system only applies bindings inside a panel's update loop, so these are
    /// <c>[UnityTest]</c> on a real <c>UIDocument</c>, following the same pattern as the
    /// collection adapter tests.</para>
    ///
    /// <para>What it pins down: a property write through <c>Set</c> reaches the bound
    /// element on a later frame with no <c>Render</c> call anywhere — the exact mechanism
    /// the EcsHud sample's View relies on. See
    /// <c>Documentation~/HYBRID-DATA-BINDING.md</c>.</para>
    /// </remarks>
    public class BindableViewModelBindingTests
    {
        /// <summary>The shape the EcsHud sample binds: a caption and a 0..1 fraction.</summary>
        private sealed class HudViewModel : BindableViewModel
        {
            private string caption = string.Empty;
            private float  fraction;

            [CreateProperty]
            public string Caption
            {
                get => this.caption;
                set => this.Set(ref this.caption, value);
            }

            [CreateProperty]
            public float Fraction
            {
                get => this.fraction;
                set => this.Set(ref this.fraction, value);
            }
        }

        private GameObject    documentObject;
        private PanelSettings panelSettings;

        [TearDown]
        public void TearDown()
        {
            if (this.documentObject != null) Object.DestroyImmediate(this.documentObject);
            if (this.panelSettings != null) Object.DestroyImmediate(this.panelSettings);

            this.documentObject = null;
            this.panelSettings  = null;
        }

        private VisualElement BuildPanelRoot()
        {
            // A runtime panel with no theme style sheet logs about it; that is a rendering
            // concern and not what these tests are about. Re-asserted here rather than in
            // [SetUp] for the reason the collection tests document: [UnityTest] resets
            // LogAssert state after SetUp has already run.
            LogAssert.ignoreFailingMessages = true;

            this.panelSettings           = ScriptableObject.CreateInstance<PanelSettings>();
            this.panelSettings.scaleMode = PanelScaleMode.ConstantPixelSize;

            this.documentObject = new GameObject(nameof(BindableViewModelBindingTests));
            this.documentObject.SetActive(false);

            var uiDocument = this.documentObject.AddComponent<UIDocument>();
            uiDocument.panelSettings = this.panelSettings;

            this.documentObject.SetActive(true);

            var root = uiDocument.rootVisualElement;
            Assert.That(root, Is.Not.Null, "The UIDocument produced no root element.");

            return root;
        }

        /// <summary>Yields a few frames so the panel's binding updater runs.</summary>
        private static async UniTask Settle()
        {
            for (var i = 0; i < 3; ++i) await UniTask.Yield();
        }

        [UnityTest]
        public IEnumerator ANotifiedPropertyChange_ReachesTheBoundLabel_WithNoRenderCall() => UniTask.ToCoroutine(async () =>
        {
            var root      = this.BuildPanelRoot();
            var viewModel = new HudViewModel { Caption = "50/100" };

            var label = new Label();
            root.Add(label);

            root.dataSource = viewModel;
            label.SetBinding(nameof(Label.text), new DataBinding
            {
                dataSourcePath = new PropertyPath(nameof(HudViewModel.Caption)),
                bindingMode    = BindingMode.ToTarget,
            });

            await Settle();
            Assert.That(label.text, Is.EqualTo("50/100"), "the initial value must be applied when the binding activates");

            viewModel.Caption = "49/100";
            await Settle();

            Assert.That(label.text, Is.EqualTo("49/100"), "a Set() on the ViewModel must reach the element through the binding system");
        });

        [UnityTest]
        public IEnumerator AConvertedBinding_DrivesAStyleProperty_FromAPlainFloat() => UniTask.ToCoroutine(async () =>
        {
            // The sample's health bar: the ViewModel exposes a plain 0..1 float and the
            // View-side converter turns it into a StyleLength — UIElements types stay out
            // of the ViewModel.
            var root      = this.BuildPanelRoot();
            var viewModel = new HudViewModel { Fraction = 0.5f };

            var fill = new VisualElement();
            root.Add(fill);

            var binding = new DataBinding
            {
                dataSourcePath = new PropertyPath(nameof(HudViewModel.Fraction)),
                bindingMode    = BindingMode.ToTarget,
            };
            binding.sourceToUiConverters.AddConverter((ref float fraction) => new StyleLength(Length.Percent(fraction * 100f)));

            fill.SetBinding("style.width", binding);
            root.dataSource = viewModel;

            await Settle();
            Assert.That(fill.style.width.value, Is.EqualTo(Length.Percent(50f)));

            viewModel.Fraction = 0.25f;
            await Settle();

            Assert.That(fill.style.width.value, Is.EqualTo(Length.Percent(25f)));
        });
    }
}
