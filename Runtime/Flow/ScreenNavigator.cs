namespace Cuvara.UIToolkit.Flow
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using Cuvara.UIToolkit.Core;
    using Cuvara.UIToolkit.Managers;
    using Cuvara.UIToolkit.View;
    using Cysharp.Threading.Tasks;

    /// <summary>
    /// The stack, and the operations that move screens on and off it.
    /// </summary>
    /// <remarks>
    /// <para><b>One screen, one scope.</b> Every push creates a container scope; every pop
    /// disposes it. The presenter, the view, the subscriptions and any screen-scoped services go
    /// together, at a single moment, without a screen author writing any of it. That is the whole
    /// lifetime story — there is no cache of retained presenters, no reopen path, and no object
    /// that is disposed and then used again.</para>
    ///
    /// <para><b>Operations are serialised.</b> Every mutating method takes a gate first. Two taps
    /// on two buttons queue rather than interleave, which will occasionally read as input lag and
    /// is still right: concurrent stack mutation gives orderings nobody can reason about, and
    /// "what is on top" stops having an answer mid-operation.</para>
    ///
    /// <para><b>A failed open leaves nothing behind.</b> If the asset is missing, the view has no
    /// usable constructor, the presenter cannot be resolved, or <c>OnBindAsync</c> throws, the
    /// half-built scope is disposed and the stack is untouched. The exception reaches the caller
    /// unchanged. There is no state in which a screen is half-open.</para>
    ///
    /// <para><b>It knows nothing about input.</b> Back arrives through <see cref="HandleBack"/>,
    /// a plain method returning whether the navigator acted. A host wires whatever raises Back to
    /// it. That keeps the input backend a host choice and makes the entire back policy testable
    /// with no panel and no focus controller.</para>
    /// </remarks>
    public sealed class ScreenNavigator : IScreenNavigator, IDisposable
    {
        private sealed class ScreenEntry
        {
            public IScreenScope               Scope;
            public IUIToolkitScreenPresenter  Presenter;
            public IUIToolkitView             View;
            public ScreenSubscriptions        Subscriptions;
            public CancellationTokenSource    Cancellation;
            public ScreenOptions              Options;

            public bool IsModal => (this.Options & ScreenOptions.Modal) != 0;

            public bool LeavesBelowActive => this.IsModal && (this.Options & ScreenOptions.DimsBelow) != 0;
        }

        private readonly ScreenRegistry         registry;
        private readonly IScreenScopeFactory    scopeFactory;
        private readonly UIToolkitViewFactory   viewFactory;
        private readonly Func<ViewLayers>       layersProvider;

        private readonly List<ScreenEntry> stack = new();

        private bool busy;
        private bool disposed;

        /// <summary>Builds a navigator over layers that are read fresh on every use.</summary>
        /// <remarks>
        /// <para><b>A provider rather than a value, and this was a real bug rather than a
        /// precaution.</b> Taking <see cref="ViewLayers"/> by value captures whatever the layers
        /// were at construction time — and the navigator is a container singleton, so
        /// construction happens once, at whatever moment something first resolves it. If that
        /// moment precedes <c>UIDocument.OnEnable</c>, the navigator holds three null layers for
        /// the rest of the session and every push dies inside <c>SetParent</c> with a
        /// NullReferenceException naming neither the document nor the ordering.</para>
        ///
        /// <para>Reading through a delegate costs one indirection per reparent and removes the
        /// entire class of problem. Found by running the sample scene; no headless test could
        /// see it, because they all construct the navigator with layers that already exist.</para>
        /// </remarks>
        public ScreenNavigator(
            ScreenRegistry       registry,
            IScreenScopeFactory  scopeFactory,
            UIToolkitViewFactory viewFactory,
            Func<ViewLayers>     layersProvider)
        {
            this.registry       = registry ?? throw new ArgumentNullException(nameof(registry));
            this.scopeFactory   = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
            this.viewFactory    = viewFactory ?? throw new ArgumentNullException(nameof(viewFactory));
            this.layersProvider = layersProvider ?? throw new ArgumentNullException(nameof(layersProvider));
        }

        /// <summary>Builds a navigator over layers that already exist. For tests.</summary>
        public ScreenNavigator(
            ScreenRegistry       registry,
            IScreenScopeFactory  scopeFactory,
            UIToolkitViewFactory viewFactory,
            ViewLayers           layers)
            : this(registry, scopeFactory, viewFactory, () => layers)
        {
        }

        private ViewLayers Layers => this.layersProvider();

        public int Depth => this.stack.Count;

        public IUIToolkitScreenPresenter Top => this.stack.Count == 0 ? null : this.stack[^1].Presenter;

        public bool IsBusy => this.busy;

        /// <summary>
        /// How many exceptions teardown caught and continued past.
        /// </summary>
        /// <remarks>
        /// <para><b>Teardown never aborts, and never propagates.</b> A throw halfway through
        /// leaves the stack, the scope and the layer in a state nobody designed — and the screen
        /// that failed to release is the least of it, because every screen after it in the
        /// teardown loop is then skipped. One leaked handler is a far better outcome than a
        /// half-torn-down stack.</para>
        ///
        /// <para>But swallowing silently is the failure mode on the other side, so every caught
        /// exception is logged with the screen that produced it and counted here. "Teardown
        /// swallowed three exceptions" is then observable in a test rather than invisible.</para>
        ///
        /// <para><see cref="ScreenSubscriptions"/> still aggregates and rethrows to ITS caller —
        /// that type reporting the truth is right. This is the layer that decides not to let it
        /// escape.</para>
        /// </remarks>
        public int TeardownFailureCount { get; private set; }

        public RootBackPolicy RootBackPolicy { get; set; } = RootBackPolicy.NotHandled;

        public event Action RootBackRequested;

        public event Action<IUIToolkitScreenPresenter> ScreenActivated;

        public event Action<IUIToolkitScreenPresenter> ScreenDeactivated;

        #region Push

        public UniTask<TPresenter> PushAsync<TPresenter>() where TPresenter : class, IUIToolkitScreenPresenter
        {
            return this.OpenAsync<TPresenter>(replaceTop: false, applyModel: null);
        }

        public UniTask<TPresenter> PushAsync<TPresenter, TModel>(TModel model) where TPresenter : class, IUIToolkitScreenPresenter<TModel>
        {
            return this.OpenAsync<TPresenter>(replaceTop: false, applyModel: presenter => ApplyModel(presenter, model));
        }

        public UniTask<TPresenter> ReplaceAsync<TPresenter>() where TPresenter : class, IUIToolkitScreenPresenter
        {
            return this.OpenAsync<TPresenter>(replaceTop: true, applyModel: null);
        }

        public UniTask<TPresenter> ReplaceAsync<TPresenter, TModel>(TModel model) where TPresenter : class, IUIToolkitScreenPresenter<TModel>
        {
            return this.OpenAsync<TPresenter>(replaceTop: true, applyModel: presenter => ApplyModel(presenter, model));
        }

        private static void ApplyModel<TModel>(object presenter, TModel model)
        {
            // The model lives on the two-parameter base, which the navigator does not name in its
            // generic signature — TPresenter is constrained to the interface, not to the base
            // class, so that a host is free to implement the interface directly. This is the one
            // place the two meet.
            if (presenter is not IModelReceiver<TModel> receiver)
            {
                throw new InvalidOperationException(
                    $"{presenter.GetType().Name} was pushed with a model of type {typeof(TModel).Name}, but does not accept one. "
                    + "Derive from BaseUIToolkitScreenPresenter<TView, TModel> to take a model.");
            }

            receiver.ReceiveModel(model);
        }

        private async UniTask<TPresenter> OpenAsync<TPresenter>(bool replaceTop, Action<object> applyModel)
            where TPresenter : class, IUIToolkitScreenPresenter
        {
            this.ThrowIfDisposed();
            await this.AcquireGateAsync();

            try
            {
                var registration = this.registry.Get(typeof(TPresenter));

                // Replace closes the outgoing screen BEFORE building the new one, so what is
                // underneath is never resumed and therefore never flashes into view for a frame.
                if (replaceTop && this.stack.Count > 0) await this.CloseTopAsync(resumeBelow: false);

                var entry = await this.BuildAsync(registration, applyModel);

                this.SuspendOrDeactivateBelow(entry);

                this.stack.Add(entry);

                await this.OpenEntryAsync(entry);

                return (TPresenter)entry.Presenter;
            }
            finally
            {
                this.busy = false;
            }
        }

        private async UniTask<ScreenEntry> BuildAsync(ScreenRegistration registration, Action<object> applyModel)
        {
            var scope        = this.scopeFactory.CreateScreenScope();
            var cancellation = new CancellationTokenSource();

            var entry = new ScreenEntry
            {
                Scope        = scope,
                Cancellation = cancellation,
                Options      = registration.Options,
            };

            try
            {
                var view = await this.viewFactory.CreateAsync(registration.ViewType, registration.AssetKey);
                entry.View = view;

                var presenter = scope.Resolve(registration.PresenterType);

                if (presenter is not IUIToolkitScreenPresenter screenPresenter)
                {
                    throw new InvalidOperationException(
                        $"{registration.PresenterType.Name} was resolved but is not an {nameof(IUIToolkitScreenPresenter)}.");
                }

                entry.Presenter = screenPresenter;

                Attach(screenPresenter, view, this, registration.Options);
                SetState(screenPresenter, ScreenLifecycleState.Constructed);

                applyModel?.Invoke(presenter);

                entry.Subscriptions = new();
                SetState(screenPresenter, ScreenLifecycleState.Binding);

                await Bind(screenPresenter, entry.Subscriptions, cancellation.Token);

                return entry;
            }
            catch
            {
                // A half-built screen never reaches the stack. Everything created so far is
                // released here, in the order that leaves nothing holding anything, and the
                // exception continues to the caller untouched.
                this.DisposeEntry(entry);
                throw;
            }
        }

        private async UniTask OpenEntryAsync(ScreenEntry entry)
        {
            var layer = entry.IsModal ? this.Layers.Overlay : this.Layers.Screen;

            entry.View.ViewSurface.SetParent(layer);

            SetState(entry.Presenter, ScreenLifecycleState.Opening);

            try
            {
                await entry.View.Open();
            }
            finally
            {
                // A transition library that throws must not leave a screen invisible and
                // uninteractive with no way back. Landing in Active with the view shown is the
                // only recovery that does not strand the user.
                entry.View.Show();
                SetState(entry.Presenter, ScreenLifecycleState.Active);
            }

            Activate(entry.Presenter);
            this.ScreenActivated?.Invoke(entry.Presenter);
        }

        private void SuspendOrDeactivateBelow(ScreenEntry incoming)
        {
            if (this.stack.Count == 0) return;

            var below = this.stack[^1];

            this.ScreenDeactivated?.Invoke(below.Presenter);
            Deactivate(below.Presenter);

            // A dimming modal deliberately leaves what is below Active — still rendering, still
            // receiving pushes — because a dialog over a live HUD that froze the HUD would look
            // broken. It stops being interactive instead.
            if (incoming.LeavesBelowActive)
            {
                SetPickable(below, false);
                return;
            }

            this.Suspend(below);
        }

        #endregion

        #region Pop

        public async UniTask PopAsync()
        {
            this.ThrowIfDisposed();
            await this.AcquireGateAsync();

            try
            {
                if (this.stack.Count == 0) return;

                await this.CloseTopAsync(resumeBelow: true);
            }
            finally
            {
                this.busy = false;
            }
        }

        public async UniTask PopToRootAsync()
        {
            this.ThrowIfDisposed();
            await this.AcquireGateAsync();

            try
            {
                while (this.stack.Count > 1) await this.CloseTopAsync(resumeBelow: this.stack.Count == 2);
            }
            finally
            {
                this.busy = false;
            }
        }

        public async UniTask PopAllAsync()
        {
            this.ThrowIfDisposed();
            await this.AcquireGateAsync();

            try
            {
                while (this.stack.Count > 0) await this.CloseTopAsync(resumeBelow: false);
            }
            finally
            {
                this.busy = false;
            }
        }

        private async UniTask CloseTopAsync(bool resumeBelow)
        {
            var entry = this.stack[^1];
            this.stack.RemoveAt(this.stack.Count - 1);

            this.ScreenDeactivated?.Invoke(entry.Presenter);
            Deactivate(entry.Presenter);

            SetState(entry.Presenter, ScreenLifecycleState.Closing);

            try
            {
                await entry.View.Close();
            }
            finally
            {
                // Teardown never aborts. A failing outro must not leave a screen on screen with
                // its scope disposed, so the detach and the disposal happen regardless.
                this.DisposeEntry(entry);
            }

            if (this.stack.Count == 0) return;

            var below = this.stack[^1];

            SetPickable(below, true);

            if (!resumeBelow) return;

            if (below.Presenter.State == ScreenLifecycleState.Suspended) this.Resume(below);

            Activate(below.Presenter);
            this.ScreenActivated?.Invoke(below.Presenter);
        }

        #endregion

        #region Suspend and resume

        private void Suspend(ScreenEntry entry)
        {
            entry.View.ViewSurface.SetParent(this.Layers.Hidden);
            entry.View.Hide();

            SetState(entry.Presenter, ScreenLifecycleState.Suspended);
            SuspendPresenter(entry.Presenter);
        }

        private void Resume(ScreenEntry entry)
        {
            entry.View.ViewSurface.SetParent(entry.IsModal ? this.Layers.Overlay : this.Layers.Screen);
            entry.View.Show();

            SetState(entry.Presenter, ScreenLifecycleState.Active);
            ResumePresenter(entry.Presenter);
        }

        private static void SetPickable(ScreenEntry entry, bool pickable)
        {
            if (entry.View?.Root == null) return;

            entry.View.Root.pickingMode = pickable
                ? UnityEngine.UIElements.PickingMode.Position
                : UnityEngine.UIElements.PickingMode.Ignore;
        }

        #endregion

        #region Back

        public bool HandleBack()
        {
            if (this.disposed) return false;

            // A press during a push or pop is consumed and dropped, deliberately. Letting it
            // queue means a double-tap pops two screens, which is the single most common
            // navigation complaint there is.
            if (this.busy) return true;

            if (this.stack.Count > 0)
            {
                // The screen on top gets first refusal — a dropdown to close, an edit to confirm.
                if (BackOnPresenter(this.stack[^1].Presenter)) return true;
            }

            if (this.stack.Count > 1)
            {
                this.PopAsync().Forget();
                return true;
            }

            // Nothing open at all. The root policy is about the ROOT SCREEN, not about an empty
            // stack: applying Consume here would swallow Back with no UI on screen, which is
            // precisely the stranding NotHandled exists to prevent.
            if (this.stack.Count == 0) return false;

            switch (this.RootBackPolicy)
            {
                case RootBackPolicy.Consume:
                    return true;

                case RootBackPolicy.Raise:
                    this.RootBackRequested?.Invoke();
                    return true;

                default:
                    return false;
            }
        }

        #endregion

        #region State

        public ScreenLifecycleState? StateOf<TPresenter>() where TPresenter : class, IUIToolkitScreenPresenter
        {
            foreach (var entry in this.stack)
            {
                if (entry.Presenter is TPresenter) return entry.Presenter.State;
            }

            return null;
        }

        #endregion

        #region Presenter plumbing

        // The navigator drives presenters through IUIToolkitScreenPresenter, but the members that
        // open, bind and suspend are internal to the base class so a host cannot call them out of
        // order. These helpers are where the two meet; each is a no-op for a presenter that
        // implements the interface directly rather than deriving from the base.

        private static void Attach(IUIToolkitScreenPresenter presenter, IUIToolkitView view, IScreenNavigator navigator, ScreenOptions options)
        {
            if (presenter is not IFlowDriven driven) return;

            driven.AttachView(view);
            driven.AttachNavigator(navigator);
            driven.SetOptions(options);
        }

        private static void SetState(IUIToolkitScreenPresenter presenter, ScreenLifecycleState state)
        {
            if (presenter is IFlowDriven driven) driven.SetState(state);
        }

        private static UniTask Bind(IUIToolkitScreenPresenter presenter, ScreenSubscriptions subscriptions, CancellationToken cancellationToken)
        {
            return presenter is IFlowDriven driven ? driven.Bind(subscriptions, cancellationToken) : UniTask.CompletedTask;
        }

        private static void Activate(IUIToolkitScreenPresenter presenter)
        {
            if (presenter is IFlowDriven driven) driven.Activate();
        }

        private static void Deactivate(IUIToolkitScreenPresenter presenter)
        {
            if (presenter is IFlowDriven driven) driven.Deactivate();
        }

        private static void SuspendPresenter(IUIToolkitScreenPresenter presenter)
        {
            if (presenter is IFlowDriven driven) driven.Suspend();
        }

        private static void ResumePresenter(IUIToolkitScreenPresenter presenter)
        {
            if (presenter is IFlowDriven driven) driven.Resume();
        }

        private static bool BackOnPresenter(IUIToolkitScreenPresenter presenter)
        {
            return presenter is IFlowDriven driven && driven.Back();
        }

        #endregion

        #region Teardown

        private void DisposeEntry(ScreenEntry entry)
        {
            // Order matters and is not arbitrary: cancel first so anything awaiting the token
            // unwinds, then release subscriptions, then detach the view, then dispose the scope
            // that owns the presenter. Disposing the scope first would tear the presenter out
            // from under its own subscriptions.
            try
            {
                entry.Cancellation?.Cancel();
            }
            catch (Exception exception)
            {
                // A cancellation callback threw. Letting it escape would abandon the rest of the
                // teardown, so it is recorded and stepped over.
                this.RecordTeardownFailure(entry, "cancelling", exception);
            }

            entry.Cancellation?.Dispose();
            entry.Cancellation = null;

            try
            {
                entry.Subscriptions?.Dispose();
            }
            catch (Exception exception)
            {
                // ScreenSubscriptions releases everything and THEN rethrows an aggregate, so by
                // the time this runs nothing is still held. Recording and continuing is what
                // stops one screen's bad handler skipping every screen below it.
                this.RecordTeardownFailure(entry, "releasing subscriptions", exception);
            }

            entry.Subscriptions = null;

            try
            {
                entry.View?.DestroySelf();
            }
            catch (Exception exception)
            {
                this.RecordTeardownFailure(entry, "destroying the view", exception);
            }

            entry.View = null;

            try
            {
                entry.Scope?.Dispose();
            }
            catch (Exception exception)
            {
                this.RecordTeardownFailure(entry, "disposing the scope", exception);
            }

            entry.Scope = null;

            if (entry.Presenter != null) SetState(entry.Presenter, ScreenLifecycleState.Disposed);

            entry.Presenter = null;
        }

        private void RecordTeardownFailure(ScreenEntry entry, string stage, Exception exception)
        {
            ++this.TeardownFailureCount;

            // Named screen, named stage. "An exception during teardown" is not actionable; "X
            // threw while releasing subscriptions" is.
            UnityEngine.Debug.LogError(
                $"{nameof(ScreenNavigator)}: {entry.Presenter?.GetType().Name ?? "a screen"} threw while {stage}. "
                + $"Teardown continued. {exception}");
        }

        /// <summary>Closes everything, synchronously, without running outro transitions.</summary>
        /// <remarks>
        /// For scene teardown, where awaiting an animation is pointless and the panel may already
        /// be gone. Screens are disposed top-down so a screen never outlives one above it.
        /// </remarks>
        public void Dispose()
        {
            if (this.disposed) return;
            this.disposed = true;

            for (var i = this.stack.Count - 1; i >= 0; --i) this.DisposeEntry(this.stack[i]);

            this.stack.Clear();
        }

        private void ThrowIfDisposed()
        {
            if (this.disposed) throw new ObjectDisposedException(nameof(ScreenNavigator));
        }

        private async UniTask AcquireGateAsync()
        {
            // A flag rather than a semaphore: everything here runs on the main thread, so there
            // is no race to protect against — only re-entrancy from a caller that did not await.
            while (this.busy) await UniTask.Yield();

            this.busy = true;
        }

        #endregion
    }
}
