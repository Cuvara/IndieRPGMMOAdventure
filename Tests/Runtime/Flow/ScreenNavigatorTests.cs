namespace Cuvara.UIToolkit.Flow.Tests
{
    using System;
    using System.Collections;
    using System.Collections.Generic;
    using System.Threading;
    using Cuvara.UIToolkit.Core;
    using Cuvara.UIToolkit.Flow;
    using Cuvara.UIToolkit.Managers;
    using Cuvara.UIToolkit.View;
    using Cysharp.Threading.Tasks;
    using NUnit.Framework;
    using UnityEngine.TestTools;
    using UnityEngine.UIElements;

    #region Doubles

    /// <summary>A loader over a single asset, with a switch to make it fail.</summary>
    internal sealed class OneAssetLoader : IVisualTreeAssetLoader
    {
        private readonly VisualTreeAsset asset;

        public bool FailNext { get; set; }

        public OneAssetLoader(VisualTreeAsset asset) { this.asset = asset; }

        public UniTask<VisualTreeAsset> LoadAsync(string key)
        {
            if (this.FailNext) throw new KeyNotFoundException($"no asset for '{key}'");

            return UniTask.FromResult(this.asset);
        }
    }

    /// <summary>
    /// A scope factory backed by a dictionary, counting how many scopes were disposed.
    /// </summary>
    /// <remarks>
    /// Fifteen lines, no container. That the navigator's disposal guarantees can be ASSERTED
    /// rather than argued is the entire reason it talks to <see cref="IScreenScopeFactory"/>
    /// instead of naming a DI framework.
    /// </remarks>
    internal sealed class FakeScopeFactory : IScreenScopeFactory
    {
        private readonly Dictionary<Type, Func<object>> factories = new();

        public int Created { get; private set; }

        public int Disposed { get; private set; }

        public void Bind<T>(Func<object> factory) { this.factories[typeof(T)] = factory; }

        public IScreenScope CreateScreenScope()
        {
            ++this.Created;
            return new FakeScope(this);
        }

        private sealed class FakeScope : IScreenScope
        {
            private readonly FakeScopeFactory owner;
            private bool disposed;

            public FakeScope(FakeScopeFactory owner) { this.owner = owner; }

            public object Resolve(Type type)
            {
                if (this.disposed) throw new ObjectDisposedException(nameof(FakeScope));

                return this.owner.factories.TryGetValue(type, out var factory)
                    ? factory()
                    : throw new InvalidOperationException($"nothing bound for {type.Name}");
            }

            public void Dispose()
            {
                if (this.disposed) return;
                this.disposed = true;
                ++this.owner.Disposed;
            }
        }
    }

    internal interface ITestScreenView : IUIToolkitView
    {
    }

    internal sealed class TestScreenView : BaseUIToolkitView, ITestScreenView
    {
        public TestScreenView(VisualTreeAsset asset) : base(asset) { this.StretchToParent(); }
    }

    internal class TestScreenPresenter : BaseUIToolkitScreenPresenter<ITestScreenView>
    {
        public int BindCount, ActivateCount, DeactivateCount, SuspendCount, ResumeCount;

        public ScreenSubscriptions LastSubscriptions;

        public bool ConsumeBack;

        protected override UniTask OnBindAsync(ScreenSubscriptions subscriptions, CancellationToken cancellationToken)
        {
            ++this.BindCount;
            this.LastSubscriptions = subscriptions;
            return UniTask.CompletedTask;
        }

        protected override void OnActivate() => ++this.ActivateCount;

        protected override void OnDeactivate() => ++this.DeactivateCount;

        protected override void OnSuspend() => ++this.SuspendCount;

        protected override void OnResume() => ++this.ResumeCount;

        protected override bool OnBackRequested() => this.ConsumeBack;
    }

    internal sealed class SecondScreenPresenter : TestScreenPresenter
    {
    }

    internal sealed class ModalPresenter : TestScreenPresenter
    {
    }

    internal sealed class FailingPresenter : BaseUIToolkitScreenPresenter<ITestScreenView>
    {
        protected override UniTask OnBindAsync(ScreenSubscriptions subscriptions, CancellationToken cancellationToken)
        {
            throw new InvalidOperationException("bind failed on purpose");
        }
    }

    internal sealed class ModelPresenter : BaseUIToolkitScreenPresenter<ITestScreenView, string>
    {
        public string Received;

        protected override UniTask OnBindAsync(string model, ScreenSubscriptions subscriptions, CancellationToken cancellationToken)
        {
            this.Received = model;
            return UniTask.CompletedTask;
        }
    }

    #endregion

    /// <summary>
    /// The navigator: the stack, the scopes, and what Back means.
    /// </summary>
    /// <remarks>
    /// Headless. No scene, no <c>UIDocument</c>, no panel, no container — layers are plain
    /// detached <c>VisualElement</c>s and scopes are a dictionary. That is not a shortcut: a
    /// navigator whose disposal behaviour could only be observed inside a real container would be
    /// one whose central guarantee was argued rather than asserted.
    /// </remarks>
    public class ScreenNavigatorTests
    {
        private const string ViewUxmlPath = "Packages/com.cuvara.uitoolkit/Tests/Runtime/TestView.uxml";

        private const string ScreenKey = "screen";
        private const string SecondKey = "second";
        private const string ModalKey  = "modal";

        private VisualElement    showLayer, hiddenLayer, overlayLayer;
        private FakeScopeFactory scopes;
        private ScreenRegistry   registry;
        private OneAssetLoader   loader;
        private ScreenNavigator  nav;

        [SetUp]
        public void SetUp()
        {
            this.showLayer    = new();
            this.hiddenLayer  = new();
            this.overlayLayer = new();

            this.loader   = new(LoadUxml(ViewUxmlPath));
            this.scopes   = new();
            this.registry = new();

            this.registry.Register(typeof(TestScreenPresenter), typeof(TestScreenView), ScreenKey);
            this.registry.Register(typeof(SecondScreenPresenter), typeof(TestScreenView), SecondKey);
            this.registry.Register(typeof(ModalPresenter), typeof(TestScreenView), ModalKey, ScreenOptions.Modal);
            this.registry.Register(typeof(FailingPresenter), typeof(TestScreenView), ScreenKey);
            this.registry.Register(typeof(ModelPresenter), typeof(TestScreenView), ScreenKey);

            this.scopes.Bind<TestScreenPresenter>(() => new TestScreenPresenter());
            this.scopes.Bind<SecondScreenPresenter>(() => new SecondScreenPresenter());
            this.scopes.Bind<ModalPresenter>(() => new ModalPresenter());
            this.scopes.Bind<FailingPresenter>(() => new FailingPresenter());
            this.scopes.Bind<ModelPresenter>(() => new ModelPresenter());

            this.nav = new(
                this.registry,
                this.scopes,
                new UIToolkitViewFactory(this.loader),
                new ViewLayers(new VisualElementViewLayer(this.showLayer),
                               new VisualElementViewLayer(this.hiddenLayer),
                               new VisualElementViewLayer(this.overlayLayer)));
        }

        [TearDown]
        public void TearDown() { this.nav?.Dispose(); }

        #region Push

        [UnityTest]
        public IEnumerator PushOpensAScreenIntoTheShowLayer() => UniTask.ToCoroutine(async () =>
        {
            var presenter = await this.nav.PushAsync<TestScreenPresenter>();

            Assert.That(this.nav.Depth, Is.EqualTo(1));
            Assert.That(this.nav.Top, Is.SameAs(presenter));
            Assert.That(presenter.State, Is.EqualTo(ScreenLifecycleState.Active));
            Assert.That(presenter.BindCount, Is.EqualTo(1));
            Assert.That(presenter.ActivateCount, Is.EqualTo(1));
            Assert.That(this.showLayer.childCount, Is.EqualTo(1));
        });

        [UnityTest]
        public IEnumerator PushingASecondScreenSuspendsTheFirst() => UniTask.ToCoroutine(async () =>
        {
            var first = await this.nav.PushAsync<TestScreenPresenter>();
            var second = await this.nav.PushAsync<SecondScreenPresenter>();

            Assert.That(this.nav.Depth, Is.EqualTo(2));
            Assert.That(first.State, Is.EqualTo(ScreenLifecycleState.Suspended));
            Assert.That(second.State, Is.EqualTo(ScreenLifecycleState.Active));
            Assert.That(first.SuspendCount, Is.EqualTo(1));
            Assert.That(first.DeactivateCount, Is.EqualTo(1));
            Assert.That(this.hiddenLayer.childCount, Is.EqualTo(1), "the suspended screen moves to the hidden layer");
        });

        [UnityTest]
        public IEnumerator ASuspendedScreenIsNotDisposed() => UniTask.ToCoroutine(async () =>
        {
            // The hazard this whole layer is written against: in the old framework, hiding a
            // screen called Dispose() on an object that kept living.
            await this.nav.PushAsync<TestScreenPresenter>();
            await this.nav.PushAsync<SecondScreenPresenter>();

            Assert.That(this.scopes.Disposed, Is.Zero, "suspending must not dispose a scope");
        });

        [UnityTest]
        public IEnumerator AModelIsDeliveredToTheScreen() => UniTask.ToCoroutine(async () =>
        {
            var presenter = await this.nav.PushAsync<ModelPresenter, string>("hello");

            Assert.That(presenter.Received, Is.EqualTo("hello"));
        });

        #endregion

        #region Pop

        [UnityTest]
        public IEnumerator PopDisposesTheScopeAndResumesBelow() => UniTask.ToCoroutine(async () =>
        {
            var first = await this.nav.PushAsync<TestScreenPresenter>();
            var second = await this.nav.PushAsync<SecondScreenPresenter>();

            await this.nav.PopAsync();

            Assert.That(this.nav.Depth, Is.EqualTo(1));
            Assert.That(this.nav.Top, Is.SameAs(first));
            Assert.That(second.State, Is.EqualTo(ScreenLifecycleState.Disposed));
            Assert.That(this.scopes.Disposed, Is.EqualTo(1), "exactly the popped screen's scope");
            Assert.That(first.State, Is.EqualTo(ScreenLifecycleState.Active));
            Assert.That(first.ResumeCount, Is.EqualTo(1));
        });

        [UnityTest]
        public IEnumerator PopReleasesTheScreenSubscriptions() => UniTask.ToCoroutine(async () =>
        {
            // The author writes no teardown. This is what makes that true.
            var presenter = await this.nav.PushAsync<TestScreenPresenter>();
            presenter.LastSubscriptions.AddAction(() => { });

            Assert.That(presenter.LastSubscriptions.LiveCount, Is.EqualTo(1), "precondition");

            await this.nav.PopAsync();

            Assert.That(presenter.LastSubscriptions.IsDisposed, Is.True);
            Assert.That(presenter.LastSubscriptions.LiveCount, Is.Zero);
        });

        [UnityTest]
        public IEnumerator PopDetachesTheViewFromItsLayer() => UniTask.ToCoroutine(async () =>
        {
            await this.nav.PushAsync<TestScreenPresenter>();
            Assert.That(this.showLayer.childCount, Is.EqualTo(1), "precondition");

            await this.nav.PopAsync();

            Assert.That(this.showLayer.childCount, Is.Zero);
        });

        [UnityTest]
        public IEnumerator PopOnAnEmptyStackDoesNothing() => UniTask.ToCoroutine(async () =>
        {
            await this.nav.PopAsync();

            Assert.That(this.nav.Depth, Is.Zero);
        });

        [UnityTest]
        public IEnumerator PopAllClosesEverything() => UniTask.ToCoroutine(async () =>
        {
            await this.nav.PushAsync<TestScreenPresenter>();
            await this.nav.PushAsync<SecondScreenPresenter>();

            await this.nav.PopAllAsync();

            Assert.That(this.nav.Depth, Is.Zero);
            Assert.That(this.scopes.Disposed, Is.EqualTo(2));
        });

        [UnityTest]
        public IEnumerator PopToRootLeavesOne() => UniTask.ToCoroutine(async () =>
        {
            var first = await this.nav.PushAsync<TestScreenPresenter>();
            await this.nav.PushAsync<SecondScreenPresenter>();

            await this.nav.PopToRootAsync();

            Assert.That(this.nav.Depth, Is.EqualTo(1));
            Assert.That(this.nav.Top, Is.SameAs(first));
            Assert.That(first.State, Is.EqualTo(ScreenLifecycleState.Active));
        });

        #endregion

        #region Replace

        [UnityTest]
        public IEnumerator ReplaceNeverResumesWhatIsBelow() => UniTask.ToCoroutine(async () =>
        {
            // Resuming the screen below between the close and the open would flash it into view
            // for a frame. This is the assertion that pins the ordering.
            var bottom = await this.nav.PushAsync<TestScreenPresenter>();
            await this.nav.PushAsync<SecondScreenPresenter>();

            await this.nav.ReplaceAsync<ModalPresenter>();

            Assert.That(bottom.ResumeCount, Is.Zero, "the screen below must never have been resumed");
            Assert.That(this.nav.Depth, Is.EqualTo(2));
        });

        #endregion

        #region Modals

        [UnityTest]
        public IEnumerator AModalGoesIntoTheOverlayLayer() => UniTask.ToCoroutine(async () =>
        {
            await this.nav.PushAsync<ModalPresenter>();

            Assert.That(this.overlayLayer.childCount, Is.EqualTo(1));
            Assert.That(this.showLayer.childCount, Is.Zero);
        });

        [UnityTest]
        public IEnumerator AnOpaqueModalSuspendsWhatIsBelow() => UniTask.ToCoroutine(async () =>
        {
            var below = await this.nav.PushAsync<TestScreenPresenter>();

            await this.nav.PushAsync<ModalPresenter>();

            Assert.That(below.State, Is.EqualTo(ScreenLifecycleState.Suspended));
        });

        [UnityTest]
        public IEnumerator ADimmingModalLeavesWhatIsBelowActiveButNotInteractive() => UniTask.ToCoroutine(async () =>
        {
            // The behaviour test for ScreenOptions.DimsBelow. A dialog over a live HUD that froze
            // the HUD would look broken; one that left it clickable would be worse.
            this.registry.Register(typeof(DimmingModalPresenter), typeof(TestScreenView), ModalKey,
                ScreenOptions.Modal | ScreenOptions.DimsBelow);
            this.scopes.Bind<DimmingModalPresenter>(() => new DimmingModalPresenter());

            var below = await this.nav.PushAsync<TestScreenPresenter>();

            await this.nav.PushAsync<DimmingModalPresenter>();

            Assert.That(below.State, Is.EqualTo(ScreenLifecycleState.Active), "DimsBelow must not suspend");
            Assert.That(below.SuspendCount, Is.Zero);
            Assert.That(((IUIToolkitScreenPresenter)below).View.Root.pickingMode, Is.EqualTo(PickingMode.Ignore),
                "DimsBelow must stop the screen below being interactive");
        });

        [UnityTest]
        public IEnumerator ClosingADimmingModalMakesWhatIsBelowInteractiveAgain() => UniTask.ToCoroutine(async () =>
        {
            this.registry.Register(typeof(DimmingModalPresenter), typeof(TestScreenView), ModalKey,
                ScreenOptions.Modal | ScreenOptions.DimsBelow);
            this.scopes.Bind<DimmingModalPresenter>(() => new DimmingModalPresenter());

            var below = await this.nav.PushAsync<TestScreenPresenter>();
            await this.nav.PushAsync<DimmingModalPresenter>();

            await this.nav.PopAsync();

            Assert.That(((IUIToolkitScreenPresenter)below).View.Root.pickingMode, Is.EqualTo(PickingMode.Position));
        });

        internal sealed class DimmingModalPresenter : TestScreenPresenter
        {
        }

        #endregion

        #region A failed open

        [UnityTest]
        public IEnumerator AFailedBindLeavesTheStackUntouched() => UniTask.ToCoroutine(async () =>
        {
            var thrown = false;

            try
            {
                await this.nav.PushAsync<FailingPresenter>();
            }
            catch (InvalidOperationException)
            {
                thrown = true;
            }

            Assert.That(thrown, Is.True, "the exception must reach the caller");
            Assert.That(this.nav.Depth, Is.Zero, "there is no such thing as a half-open screen");
            Assert.That(this.scopes.Disposed, Is.EqualTo(1), "the half-built scope must be released");
            Assert.That(this.showLayer.childCount, Is.Zero, "nothing may be left parented");
        });

        [UnityTest]
        public IEnumerator AFailedLoadLeavesTheStackUntouched() => UniTask.ToCoroutine(async () =>
        {
            this.loader.FailNext = true;

            try
            {
                await this.nav.PushAsync<TestScreenPresenter>();
            }
            catch (KeyNotFoundException)
            {
            }

            Assert.That(this.nav.Depth, Is.Zero);
            Assert.That(this.scopes.Disposed, Is.EqualTo(1));
        });

        [UnityTest]
        public IEnumerator AFailedPushDoesNotDisturbAnExistingScreen() => UniTask.ToCoroutine(async () =>
        {
            var first = await this.nav.PushAsync<TestScreenPresenter>();

            try
            {
                await this.nav.PushAsync<FailingPresenter>();
            }
            catch (InvalidOperationException)
            {
            }

            Assert.That(this.nav.Depth, Is.EqualTo(1));
            Assert.That(this.nav.Top, Is.SameAs(first));
            Assert.That(first.State, Is.EqualTo(ScreenLifecycleState.Active), "the screen below must not have been suspended");
            Assert.That(first.SuspendCount, Is.Zero);
        });

        [Test]
        public void PushingAnUnregisteredScreenSaysWhatToDoAboutIt()
        {
            var exception = Assert.Throws<InvalidOperationException>(() => this.registry.Get(typeof(string)));

            Assert.That(exception.Message, Does.Contain("RegisterScreen"), "the message must name the fix");
        }

        #endregion

        #region Back

        [UnityTest]
        public IEnumerator BackPopsWhenThereIsSomethingUnderneath() => UniTask.ToCoroutine(async () =>
        {
            var first = await this.nav.PushAsync<TestScreenPresenter>();
            await this.nav.PushAsync<SecondScreenPresenter>();

            var handled = this.nav.HandleBack();
            await UniTask.DelayFrame(2);

            Assert.That(handled, Is.True);
            Assert.That(this.nav.Depth, Is.EqualTo(1));
            Assert.That(this.nav.Top, Is.SameAs(first));
        });

        [UnityTest]
        public IEnumerator BackAtTheRootIsNotHandledByDefault() => UniTask.ToCoroutine(async () =>
        {
            // The default exists so the platform's own Back still runs. On Android, reporting
            // handled here is the app silently ceasing to exit.
            await this.nav.PushAsync<TestScreenPresenter>();

            Assert.That(this.nav.RootBackPolicy, Is.EqualTo(RootBackPolicy.NotHandled));
            Assert.That(this.nav.HandleBack(), Is.False);
            Assert.That(this.nav.Depth, Is.EqualTo(1), "the root screen must not be popped");
        });

        [UnityTest]
        public IEnumerator BackAtTheRootCanConsume() => UniTask.ToCoroutine(async () =>
        {
            await this.nav.PushAsync<TestScreenPresenter>();
            this.nav.RootBackPolicy = RootBackPolicy.Consume;

            Assert.That(this.nav.HandleBack(), Is.True);
            Assert.That(this.nav.Depth, Is.EqualTo(1));
        });

        [UnityTest]
        public IEnumerator BackAtTheRootCanRaise() => UniTask.ToCoroutine(async () =>
        {
            await this.nav.PushAsync<TestScreenPresenter>();
            this.nav.RootBackPolicy = RootBackPolicy.Raise;

            var raised = 0;
            this.nav.RootBackRequested += () => ++raised;

            Assert.That(this.nav.HandleBack(), Is.True);
            Assert.That(raised, Is.EqualTo(1));
        });

        [Test]
        public void BackOnAnEmptyStackIsNeverHandled_WhateverThePolicy()
        {
            // The root policy is about the ROOT SCREEN, not about an empty stack. Consuming here
            // would swallow Back with no UI on screen at all — precisely the stranding that
            // NotHandled exists to prevent.
            foreach (var policy in new[] { RootBackPolicy.NotHandled, RootBackPolicy.Consume, RootBackPolicy.Raise })
            {
                this.nav.RootBackPolicy = policy;

                Assert.That(this.nav.HandleBack(), Is.False, $"with nothing open, {policy} must still not consume");
            }
        }

        [UnityTest]
        public IEnumerator TheTopScreenGetsFirstRefusalOnBack() => UniTask.ToCoroutine(async () =>
        {
            await this.nav.PushAsync<TestScreenPresenter>();
            var top = await this.nav.PushAsync<SecondScreenPresenter>();
            top.ConsumeBack = true;

            var handled = this.nav.HandleBack();
            await UniTask.DelayFrame(2);

            Assert.That(handled, Is.True);
            Assert.That(this.nav.Depth, Is.EqualTo(2), "the screen consumed Back, so nothing was popped");
        });

        #endregion

        #region Teardown

        [UnityTest]
        public IEnumerator DisposingTheNavigatorReleasesEveryScope() => UniTask.ToCoroutine(async () =>
        {
            await this.nav.PushAsync<TestScreenPresenter>();
            await this.nav.PushAsync<SecondScreenPresenter>();

            this.nav.Dispose();

            Assert.That(this.scopes.Disposed, Is.EqualTo(2));
            Assert.That(this.nav.Depth, Is.Zero);
        });

        [UnityTest]
        public IEnumerator PushingAfterDisposeThrows() => UniTask.ToCoroutine(async () =>
        {
            this.nav.Dispose();

            try
            {
                await this.nav.PushAsync<TestScreenPresenter>();
                Assert.Fail("expected ObjectDisposedException");
            }
            catch (ObjectDisposedException)
            {
            }
        });

        [Test]
        public void ConstructingWithNullsThrows()
        {
            var layers  = new ViewLayers(new VisualElementViewLayer(new VisualElement()), new VisualElementViewLayer(new VisualElement()), new VisualElementViewLayer(new VisualElement()));
            var factory = new UIToolkitViewFactory(this.loader);

            Assert.Throws<ArgumentNullException>(() => new ScreenNavigator(null, this.scopes, factory, layers));
            Assert.Throws<ArgumentNullException>(() => new ScreenNavigator(this.registry, null, factory, layers));
            Assert.Throws<ArgumentNullException>(() => new ScreenNavigator(this.registry, this.scopes, null, layers));
        }

        #endregion

        #region State

        [UnityTest]
        public IEnumerator StateOfReportsAScreenOnTheStack() => UniTask.ToCoroutine(async () =>
        {
            Assert.That(this.nav.StateOf<TestScreenPresenter>(), Is.Null, "not on the stack");

            await this.nav.PushAsync<TestScreenPresenter>();

            Assert.That(this.nav.StateOf<TestScreenPresenter>(), Is.EqualTo(ScreenLifecycleState.Active));

            await this.nav.PushAsync<SecondScreenPresenter>();

            Assert.That(this.nav.StateOf<TestScreenPresenter>(), Is.EqualTo(ScreenLifecycleState.Suspended));
        });

        [Test]
        public void RegisteringTheSameScreenTwiceIsRefused()
        {
            // Two registrations mean two asset keys for one screen, and which wins would depend
            // on registration order.
            Assert.Throws<InvalidOperationException>(() =>
                this.registry.Register(typeof(TestScreenPresenter), typeof(TestScreenView), "other"));
        }

        #endregion

        private static VisualTreeAsset LoadUxml(string path)
        {
            #if UNITY_EDITOR
            var asset = UnityEditor.AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(path);
            Assert.That(asset, Is.Not.Null, $"Could not load {path}.");
            return asset;
            #else
            Assert.Ignore("Loads its UXML through the AssetDatabase; Editor only.");
            return null;
            #endif
        }
    }
}
