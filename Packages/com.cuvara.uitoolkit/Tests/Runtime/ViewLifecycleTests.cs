namespace Cuvara.UIToolkit.Tests
{
    using System;
    using System.Collections;
    using System.Collections.Generic;
    using Cuvara.UIToolkit.Core;
    using Cuvara.UIToolkit.Input;
    using Cuvara.UIToolkit.Managers;
    using Cuvara.UIToolkit.View;
    using Cysharp.Threading.Tasks;
    using NUnit.Framework;
    using UnityEngine;
    using UnityEngine.TestTools;
    using UnityEngine.UIElements;
    using Object = UnityEngine.Object;

    /// <summary>A loader serving assets the test hands it, by key.</summary>
    /// <remarks>
    /// This is what <see cref="IVisualTreeAssetLoader"/> is for, and the fact that a test
    /// double for it is nine lines is the argument for the seam. The host framework this
    /// package was extracted from went through an asset manager with a dozen members, none
    /// of which the package used.
    /// </remarks>
    public sealed class StubVisualTreeAssetLoader : IVisualTreeAssetLoader
    {
        private readonly Dictionary<string, VisualTreeAsset> assets = new();

        /// <summary>Keys asked for, in order. Lets a test assert the factory used the key it was given.</summary>
        public readonly List<string> RequestedKeys = new();

        /// <summary>When true, unknown keys resolve to null instead of throwing.</summary>
        public bool ReturnNullForUnknownKeys { get; set; }

        public void Add(string key, VisualTreeAsset asset) { this.assets[key] = asset; }

        public UniTask<VisualTreeAsset> LoadAsync(string key)
        {
            this.RequestedKeys.Add(key);

            if (this.assets.TryGetValue(key, out var asset)) return UniTask.FromResult(asset);

            if (this.ReturnNullForUnknownKeys) return UniTask.FromResult<VisualTreeAsset>(null);

            throw new KeyNotFoundException($"No stub asset for key '{key}'.");
        }
    }

    /// <summary>A minimal concrete view, built the way the factory requires.</summary>
    public sealed class TestView : BaseUIToolkitView
    {
        public TestView(VisualTreeAsset visualTreeAsset) : base(visualTreeAsset)
        {
            this.StretchToParent();
        }
    }

    /// <summary>A view whose constructor does not match what the factory calls.</summary>
    public sealed class WrongConstructorView : BaseUIToolkitView
    {
        public WrongConstructorView(VisualElement root, int unused) : base(root)
        {
            _ = unused;
        }
    }

    /// <summary>Not a view at all.</summary>
    public sealed class NotAView
    {
    }

    /// <summary>
    /// The package's own contracts, exercised without a host framework anywhere in sight —
    /// which is the property the whole extraction exists to create.
    /// </summary>
    public class ViewLifecycleTests
    {
        private const string RootUxmlPath = "Packages/com.cuvara.uitoolkit/Tests/Runtime/TestRoot.uxml";
        private const string ViewUxmlPath = "Packages/com.cuvara.uitoolkit/Tests/Runtime/TestView.uxml";

        private const string ViewKey = "TestView";

        private GameObject     documentObject;
        private PanelSettings  panelSettings;
        private RootUIDocument rootUIDocument;

        [SetUp]
        public void SetUp()
        {
            // A UIDocument with no theme logs about it; that is a rendering concern and not
            // what these tests are about.
            LogAssert.ignoreFailingMessages = true;
        }

        [TearDown]
        public void TearDown()
        {
            if (this.documentObject != null) Object.DestroyImmediate(this.documentObject);
            if (this.panelSettings != null) Object.DestroyImmediate(this.panelSettings);

            this.rootUIDocument             = null;
            LogAssert.ignoreFailingMessages = false;
        }

        #region The view lifecycle

        [Test]
        public void ANewView_StartsInvisibleAndNonInteractive()
        {
            // Created unseen, so Open() is what reveals it. Building straight into a visible
            // layer would otherwise flash one un-transitioned frame.
            var view = new TestView(LoadUxml(ViewUxmlPath));

            Assert.That(view.Root.style.opacity.value, Is.EqualTo(0f).Within(0.001f));
            Assert.That(view.Root.pickingMode, Is.EqualTo(PickingMode.Ignore));
        }

        [UnityTest]
        public IEnumerator Open_MakesTheViewVisibleAndInteractive_AndRaisesTheEvent() => UniTask.ToCoroutine(async () =>
        {
            var view   = new TestView(LoadUxml(ViewUxmlPath));
            var opened = 0;

            view.ViewDidOpen += () => ++opened;

            await view.Open();

            Assert.That(view.Root.style.opacity.value, Is.EqualTo(1f).Within(0.001f));
            Assert.That(view.Root.pickingMode, Is.EqualTo(PickingMode.Position), "an opened view must accept clicks");
            Assert.That(opened, Is.EqualTo(1));
        });

        [UnityTest]
        public IEnumerator Close_MakesTheViewInvisibleAndInert_AndRaisesTheEvent() => UniTask.ToCoroutine(async () =>
        {
            var view   = new TestView(LoadUxml(ViewUxmlPath));
            var closed = 0;

            view.ViewDidClose += () => ++closed;

            await view.Open();
            await view.Close();

            Assert.That(view.Root.style.opacity.value, Is.EqualTo(0f).Within(0.001f));
            Assert.That(view.Root.pickingMode, Is.EqualTo(PickingMode.Ignore),
                "a closed view that still swallows clicks is the bug opacity-only hiding causes");
            Assert.That(closed, Is.EqualTo(1));
        });

        [Test]
        public void HideAndShow_MoveOpacityAndPickingTogether()
        {
            var view = new TestView(LoadUxml(ViewUxmlPath));

            view.Show();
            Assert.That(view.Root.pickingMode, Is.EqualTo(PickingMode.Position));

            view.Hide();
            Assert.That(view.Root.pickingMode, Is.EqualTo(PickingMode.Ignore));
        }

        [Test]
        public void DestroySelf_DetachesTheRootAndRaisesTheEvent()
        {
            var parent    = new VisualElement();
            var view      = new TestView(LoadUxml(ViewUxmlPath));
            var destroyed = 0;

            view.ViewDidDestroy += () => ++destroyed;
            parent.Add(view.Root);

            Assert.That(view.Root.parent, Is.SameAs(parent), "precondition");

            view.DestroySelf();

            Assert.That(view.Root.parent, Is.Null);
            Assert.That(destroyed, Is.EqualTo(1));
        }

        [Test]
        public void ConstructingFromANullAsset_Throws()
        {
            Assert.Throws<ArgumentNullException>(() => new TestView(null));
        }

        #endregion

        #region Layers and surfaces

        [Test]
        public void SetParent_MovesTheViewIntoTheLayer()
        {
            var layerElement = new VisualElement();
            var layer        = new VisualElementViewLayer(layerElement);
            var view         = new TestView(LoadUxml(ViewUxmlPath));

            view.ViewSurface.SetParent(layer);

            Assert.That(view.Root.parent, Is.SameAs(layerElement));
        }

        [Test]
        public void SetParent_Twice_Reparents_RatherThanDoubleParenting()
        {
            var first  = new VisualElementViewLayer(new VisualElement());
            var second = new VisualElementViewLayer(new VisualElement());
            var view   = new TestView(LoadUxml(ViewUxmlPath));

            view.ViewSurface.SetParent(first);
            view.ViewSurface.SetParent(second);

            Assert.That(second.Element.childCount, Is.EqualTo(1));
            Assert.That(first.Element.childCount, Is.EqualTo(0), "the first layer must have let go");
        }

        [Test]
        public void TheViewSurfaceIsCached_NotAllocatedPerAccess()
        {
            // A screen flow reparents on every open and close; a wrapper per access would
            // put garbage on a path that runs during transitions.
            var view = new TestView(LoadUxml(ViewUxmlPath));

            Assert.That(view.ViewSurface, Is.SameAs(view.ViewSurface));
        }

        [Test]
        public void SetParent_WithAForeignLayer_ThrowsRatherThanDoingNothing()
        {
            // The negative path that matters most: a silent no-op here surfaces much later
            // as a screen that renders nowhere, with nothing in the log to say why.
            var view = new TestView(LoadUxml(ViewUxmlPath));

            Assert.Throws<InvalidOperationException>(() => view.ViewSurface.SetParent(new ForeignLayer()));
        }

        [Test]
        public void SetParent_WithNull_Throws()
        {
            var view = new TestView(LoadUxml(ViewUxmlPath));

            Assert.Throws<InvalidOperationException>(() => view.ViewSurface.SetParent(null));
        }

        [Test]
        public void ALayerOverANullElement_Throws()
        {
            Assert.Throws<ArgumentNullException>(() => new VisualElementViewLayer(null));
        }

        private sealed class ForeignLayer : IViewLayer
        {
        }

        #endregion

        #region The factory

        [UnityTest]
        public IEnumerator CreateAsync_LoadsByKeyAndBuildsTheView() => UniTask.ToCoroutine(async () =>
        {
            var loader = new StubVisualTreeAssetLoader();
            loader.Add(ViewKey, LoadUxml(ViewUxmlPath));

            var factory = new UIToolkitViewFactory(loader);
            var view    = await factory.CreateAsync<TestView>(ViewKey);

            Assert.That(view, Is.Not.Null);
            Assert.That(view.Root, Is.Not.Null);
            Assert.That(loader.RequestedKeys, Is.EqualTo(new[] { ViewKey }), "the factory must ask for the key it was given");
        });

        [Test]
        public void Create_FromAnAssetDirectly_NeedsNoLoaderAtAll()
        {
            // The static path is what makes the one reflective step testable with no panel,
            // no loader and no container in sight.
            var view = UIToolkitViewFactory.Create<TestView>(LoadUxml(ViewUxmlPath));

            Assert.That(view, Is.Not.Null);
        }

        [UnityTest]
        public IEnumerator CreateAsync_WhenTheLoaderReturnsNull_ThrowsNamingTheKey() => UniTask.ToCoroutine(async () =>
        {
            var loader  = new StubVisualTreeAssetLoader { ReturnNullForUnknownKeys = true };
            var factory = new UIToolkitViewFactory(loader);

            try
            {
                await factory.CreateAsync<TestView>("missing-key");
                Assert.Fail("Expected an InvalidOperationException.");
            }
            catch (InvalidOperationException exception)
            {
                Assert.That(exception.Message, Does.Contain("missing-key"),
                    "the message must name the key, or the caller cannot tell which asset is missing");
            }
        });

        [Test]
        public void Create_WithATypeThatIsNotAView_ThrowsNamingIt()
        {
            var exception = Assert.Throws<ArgumentException>(() => UIToolkitViewFactory.Create(typeof(NotAView), LoadUxml(ViewUxmlPath)));

            Assert.That(exception.Message, Does.Contain(nameof(NotAView)));
        }

        [Test]
        public void Create_WithAnAbstractType_Throws()
        {
            Assert.Throws<ArgumentException>(() => UIToolkitViewFactory.Create(typeof(BaseUIToolkitView), LoadUxml(ViewUxmlPath)));
        }

        [Test]
        public void Create_WithoutTheExpectedConstructor_ThrowsAndListsWhatItFound()
        {
            // The alternative — a MissingMethodException out of Activator — says only "no
            // matching constructor" and leaves the reader to work out which one was wanted.
            var exception = Assert.Throws<ArgumentException>(() => UIToolkitViewFactory.Create(typeof(WrongConstructorView), LoadUxml(ViewUxmlPath)));

            Assert.That(exception.Message, Does.Contain(nameof(VisualTreeAsset)));
            Assert.That(exception.Message, Does.Contain("Found:"));
        }

        [Test]
        public void Create_WithNullArguments_Throws()
        {
            Assert.Throws<ArgumentNullException>(() => UIToolkitViewFactory.Create(null, LoadUxml(ViewUxmlPath)));
            Assert.Throws<ArgumentNullException>(() => UIToolkitViewFactory.Create(typeof(TestView), null));
        }

        [Test]
        public void AFactoryOverANullLoader_Throws()
        {
            Assert.Throws<ArgumentNullException>(() => new UIToolkitViewFactory(null));
        }

        [UnityTest]
        public IEnumerator CreateAsync_WithNoKey_Throws() => UniTask.ToCoroutine(async () =>
        {
            var factory = new UIToolkitViewFactory(new StubVisualTreeAssetLoader());

            try
            {
                await factory.CreateAsync<TestView>(null);
                Assert.Fail("Expected an ArgumentException.");
            }
            catch (ArgumentException)
            {
            }
        });

        #endregion

        #region RootUIDocument

        [Test]
        public void ItResolvesTheThreeLayersByName()
        {
            this.BuildDocument();

            Assert.That(this.rootUIDocument.RootUIShowElement, Is.Not.Null);
            Assert.That(this.rootUIDocument.RootUIClosedElement, Is.Not.Null);
            Assert.That(this.rootUIDocument.RootUIOverlayElement, Is.Not.Null);

            Assert.That(this.rootUIDocument.RootUIShowElement.name, Is.EqualTo("root-ui-show"));
            Assert.That(this.rootUIDocument.RootUIOverlayElement.name, Is.EqualTo("root-ui-overlay"));
        }

        [Test]
        public void TheThreeLayersAreCached_NotRebuiltPerAccess()
        {
            this.BuildDocument();

            Assert.That(this.rootUIDocument.ShowLayer, Is.SameAs(this.rootUIDocument.ShowLayer));
            Assert.That(this.rootUIDocument.Layers.Screen, Is.SameAs(this.rootUIDocument.ShowLayer));
            Assert.That(this.rootUIDocument.Layers.Overlay, Is.SameAs(this.rootUIDocument.OverlayLayer));
        }

        [Test]
        public void AViewParentedIntoTheOverlayLayer_LandsInTheOverlayElement()
        {
            this.BuildDocument();

            var view = new TestView(LoadUxml(ViewUxmlPath));
            view.ViewSurface.SetParent(this.rootUIDocument.OverlayLayer);

            Assert.That(view.Root.parent, Is.SameAs(this.rootUIDocument.RootUIOverlayElement));
        }

        #endregion

        #region Back navigation

        // These need a real panel. A detached VisualElement has no event dispatcher, so
        // SendEvent on one is silently a no-op — which makes every assertion here pass
        // vacuously for the wrong reason. BuildDocument() gives us a live UIDocument, and
        // its rootVisualElement is where a host would register the source anyway.

        [Test]
        public void ACancelEvent_RaisesBackRequested()
        {
            this.BuildDocument();

            var requested = 0;

            using var source = new BackNavigationSource(this.rootUIDocument.RootVisualElement);
            source.BackRequested += () => ++requested;

            this.SendCancel();

            Assert.That(requested, Is.EqualTo(1));
            Assert.That(source.HandledCount, Is.EqualTo(1));
        }

        #region BackHandler — the path that can decline

        /// <summary>
        /// A handler that returns false must NOT consume the press.
        /// </summary>
        /// <remarks>
        /// This is the whole reason <c>BackHandler</c> exists. An <c>Action</c> cannot report
        /// whether it did anything, so the legacy path has to assume every press was handled
        /// and consume it — which at the root screen swallows Back and stops an Android app
        /// exiting. A source that eats a press it did not act on is indistinguishable, from
        /// the outside, from a broken Back button.
        /// </remarks>
        [Test]
        public void BackHandlerReturningFalse_DoesNotConsumeAndDoesNotCount()
        {
            // Every test in this region needs the panel: BackNavigationSource takes the
            // document's root, and SendCancel dispatches through it.
            this.BuildDocument();

            using var source = new BackNavigationSource(this.rootUIDocument.RootVisualElement);
            source.BackHandler = () => false;

            this.SendCancel();

            Assert.That(source.HandledCount, Is.EqualTo(0),
                "an unhandled press was counted as handled");
        }

        [Test]
        public void BackHandlerReturningTrue_ConsumesAndCounts()
        {
            // Every test in this region needs the panel: BackNavigationSource takes the
            // document's root, and SendCancel dispatches through it.
            this.BuildDocument();

            using var source = new BackNavigationSource(this.rootUIDocument.RootVisualElement);
            var calls = 0;
            source.BackHandler = () => { ++calls; return true; };

            this.SendCancel();

            Assert.That(calls, Is.EqualTo(1));
            Assert.That(source.HandledCount, Is.EqualTo(1));
        }

        /// <summary>
        /// With both set, the handler wins and the event is not raised.
        /// </summary>
        /// <remarks>
        /// Raising both would double-handle one press — the navigator pops, and whatever is
        /// still on the legacy event pops again. Stated as a test because "which one wins"
        /// is exactly the kind of thing a later refactor reasonably guesses wrong.
        /// </remarks>
        [Test]
        public void WhenBothAreSet_TheHandlerWinsAndTheEventIsNotRaised()
        {
            // Every test in this region needs the panel: BackNavigationSource takes the
            // document's root, and SendCancel dispatches through it.
            this.BuildDocument();

            using var source = new BackNavigationSource(this.rootUIDocument.RootVisualElement);
            var eventRaised = 0;
            source.BackRequested += () => ++eventRaised;
            source.BackHandler   = () => true;

            this.SendCancel();

            Assert.That(eventRaised, Is.EqualTo(0),
                "both paths ran, so one Back press would be handled twice");
            Assert.That(source.HandledCount, Is.EqualTo(1));
        }

        /// <summary>
        /// With no handler, behaviour is byte-identical to before BackHandler existed.
        /// </summary>
        /// <remarks>
        /// This is the regression guard for the frozen host framework this package came out
        /// of. That repository is read-only to us, and a file in it subscribes to
        /// <see cref="BackNavigationSource.BackRequested"/> with a <c>void</c> method.
        /// Changing the event's signature would have broken it with CS0407 and there would
        /// have been no way to fix it, which is why <c>BackHandler</c> was added ALONGSIDE
        /// the event rather than replacing it. If someone later "tidies up" by deleting the
        /// legacy path, this test is what says no.
        ///
        /// <para>The host is deliberately not named here: this package must not mention it,
        /// and the gate that enforces that caught this very comment on its first draft.</para>
        /// </remarks>
        [Test]
        public void WithNoHandler_TheLegacyEventPathIsUnchanged()
        {
            // Every test in this region needs the panel: BackNavigationSource takes the
            // document's root, and SendCancel dispatches through it.
            this.BuildDocument();

            using var source = new BackNavigationSource(this.rootUIDocument.RootVisualElement);
            var requested = 0;
            source.BackRequested += () => ++requested;

            this.SendCancel();
            this.SendCancel();

            Assert.That(requested, Is.EqualTo(2));
            Assert.That(source.HandledCount, Is.EqualTo(2));
        }

        #endregion

        [Test]
        public void ItRaisesOncePerPress()
        {
            this.BuildDocument();

            var requested = 0;

            using var source = new BackNavigationSource(this.rootUIDocument.RootVisualElement);
            source.BackRequested += () => ++requested;

            this.SendCancel();
            this.SendCancel();

            Assert.That(requested, Is.EqualTo(2));
        }

        [Test]
        public void WithNoSubscriber_ItDoesNotConsumeThePress()
        {
            // Consuming a press nobody wants would silently swallow Back for whatever is
            // underneath.
            this.BuildDocument();

            var reachedLayer = 0;
            this.rootUIDocument.RootUIOverlayElement.RegisterCallback<NavigationCancelEvent>(_ => ++reachedLayer);

            using var source = new BackNavigationSource(this.rootUIDocument.RootVisualElement);

            this.SendCancel(this.rootUIDocument.RootUIOverlayElement);

            Assert.That(source.HandledCount, Is.EqualTo(0));
            Assert.That(reachedLayer, Is.EqualTo(1), "an unhandled cancel must keep propagating");
        }

        [Test]
        public void AHandledPress_IsConsumed_SoNothingUnderneathAlsoHandlesIt()
        {
            this.BuildDocument();

            var reachedLayer = 0;
            this.rootUIDocument.RootUIOverlayElement.RegisterCallback<NavigationCancelEvent>(_ => ++reachedLayer);

            using var source = new BackNavigationSource(this.rootUIDocument.RootVisualElement);
            source.BackRequested += () => { };

            // Aimed at a DESCENDANT of the root, so the root's trickle-down callback runs
            // first and the layer's would run second — if the event were still propagating.
            this.SendCancel(this.rootUIDocument.RootUIOverlayElement);

            Assert.That(source.HandledCount, Is.EqualTo(1), "precondition: the source handled it");
            Assert.That(reachedLayer, Is.EqualTo(0), "a consumed cancel must not keep trickling down");
        }

        [Test]
        public void WithConsumeEventOff_ThePressKeepsPropagating()
        {
            this.BuildDocument();

            var reachedLayer = 0;
            this.rootUIDocument.RootUIOverlayElement.RegisterCallback<NavigationCancelEvent>(_ => ++reachedLayer);

            using var source = new BackNavigationSource(this.rootUIDocument.RootVisualElement) { ConsumeEvent = false };
            source.BackRequested += () => { };

            this.SendCancel(this.rootUIDocument.RootUIOverlayElement);

            Assert.That(source.HandledCount, Is.EqualTo(1));
            Assert.That(reachedLayer, Is.EqualTo(1));
        }

        [Test]
        public void WhileDisabled_ItRaisesNothing()
        {
            this.BuildDocument();

            var requested = 0;

            using var source = new BackNavigationSource(this.rootUIDocument.RootVisualElement) { Enabled = false };
            source.BackRequested += () => ++requested;

            this.SendCancel();

            Assert.That(requested, Is.EqualTo(0));
        }

        [Test]
        public void AfterDispose_ItRaisesNothing()
        {
            this.BuildDocument();

            var requested = 0;

            var source = new BackNavigationSource(this.rootUIDocument.RootVisualElement);
            source.BackRequested += () => ++requested;

            source.Dispose();
            this.SendCancel();

            Assert.That(requested, Is.EqualTo(0));
        }

        [Test]
        public void DisposeIsIdempotent()
        {
            var source = new BackNavigationSource(new VisualElement());

            Assert.DoesNotThrow(() =>
            {
                source.Dispose();
                source.Dispose();
            });
        }

        [Test]
        public void ASourceOverANullRoot_Throws()
        {
            Assert.Throws<ArgumentNullException>(() => new BackNavigationSource(null));
        }

        [Test]
        public void ItKnowsNothingAboutWhatBackMeans()
        {
            // The inversion, asserted rather than asserted-about: the source exposes an
            // event and three knobs, and nothing that names a screen, a stack or a dialog.
            // A member that decided policy would show up here.
            var members = typeof(BackNavigationSource).GetMembers(
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.DeclaredOnly);

            foreach (var member in members)
            {
                Assert.That(member.Name, Does.Not.Contain("Screen").And.Not.Contain("Popup").And.Not.Contain("Close").And.Not.Contain("Quit"),
                    $"{member.Name} looks like policy, which belongs to the host.");
            }
        }

        #endregion

        #region Helpers

        /// <summary>Synthesises a cancel aimed at <paramref name="target"/>, or at the panel root.</summary>
        /// <remarks>
        /// What is synthesised is only the DISPATCH — that a cancel exists and is aimed
        /// somewhere. That a real Android back press or gamepad B produces one is Unity's
        /// input backend, needs a device, and is asserted nowhere in this file.
        /// </remarks>
        private void SendCancel(VisualElement target = null)
        {
            var root = this.rootUIDocument.RootVisualElement;

            using var evt = NavigationCancelEvent.GetPooled();
            evt.target = target ?? root;
            root.SendEvent(evt);
        }

        private void BuildDocument()
        {
            this.panelSettings = ScriptableObject.CreateInstance<PanelSettings>();

            // Inactive first: RootUIDocument reads the document's rootVisualElement in
            // Awake, which needs the UIDocument already configured.
            this.documentObject = new GameObject(nameof(RootUIDocument));
            this.documentObject.SetActive(false);

            var uiDocument = this.documentObject.AddComponent<UIDocument>();
            uiDocument.panelSettings   = this.panelSettings;
            uiDocument.visualTreeAsset = LoadUxml(RootUxmlPath);

            this.rootUIDocument = this.documentObject.AddComponent<RootUIDocument>();
            this.documentObject.SetActive(true);
        }

        private static VisualTreeAsset LoadUxml(string path)
        {
            #if UNITY_EDITOR
            var asset = UnityEditor.AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(path);
            Assert.That(asset, Is.Not.Null, $"Could not load {path}.");
            return asset;
            #else
            Assert.Ignore("These tests load their UXML through the AssetDatabase and only run in the Editor.");
            return null;
            #endif
        }

        #endregion
    }
}
