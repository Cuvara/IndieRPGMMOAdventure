namespace Cuvara.UIToolkit.Flow
{
    using System;
    using System.Threading;
    using Cuvara.UIToolkit.Core;
    using Cysharp.Threading.Tasks;

    /// <summary>
    /// The base a screen's presenter derives from. Its constructor takes nothing.
    /// </summary>
    /// <remarks>
    /// <para><b>The parameterless protected constructor is the single most important line in this
    /// file.</b> The framework this package replaces demanded a signal bus and a logger manager
    /// in its presenter base constructor, which meant every screen's constructor grew both, and
    /// every screen's *test* grew both — which is why "a presenter must be testable as plain C#"
    /// stayed aspirational there. Here the base needs a view, a navigator and its options, and it
    /// receives all three through internal setters after construction. So a screen's constructor
    /// contains exactly its own dependencies, and its test constructs it with exactly those.</para>
    ///
    /// <para>The moment this base takes a constructor parameter, that property is gone for every
    /// screen ever written against it. It is one line with a very large blast radius.</para>
    ///
    /// <para><b>There is no <c>Dispose</c> to override, and that is deliberate.</b> Everything a
    /// screen registers goes into the <see cref="ScreenSubscriptions"/> handed to
    /// <c>OnBindAsync</c>, and the navigator disposes the whole scope on close. In the old
    /// framework, close and suspend both called <c>Dispose()</c> on an object that kept living
    /// and was reopened — which is exactly why every screen there carried an
    /// unregister-then-register pair. Here suspend is <see cref="OnSuspend"/>, close is the end,
    /// and the two cannot be confused because they have different names and different effects.</para>
    ///
    /// <para><b>Hooks are all optional and all no-ops by default.</b> The only member a screen
    /// must write is <c>OnBindAsync</c>.</para>
    /// </remarks>
    /// <typeparam name="TView">The screen's view interface. An interface, not a concrete view — a presenter that named a concrete <c>VisualElement</c>-bearing type could not be tested without a panel.</typeparam>
    public abstract class BaseUIToolkitScreenPresenter<TView> : IUIToolkitScreenPresenter, IFlowDriven
        where TView : class, IUIToolkitView
    {
        private TView view;

        /// <summary>Nothing framework-shaped is forced through here. See the class remarks.</summary>
        protected BaseUIToolkitScreenPresenter()
        {
        }

        /// <summary>The view. Available from <c>OnBindAsync</c> onwards.</summary>
        /// <exception cref="InvalidOperationException">
        /// Accessed before the flow attached one — which means the presenter was constructed by
        /// hand and driven directly. The message says so, because the alternative is a
        /// <c>NullReferenceException</c> inside the screen's own code with no hint that the
        /// cause is upstream.
        /// </exception>
        protected TView View => this.view ?? throw new InvalidOperationException(
            $"{this.GetType().Name}.View was read before the flow attached one. A presenter is driven by the navigator; "
            + "if you are constructing it in a test, attach a view first.");

        /// <summary>The navigator, for pushing and popping from inside a screen.</summary>
        /// <exception cref="InvalidOperationException">Accessed before the flow attached one.</exception>
        protected IScreenNavigator Nav => this.navigator ?? throw new InvalidOperationException(
            $"{this.GetType().Name}.Nav was read before the flow attached one.");

        private IScreenNavigator navigator;

        IUIToolkitView IUIToolkitScreenPresenter.View => this.view;

        /// <inheritdoc/>
        public ScreenLifecycleState State { get; private set; } = ScreenLifecycleState.Registered;

        /// <inheritdoc/>
        public ScreenOptions Options { get; private set; }

        // ---- Driven by the flow, never by a screen author. Explicit interface implementations,
        // so they do not appear on the type a screen author sees at all. ----

        void IFlowDriven.SetOptions(ScreenOptions options) { this.Options = options; }

        void IFlowDriven.SetState(ScreenLifecycleState state) { this.State = state; }

        void IFlowDriven.AttachNavigator(IScreenNavigator attachedNavigator)
        {
            this.navigator = attachedNavigator ?? throw new ArgumentNullException(nameof(attachedNavigator));
        }

        UniTask IFlowDriven.Bind(ScreenSubscriptions subscriptions, CancellationToken cancellationToken)
        {
            return this.OnBindAsync(subscriptions, cancellationToken);
        }

        void IFlowDriven.Activate() { this.OnActivate(); }

        void IFlowDriven.Deactivate() { this.OnDeactivate(); }

        void IFlowDriven.Suspend() { this.OnSuspend(); }

        void IFlowDriven.Resume() { this.OnResume(); }

        bool IFlowDriven.Back() => this.OnBackRequested();

        void IFlowDriven.AttachView(IUIToolkitView attachedView) { this.AttachViewCore(attachedView); }

        private void AttachViewCore(IUIToolkitView attachedView)
        {
            if (attachedView == null) throw new ArgumentNullException(nameof(attachedView));

            if (attachedView is not TView typed)
            {
                // Named explicitly. A registration pairing a presenter with the wrong view type
                // is a wiring mistake, and the cast exception alone would name neither side.
                throw new InvalidOperationException(
                    $"{this.GetType().Name} expects a view implementing {typeof(TView).Name}, but was given {attachedView.GetType().Name}. "
                    + "Check the view type in this screen's registration.");
            }

            this.view = typed;
        }

        // ---- What a screen actually writes. ----

        /// <summary>
        /// Build the screen: query nothing, wire everything, load the data.
        /// </summary>
        /// <remarks>
        /// <para><paramref name="subscriptions"/> and <paramref name="cancellationToken"/> are
        /// required parameters rather than properties, and there is no overload without them.
        /// That is what makes them impossible to forget: the thing you need in order to clean up
        /// is already in scope at the moment you create something that needs cleaning up.</para>
        ///
        /// <para>Runs exactly once per open. If it throws, the flow disposes the half-built scope
        /// and the stack is left untouched — there is no such thing as a half-open screen.</para>
        /// </remarks>
        protected abstract UniTask OnBindAsync(ScreenSubscriptions subscriptions, CancellationToken cancellationToken);

        /// <summary>The screen became the top of the stack and is interactive.</summary>
        protected virtual void OnActivate() { }

        /// <summary>The screen stopped being the top of the stack.</summary>
        protected virtual void OnDeactivate() { }

        /// <summary>The screen was covered: hidden, sinks unbound, state retained, scope alive.</summary>
        /// <remarks>Not a close, and specifically not a dispose. See the class remarks.</remarks>
        protected virtual void OnSuspend() { }

        /// <summary>The screen was uncovered and is live again, without rebinding.</summary>
        protected virtual void OnResume() { }

        /// <summary>
        /// Back was pressed while this screen was on top. Return true to consume it.
        /// </summary>
        /// <remarks>
        /// The hook exists so a screen with an open dropdown or an unsaved edit can intercept
        /// Back before the navigator pops it. Returning false — the default — lets the navigator
        /// apply its own policy.
        /// </remarks>
        protected virtual bool OnBackRequested() => false;
    }

    /// <summary>The model-taking form of <see cref="BaseUIToolkitScreenPresenter{TView}"/>.</summary>
    /// <remarks>
    /// It cannot inherit the model plumbing from anywhere — C# has single inheritance and this
    /// must derive from the base above to pick up the flow members — so the model handling is
    /// repeated here. It is a field, a property and one sealed override; there is no lifecycle
    /// logic in it.
    /// </remarks>
    public abstract class BaseUIToolkitScreenPresenter<TView, TModel> : BaseUIToolkitScreenPresenter<TView>, IUIToolkitScreenPresenter<TModel>, IModelReceiver<TModel>
        where TView : class, IUIToolkitView
    {
        /// <summary>What this screen was opened with.</summary>
        protected TModel Model { get; private set; }

        void IModelReceiver<TModel>.ReceiveModel(TModel model) { this.Model = model; }

        /// <summary>Sealed: a model-taking screen binds through the model overload, and only that one.</summary>
        protected sealed override UniTask OnBindAsync(ScreenSubscriptions subscriptions, CancellationToken cancellationToken)
        {
            return this.OnBindAsync(this.Model, subscriptions, cancellationToken);
        }

        /// <inheritdoc cref="BaseUIToolkitScreenPresenter{TView}.OnBindAsync"/>
        protected abstract UniTask OnBindAsync(TModel model, ScreenSubscriptions subscriptions, CancellationToken cancellationToken);
    }

    /// <summary>A screen that opens as a modal, over whatever is below it.</summary>
    /// <remarks>
    /// The only difference from a screen is where it is parented and what it does to the screen
    /// beneath, and both of those come from its <see cref="ScreenOptions"/> at registration
    /// rather than from this type. It exists so that "this is a popup" is visible in the class
    /// declaration as well as in the registration line — and because a popup almost always wants
    /// <see cref="Close"/>, which a full screen rarely does.
    /// </remarks>
    public abstract class BaseUIToolkitPopupPresenter<TView> : BaseUIToolkitScreenPresenter<TView>
        where TView : class, IUIToolkitView
    {
        /// <summary>Closes this popup. Shorthand for popping it off the stack.</summary>
        /// <remarks>
        /// Fire-and-forget on purpose: it is wired straight to a button click, and a click
        /// handler cannot await. The navigator serialises operations, so a double-click cannot
        /// pop twice.
        /// </remarks>
        protected void Close() { this.Nav.PopAsync().Forget(); }
    }

    /// <summary>The model-taking form of <see cref="BaseUIToolkitPopupPresenter{TView}"/>.</summary>
    public abstract class BaseUIToolkitPopupPresenter<TView, TModel> : BaseUIToolkitScreenPresenter<TView, TModel>
        where TView : class, IUIToolkitView
    {
        /// <inheritdoc cref="BaseUIToolkitPopupPresenter{TView}.Close"/>
        protected void Close() { this.Nav.PopAsync().Forget(); }
    }
}
