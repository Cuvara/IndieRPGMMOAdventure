namespace Cuvara.UIToolkit.Samples.ScreenFlow
{
    using System;
    using System.Collections.Generic;
    using System.Text;
    using System.Threading;
    using Cuvara.UIToolkit.Core;
    using Cuvara.UIToolkit.Flow;
    using Cuvara.UIToolkit.Managers;
    using Cuvara.UIToolkit.VContainer;
    using Cuvara.UIToolkit.View;
    using Cysharp.Threading.Tasks;
    using global::VContainer;
    using global::VContainer.Unity;
    using UnityEngine;
    using UnityEngine.UIElements;

    // -------------------------------------------------------------------------------------
    // A scene you can press Play on. Everything the flow does — push, pop, replace, popToRoot,
    // suspend/resume, a dimming modal, and Back — is reachable by clicking, and the HUD in the
    // corner reports Depth and every screen's ScreenLifecycleState live, so suspend/resume is
    // something you WATCH rather than something you infer from a test name.
    //
    // Three screens, because two would not show suspend/resume and one would not show a stack.
    // -------------------------------------------------------------------------------------

    #region The asset loader — the one thing every host must supply

    /// <summary>
    /// A dictionary of UXML, assigned in the inspector.
    /// </summary>
    /// <remarks>
    /// <para>This is the whole of <see cref="IVisualTreeAssetLoader"/>: one method, key to
    /// asset. A real project usually points it at Addressables — the sample deliberately does
    /// not, because a sample that drags in Addressables cannot be opened by anyone who has not
    /// installed them, and the interface exists precisely so the package does not care.</para>
    /// </remarks>
    [Serializable]
    public sealed class InspectorAssetLoader : IVisualTreeAssetLoader
    {
        [SerializeField] private List<Entry> entries = new();

        [Serializable]
        public sealed class Entry
        {
            public string          Key;
            public VisualTreeAsset Asset;
        }

        public UniTask<VisualTreeAsset> LoadAsync(string key)
        {
            foreach (var entry in this.entries)
            {
                if (entry.Key == key) return UniTask.FromResult(entry.Asset);
            }

            // Naming the key and listing what IS registered, because "returned null" sends the
            // reader to the wrong file entirely.
            var known = string.Join(", ", this.entries.ConvertAll(e => e.Key));
            throw new KeyNotFoundException($"No UXML registered for screen key '{key}'. Registered keys: {known}.");
        }
    }

    #endregion

    #region Screen 1 — the root

    public interface IRootScreenView : IUIToolkitView
    {
        Button Push    { get; }
        Button Modal   { get; }
        Button Replace { get; }
    }

    public sealed class RootScreenView : BaseUIToolkitView, IRootScreenView
    {
        public Button Push    { get; }
        public Button Modal   { get; }
        public Button Replace { get; }

        public RootScreenView(VisualTreeAsset asset) : base(asset)
        {
            this.StretchToParent();

            this.Push    = this.Root.Q<Button>("btn-push");
            this.Modal   = this.Root.Q<Button>("btn-modal");
            this.Replace = this.Root.Q<Button>("btn-replace");
        }
    }

    /// <summary>
    /// The whole screen. Note what is absent: no constructor parameters from the framework, no
    /// Dispose override, no UnregisterCallback, no CancellationTokenSource, no scope handling.
    /// </summary>
    public sealed class RootScreenPresenter : BaseUIToolkitScreenPresenter<IRootScreenView>
    {
        protected override UniTask OnBindAsync(ScreenSubscriptions subs, CancellationToken ct)
        {
            // subs is a required parameter, so the thing that cleans these up is already in
            // scope at the moment they are created. There is no overload without it.
            subs.Clicked(this.View.Push, () => this.Nav.PushAsync<SecondScreenPresenter>().Forget());
            subs.Clicked(this.View.Modal, () => this.Nav.PushAsync<ConfirmPopupPresenter>().Forget());
            subs.Clicked(this.View.Replace, () => this.Nav.ReplaceAsync<SecondScreenPresenter>().Forget());

            return UniTask.CompletedTask;
        }
    }

    #endregion

    #region Screen 2 — reached by Push, so suspend/resume is visible

    public interface ISecondScreenView : IUIToolkitView
    {
        Button Push      { get; }
        Button Modal     { get; }
        Button Pop       { get; }
        Button PopToRoot { get; }
    }

    public sealed class SecondScreenView : BaseUIToolkitView, ISecondScreenView
    {
        public Button Push      { get; }
        public Button Modal     { get; }
        public Button Pop       { get; }
        public Button PopToRoot { get; }

        public SecondScreenView(VisualTreeAsset asset) : base(asset)
        {
            this.StretchToParent();

            this.Push      = this.Root.Q<Button>("btn-push");
            this.Modal     = this.Root.Q<Button>("btn-modal");
            this.Pop       = this.Root.Q<Button>("btn-pop");
            this.PopToRoot = this.Root.Q<Button>("btn-pop-to-root");
        }
    }

    public sealed class SecondScreenPresenter : BaseUIToolkitScreenPresenter<ISecondScreenView>
    {
        protected override UniTask OnBindAsync(ScreenSubscriptions subs, CancellationToken ct)
        {
            subs.Clicked(this.View.Push, () => this.Nav.PushAsync<SecondScreenPresenter>().Forget());
            subs.Clicked(this.View.Modal, () => this.Nav.PushAsync<ConfirmPopupPresenter>().Forget());
            subs.Clicked(this.View.Pop, () => this.Nav.PopAsync().Forget());
            subs.Clicked(this.View.PopToRoot, () => this.Nav.PopToRootAsync().Forget());

            return UniTask.CompletedTask;
        }
    }

    #endregion

    #region Screen 3 — a modal that dims rather than suspends

    public interface IConfirmPopupView : IUIToolkitView
    {
        Button Close { get; }
    }

    public sealed class ConfirmPopupView : BaseUIToolkitView, IConfirmPopupView
    {
        public Button Close { get; }

        public ConfirmPopupView(VisualTreeAsset asset) : base(asset)
        {
            this.StretchToParent();

            this.Close = this.Root.Q<Button>("btn-close");
        }
    }

    /// <summary>Derives from the popup base, which is where <c>Close()</c> comes from.</summary>
    public sealed class ConfirmPopupPresenter : BaseUIToolkitPopupPresenter<IConfirmPopupView>
    {
        protected override UniTask OnBindAsync(ScreenSubscriptions subs, CancellationToken ct)
        {
            subs.Clicked(this.View.Close, this.Close);

            return UniTask.CompletedTask;
        }
    }

    #endregion

    #region Composition

    /// <summary>
    /// Opens the first screen, wires Back, and drives the HUD.
    /// </summary>
    /// <remarks>
    /// <para><c>IInitializable</c> rather than <c>IStartable</c>, and that is not
    /// interchangeable: VContainer runs <c>Initialize()</c> synchronously inside its dispatch,
    /// but queues <c>Start()</c> to the player loop a point later, where the caller cannot await
    /// it. A screen opened from <c>IStartable</c> has a one-frame gap nothing can close.</para>
    ///
    /// <para><b>Back is one line</b>: <c>source.BackHandler = nav.HandleBack</c>. The navigator
    /// returns whether it acted, the source consumes only on a true, and an unhandled press
    /// keeps propagating — which is what leaves the platform's own Back working at the root.</para>
    /// </remarks>
    public sealed class ScreenFlowSampleBootstrap : IInitializable, ITickable, IDisposable
    {
        private readonly IScreenNavigator             navigator;
        private readonly RootUIDocument               document;
        private readonly IVisualTreeAssetLoader       loader;

        private Cuvara.UIToolkit.Input.BackNavigationSource backSource;

        private Label depthLabel, topLabel, statesLabel, backLabel;
        private int   backPresses, backHandled;

        public ScreenFlowSampleBootstrap(IScreenNavigator navigator, RootUIDocument document, IVisualTreeAssetLoader loader)
        {
            this.navigator = navigator;
            this.document  = document;
            this.loader    = loader;
        }

        public void Initialize()
        {
            // Everything here waits one frame, and that is a real constraint rather than
            // caution. UIDocument builds its rootVisualElement in its own OnEnable, and the
            // order of two components' Awake/OnEnable across two GameObjects is undefined — so
            // a composition root that touches the panel in Initialize() sometimes finds null.
            // The package's RootUIDocument resolves its LAYERS lazily so the navigator is
            // immune, but anything reaching for the panel root itself, as the HUD and the back
            // source do, has to wait for it.
            this.StartAsync().Forget();
        }

        private async UniTaskVoid StartAsync()
        {
            await UniTask.Yield();

            await this.BuildHudAsync();

            // Back, wired to the navigator's policy. The real wiring is ONE LINE and needs no
            // lambda, because HandleBack already has the signature BackHandler wants:
            //
            //     this.backSource.BackHandler = this.navigator.HandleBack;
            //
            // The lambda below is the SAMPLE'S doing, not the API's: it counts presses so the
            // HUD can show handled-versus-seen, which is the whole point of watching Back at
            // the root. Do not copy the lambda into your own code unless you want the count.
            //
            // Either way the source consumes only on a true, so an unhandled press keeps
            // propagating and reaches the application.
            this.backSource = new(this.document.RootVisualElement);
            this.backSource.BackHandler = () =>
            {
                ++this.backPresses;
                var handled = this.navigator.HandleBack();
                if (handled) ++this.backHandled;
                return handled;
            };

            // Try changing this to Consume or Raise and pressing Back at the root.
            this.navigator.RootBackPolicy = RootBackPolicy.NotHandled;

            await this.navigator.PushAsync<RootScreenPresenter>();
        }

        private async UniTask BuildHudAsync()
        {
            var hud = (await this.loader.LoadAsync("Hud")).CloneTree();

            // Straight into the document root rather than a layer: the HUD must never be
            // suspended, because reporting the flow's state is its entire job.
            this.document.RootVisualElement.Add(hud);
            hud.pickingMode = PickingMode.Ignore;

            this.depthLabel  = hud.Q<Label>("depth");
            this.topLabel    = hud.Q<Label>("top");
            this.statesLabel = hud.Q<Label>("states");
            this.backLabel   = hud.Q<Label>("back");
        }

        public void Tick()
        {
            if (this.depthLabel == null) return;

            this.depthLabel.text = $"Depth: {this.navigator.Depth}{(this.navigator.IsBusy ? "  (busy)" : string.Empty)}";
            this.topLabel.text   = $"Top: {this.navigator.Top?.GetType().Name ?? "(none)"}";

            var builder = new StringBuilder("Stack: ");

            if (this.navigator.Top == null) builder.Append("(empty)");
            else builder.Append($"{this.navigator.Top.GetType().Name} = {this.navigator.Top.State}");

            this.statesLabel.text = builder.ToString();
            this.backLabel.text   = $"Back: {this.backPresses} seen, {this.backHandled} handled";
        }

        public void Dispose() { this.backSource?.Dispose(); }
    }

    #endregion
}
