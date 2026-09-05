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

    /// <summary>
    /// A presenter whose <c>OnBindAsync</c> can be held open, so a test can close the screen
    /// while its bind is still awaiting.
    /// </summary>
    /// <remarks>
    /// Every other presenter here binds synchronously — <c>UniTask.CompletedTask</c> — so the
    /// window between "bind started" and "bind finished" has never existed in a test. The
    /// cancellation path is real code that nothing has ever executed.
    /// </remarks>
    internal sealed class SlowBindPresenter : BaseUIToolkitScreenPresenter<ITestScreenView>
    {
        private readonly UniTaskCompletionSource gate = new();

        public bool              BindEntered;
        public bool              BindReturned;
        public bool              TokenWasCancelledDuringBind;
        public CancellationToken TokenSeen;

        /// <summary>Lets the held bind finish.</summary>
        public void Release() => this.gate.TrySetResult();

        protected override async UniTask OnBindAsync(ScreenSubscriptions subscriptions, CancellationToken cancellationToken)
        {
            this.BindEntered = true;
            this.TokenSeen   = cancellationToken;

            await this.gate.Task;

            this.TokenWasCancelledDuringBind = cancellationToken.IsCancellationRequested;
            this.BindReturned                = true;
        }
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

    /// <summary>Registers a subscription that throws when released.</summary>
    internal sealed class BadTeardownPresenter : BaseUIToolkitScreenPresenter<ITestScreenView>
    {
        protected override UniTask OnBindAsync(ScreenSubscriptions subscriptions, CancellationToken cancellationToken)
        {
            subscriptions.AddAction(() => throw new InvalidOperationException("teardown boom"));
            return UniTask.CompletedTask;
        }
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

        /// <summary>Captured as the scope factory creates it, so a test can reach the instance
        /// the navigator is using without the fake growing a lookup API it has no other use for.</summary>
        private SlowBindPresenter lastSlowBind;

        [SetUp]
        public void SetUp()
        {
            this.lastSlowBind = null;

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
            this.registry.Register(typeof(SlowBindPresenter), typeof(TestScreenView), ScreenKey);

            this.scopes.Bind<TestScreenPresenter>(() => new TestScreenPresenter());
            this.scopes.Bind<SecondScreenPresenter>(() => new SecondScreenPresenter());
            this.scopes.Bind<ModalPresenter>(() => new ModalPresenter());
            this.scopes.Bind<FailingPresenter>(() => new FailingPresenter());
            this.scopes.Bind<ModelPresenter>(() => new ModelPresenter());
            this.scopes.Bind<SlowBindPresenter>(() => this.lastSlowBind = new SlowBindPresenter());

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

        #region Cancellation while a bind is still in flight

        /// <summary>
        /// Disposing the navigator while a bind is awaiting must cancel that bind's token.
        /// </summary>
        /// <remarks>
        /// This path was designed and never demonstrated. Every other test in this file binds
        /// synchronously, so the window between "bind started" and "bind finished" has never
        /// existed, and the CancellationTokenSource the navigator creates per screen has never
        /// been observed doing anything.
        ///
        /// <para>It matters because the token is the only thing a screen author can honour. A
        /// long OnBindAsync — a server call, a large asset — that keeps running after its
        /// screen is gone will write into a disposed view when it returns, and the exception
        /// surfaces far from the navigation that caused it.</para>
        /// </remarks>
        [UnityTest]
        public IEnumerator DisposingWhileABindIsInFlightCancelsThatBindsToken() => UniTask.ToCoroutine(async () =>
        {
            SlowBindPresenter presenter = null;

            // Do NOT await: the push is deliberately left hanging inside OnBindAsync.
            var pushing = this.nav.PushAsync<SlowBindPresenter>();

            await UniTask.Yield();

            presenter = this.lastSlowBind;
            Assert.That(presenter, Is.Not.Null, "the presenter was never constructed");
            Assert.That(presenter.BindEntered, Is.True, "OnBindAsync was never entered, so nothing is in flight and this test proves nothing");
            Assert.That(presenter.BindReturned, Is.False, "the bind completed synchronously — the gate did not hold it");

            Assert.That(presenter.TokenSeen.IsCancellationRequested, Is.False,
                "the token was already cancelled before anything cancelled it");

            this.nav.Dispose();

            Assert.That(presenter.TokenSeen.IsCancellationRequested, Is.True,
                "disposing the navigator did not cancel the in-flight bind's token — a screen closed mid-bind keeps running and writes into a view that is gone");

            presenter.Release();
            try { await pushing; } catch { /* the push may fault; the token is what is under test */ }
        });

        #endregion

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

        #region CloseOnTapOutside

        [UnityTest]
        public IEnumerator CloseOnTapOutside_ScrimIsInsertedInOverlayLayer() => UniTask.ToCoroutine(async () =>
        {
            this.registry.Register(typeof(TapOutsideModalPresenter), typeof(TestScreenView), ModalKey,
                ScreenOptions.Modal | ScreenOptions.CloseOnTapOutside);
            this.scopes.Bind<TapOutsideModalPresenter>(() => new TapOutsideModalPresenter());

            await this.nav.PushAsync<TestScreenPresenter>();
            await this.nav.PushAsync<TapOutsideModalPresenter>();

            // Overlay should have: scrim + modal view
            Assert.That(this.overlayLayer.childCount, Is.EqualTo(2), "scrim + modal view");
            Assert.That(this.overlayLayer[0].name, Is.EqualTo("cuvara-scrim"));
        });

        [UnityTest]
        public IEnumerator CloseOnTapOutside_ScrimIsRemovedOnPop() => UniTask.ToCoroutine(async () =>
        {
            this.registry.Register(typeof(TapOutsideModalPresenter), typeof(TestScreenView), ModalKey,
                ScreenOptions.Modal | ScreenOptions.CloseOnTapOutside);
            this.scopes.Bind<TapOutsideModalPresenter>(() => new TapOutsideModalPresenter());

            await this.nav.PushAsync<TestScreenPresenter>();
            await this.nav.PushAsync<TapOutsideModalPresenter>();

            await this.nav.PopAsync();

            Assert.That(this.overlayLayer.childCount, Is.Zero, "scrim and view must both be gone");
            Assert.That(this.nav.Depth, Is.EqualTo(1));
        });

        [UnityTest]
        public IEnumerator CloseOnTapOutside_WithDimsBelow_ScrimIsSemiTransparent() => UniTask.ToCoroutine(async () =>
        {
            this.registry.Register(typeof(TapOutsideDimPresenter), typeof(TestScreenView), ModalKey,
                ScreenOptions.Modal | ScreenOptions.CloseOnTapOutside | ScreenOptions.DimsBelow);
            this.scopes.Bind<TapOutsideDimPresenter>(() => new TapOutsideDimPresenter());

            await this.nav.PushAsync<TapOutsideDimPresenter>();

            var scrim = this.overlayLayer[0];
            Assert.That(scrim.name, Is.EqualTo("cuvara-scrim"));
            Assert.That(scrim.resolvedStyle.backgroundColor.a, Is.GreaterThan(0f),
                "DimsBelow scrim must be semi-transparent");
        });

        internal sealed class TapOutsideModalPresenter : TestScreenPresenter { }
        internal sealed class TapOutsideDimPresenter : TestScreenPresenter { }

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
        public IEnumerator AThrowingSubscriptionDoesNotAbortTeardown() => UniTask.ToCoroutine(async () =>
        {
            // A throw halfway leaves the stack, the scope and the layer in a state nobody
            // designed. One leaked handler beats a half-torn-down stack.
            this.registry.Register(typeof(BadTeardownPresenter), typeof(TestScreenView), ScreenKey);
            this.scopes.Bind<BadTeardownPresenter>(() => new BadTeardownPresenter());

            LogAssert.ignoreFailingMessages = true;

            try
            {
                await this.nav.PushAsync<BadTeardownPresenter>();

                await this.nav.PopAsync();

                Assert.That(this.nav.Depth, Is.Zero, "the screen must still have left the stack");
                Assert.That(this.scopes.Disposed, Is.EqualTo(1), "the scope must still have been disposed");
                Assert.That(this.showLayer.childCount, Is.Zero, "the view must still have been detached");
            }
            finally
            {
                LogAssert.ignoreFailingMessages = false;
            }
        });

        [UnityTest]
        public IEnumerator ASwallowedTeardownExceptionIsCounted() => UniTask.ToCoroutine(async () =>
        {
            // Continuing past a failure is right; doing it invisibly is not. The count is what
            // makes "teardown swallowed something" observable instead of silent.
            this.registry.Register(typeof(BadTeardownPresenter), typeof(TestScreenView), ScreenKey);
            this.scopes.Bind<BadTeardownPresenter>(() => new BadTeardownPresenter());

            LogAssert.ignoreFailingMessages = true;

            try
            {
                Assert.That(this.nav.TeardownFailureCount, Is.Zero, "precondition");

                await this.nav.PushAsync<BadTeardownPresenter>();
                await this.nav.PopAsync();

                Assert.That(this.nav.TeardownFailureCount, Is.EqualTo(1));
            }
            finally
            {
                LogAssert.ignoreFailingMessages = false;
            }
        });

        [UnityTest]
        public IEnumerator OneBadScreenDoesNotSkipTheScreensBelowItOnDispose() => UniTask.ToCoroutine(async () =>
        {
            // The reason teardown must not propagate: an exception on the way out would abandon
            // every screen after it in the loop.
            this.registry.Register(typeof(BadTeardownPresenter), typeof(TestScreenView), ScreenKey);
            this.scopes.Bind<BadTeardownPresenter>(() => new BadTeardownPresenter());

            LogAssert.ignoreFailingMessages = true;

            try
            {
                await this.nav.PushAsync<TestScreenPresenter>();
                await this.nav.PushAsync<BadTeardownPresenter>();

                this.nav.Dispose();

                Assert.That(this.scopes.Disposed, Is.EqualTo(2), "the screen below the failing one must still be released");
            }
            finally
            {
                LogAssert.ignoreFailingMessages = false;
            }
        });


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

        #region Retain

        [UnityTest]
        public IEnumerator Retain_PopDoesNotDisposeTheScope() => UniTask.ToCoroutine(async () =>
        {
            this.registry.Register(typeof(RetainedPresenter), typeof(TestScreenView), ScreenKey,
                ScreenOptions.Retain);
            this.scopes.Bind<RetainedPresenter>(() => new RetainedPresenter());

            await this.nav.PushAsync<RetainedPresenter>();
            await this.nav.PopAsync();

            Assert.That(this.scopes.Disposed, Is.Zero, "a retained screen's scope must survive pop");
            Assert.That(this.nav.Depth, Is.Zero);
        });

        [UnityTest]
        public IEnumerator Retain_SecondPushReusesTheInstance() => UniTask.ToCoroutine(async () =>
        {
            this.registry.Register(typeof(RetainedPresenter), typeof(TestScreenView), ScreenKey,
                ScreenOptions.Retain);
            var instance = new RetainedPresenter();
            this.scopes.Bind<RetainedPresenter>(() => instance);

            var first = await this.nav.PushAsync<RetainedPresenter>();
            await this.nav.PopAsync();
            var second = await this.nav.PushAsync<RetainedPresenter>();

            Assert.That(second, Is.SameAs(first), "the retained instance must be reused");
        });

        [UnityTest]
        public IEnumerator Retain_OnBindAsyncReRunsOnEachPush() => UniTask.ToCoroutine(async () =>
        {
            this.registry.Register(typeof(RetainedPresenter), typeof(TestScreenView), ScreenKey,
                ScreenOptions.Retain);
            this.scopes.Bind<RetainedPresenter>(() => new RetainedPresenter());

            var presenter = await this.nav.PushAsync<RetainedPresenter>();
            Assert.That(presenter.BindCount, Is.EqualTo(1));

            await this.nav.PopAsync();
            await this.nav.PushAsync<RetainedPresenter>();

            Assert.That(presenter.BindCount, Is.EqualTo(2), "OnBindAsync must re-run on every push");
        });

        [UnityTest]
        public IEnumerator Retain_DisposeReleasesRetainedEntries() => UniTask.ToCoroutine(async () =>
        {
            this.registry.Register(typeof(RetainedPresenter), typeof(TestScreenView), ScreenKey,
                ScreenOptions.Retain);
            this.scopes.Bind<RetainedPresenter>(() => new RetainedPresenter());

            await this.nav.PushAsync<RetainedPresenter>();
            await this.nav.PopAsync();

            this.nav.Dispose();

            Assert.That(this.scopes.Disposed, Is.EqualTo(1), "navigator dispose must release retained scopes");
        });

        internal sealed class RetainedPresenter : TestScreenPresenter { }

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
