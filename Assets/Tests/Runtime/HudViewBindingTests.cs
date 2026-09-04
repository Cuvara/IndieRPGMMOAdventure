#if UNITY_EDITOR
namespace Tests.Runtime
{
    using System.Collections;
    using Cysharp.Threading.Tasks;
    using NUnit.Framework;
    using Scripts.UI.Hud;
    using UnityEngine;
    using UnityEngine.TestTools;
    using UnityEngine.UIElements;

    /// <summary>
    /// The real <see cref="HudView"/> over the real <c>HudView.uxml</c> under the real
    /// binding system: a live <see cref="UIDocument"/>, <c>SetBinding</c> paths, converters
    /// — the wiring half of the hybrid convention, following the uitoolkit package's
    /// <c>BindableViewModelBindingTests</c>.
    /// </summary>
    /// <remarks>
    /// Editor-only (<c>#if UNITY_EDITOR</c>) because the UXML is loaded through
    /// <c>AssetDatabase</c>; PlayMode tests in the Editor are where this runs. What it pins
    /// down beyond the package's own tests: the generated <c>AssignQueries</c> resolves
    /// every element of the committed UXML, and <see cref="HudView.Bind"/>'s paths and
    /// converters reach the right elements — a renamed element or property fails here,
    /// not on a player's screen.
    /// </remarks>
    public class HudViewBindingTests
    {
        private const string UxmlPath = "Assets/Scripts/UI/Hud/HudView.uxml";

        private GameObject documentObject;
        private PanelSettings panelSettings;
        private HudView view;

        [TearDown]
        public void TearDown()
        {
            this.view?.DestroySelf();
            if (this.documentObject != null) Object.DestroyImmediate(this.documentObject);
            if (this.panelSettings != null) Object.DestroyImmediate(this.panelSettings);

            this.view = null;
            this.documentObject = null;
            this.panelSettings = null;
        }

        private VisualElement BuildPanelRoot()
        {
            // A runtime panel with no theme style sheet logs about it; not what these tests
            // are about. Re-asserted here rather than in [SetUp] because [UnityTest] resets
            // LogAssert state after SetUp has already run.
            LogAssert.ignoreFailingMessages = true;

            this.panelSettings = ScriptableObject.CreateInstance<PanelSettings>();
            this.panelSettings.scaleMode = PanelScaleMode.ConstantPixelSize;

            this.documentObject = new GameObject(nameof(HudViewBindingTests));
            this.documentObject.SetActive(false);

            var uiDocument = this.documentObject.AddComponent<UIDocument>();
            uiDocument.panelSettings = this.panelSettings;

            this.documentObject.SetActive(true);

            var root = uiDocument.rootVisualElement;
            Assert.That(root, Is.Not.Null, "The UIDocument produced no root element.");

            return root;
        }

        private HudView BuildView(VisualElement root)
        {
            var asset = UnityEditor.AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(UxmlPath);
            Assert.That(asset, Is.Not.Null, $"missing {UxmlPath} — did the HUD UXML move without this test following?");

            this.view = new HudView(asset);
            root.Add(this.view.Root);
            this.view.Show();
            return this.view;
        }

        /// <summary>Yields a few frames so the panel's binding updater runs.</summary>
        private static async UniTask Settle()
        {
            for (var i = 0; i < 3; ++i) await UniTask.Yield();
        }

        [UnityTest]
        public IEnumerator PropertyWrites_ReachTheBoundElements_WithNoRenderCall() => UniTask.ToCoroutine(async () =>
        {
            var hudView = this.BuildView(this.BuildPanelRoot());
            var viewModel = new HudViewModel
            {
                HealthCaption = "57/100",
                HealthFraction = 0.57f,
                PositionCaption = "(12.3, 45.7)",
                PlayersVisible = 3,
                EntitiesVisible = 5,
            };

            hudView.Bind(viewModel);
            await Settle();

            Assert.That(hudView.HudHealthCaption.text, Is.EqualTo("57/100"));
            Assert.That(hudView.HudHealthFill.style.width.value, Is.EqualTo(Length.Percent(57f)));
            Assert.That(hudView.HudPosition.text, Is.EqualTo("(12.3, 45.7)"));
            Assert.That(hudView.HudPlayers.text, Is.EqualTo("Players 3"));
            Assert.That(hudView.HudEntities.text, Is.EqualTo("Entities 5"));

            viewModel.HealthCaption = "30/100";
            viewModel.HealthFraction = 0.3f;
            viewModel.PlayersVisible = 2;
            await Settle();

            Assert.That(hudView.HudHealthCaption.text, Is.EqualTo("30/100"), "a Set() on the ViewModel must reach the element through the binding system");
            Assert.That(hudView.HudHealthFill.style.width.value, Is.EqualTo(Length.Percent(30f)));
            Assert.That(hudView.HudPlayers.text, Is.EqualTo("Players 2"));
        });

        [UnityTest]
        public IEnumerator AssignQueries_ResolvesEveryNamedElement_OfTheCommittedUxml() => UniTask.ToCoroutine(async () =>
        {
            var hudView = this.BuildView(this.BuildPanelRoot());
            await Settle();

            // The generated half already threw in the constructor if any Require<T> failed;
            // these asserts exist to name the elements a future edit must keep.
            Assert.That(hudView.GameHud, Is.Not.Null);
            Assert.That(hudView.HudHealthCaption, Is.Not.Null);
            Assert.That(hudView.HudHealthTrack, Is.Not.Null);
            Assert.That(hudView.HudHealthFill, Is.Not.Null);
            Assert.That(hudView.HudPosition, Is.Not.Null);
            Assert.That(hudView.HudPlayers, Is.Not.Null);
            Assert.That(hudView.HudEntities, Is.Not.Null);
        });
    }
}
#endif
