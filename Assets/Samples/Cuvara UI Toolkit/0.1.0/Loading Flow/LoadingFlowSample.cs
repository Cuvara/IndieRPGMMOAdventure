namespace Cuvara.UIToolkit.Samples.LoadingFlow
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using Cuvara.UIToolkit.Collections;
    using Cuvara.UIToolkit.Core;
    using Cuvara.UIToolkit.Flow;
    using Cuvara.UIToolkit.Input;
    using Cuvara.UIToolkit.Managers;
    using Cuvara.UIToolkit.View;
    using Cysharp.Threading.Tasks;
    using UnityEngine;
    using UnityEngine.UIElements;

    // -------------------------------------------------------------------------------------
    // Two-scene flow demonstrating every UI Toolkit package feature:
    //   LoadingScene (MonoBehaviour-driven progress) → MainScene (navigator + all features)
    //
    // Features shown: Push, Pop, Replace, PopToRoot, Modal+DimsBelow, Model parameters,
    //   OnActivate/OnDeactivate/OnSuspend/OnResume lifecycle, OnBackRequested override,
    //   BackNavigationSource, ScreenSubscriptions, UIToolkitListAdapter, lifecycle labels.
    // -------------------------------------------------------------------------------------

    #region Asset loader

    public sealed class ResourceAssetLoader : IVisualTreeAssetLoader
    {
        public UniTask<VisualTreeAsset> LoadAsync(string key)
        {
            var asset = Resources.Load<VisualTreeAsset>(key);
            if (asset == null)
                throw new KeyNotFoundException($"No VisualTreeAsset in Resources for key '{key}'.");
            return UniTask.FromResult(asset);
        }
    }

    #endregion

    #region Main screen — 5 buttons, lifecycle label, depth display

    public interface IMainScreenView : IUIToolkitView
    {
        Button InfoButton { get; }
        Button ConfirmButton { get; }
        Button SettingsButton { get; }
        Button InventoryButton { get; }
        Button ReplaceButton { get; }
        Label LifecycleLabel { get; }
        Label NavDepthLabel { get; }
    }

    public sealed class MainScreenView : BaseUIToolkitView, IMainScreenView
    {
        public Button InfoButton { get; }
        public Button ConfirmButton { get; }
        public Button SettingsButton { get; }
        public Button InventoryButton { get; }
        public Button ReplaceButton { get; }
        public Label LifecycleLabel { get; }
        public Label NavDepthLabel { get; }

        public MainScreenView(VisualTreeAsset asset) : base(asset)
        {
            this.StretchToParent();
            this.InfoButton = this.Root.Q<Button>("btn-info");
            this.ConfirmButton = this.Root.Q<Button>("btn-confirm");
            this.SettingsButton = this.Root.Q<Button>("btn-settings");
            this.InventoryButton = this.Root.Q<Button>("btn-inventory");
            this.ReplaceButton = this.Root.Q<Button>("btn-replace");
            this.LifecycleLabel = this.Root.Q<Label>("lifecycle-label");
            this.NavDepthLabel = this.Root.Q<Label>("nav-depth");
        }
    }

    public sealed class MainScreenPresenter : BaseUIToolkitScreenPresenter<IMainScreenView>
    {
        protected override UniTask OnBindAsync(ScreenSubscriptions subs, CancellationToken ct)
        {
            subs.Clicked(this.View.InfoButton, () => this.Nav.PushAsync<InfoPopupPresenter>().Forget());
            // Model parameter — pass custom message to ConfirmPopup
            subs.Clicked(this.View.ConfirmButton, () =>
                this.Nav.PushAsync<ConfirmPopupPresenter, ConfirmModel>(
                    new ConfirmModel("Delete save data?", "This cannot be undone.")).Forget());
            subs.Clicked(this.View.SettingsButton, () => this.Nav.PushAsync<SettingsPopupPresenter>().Forget());
            subs.Clicked(this.View.InventoryButton, () => this.Nav.PushAsync<InventoryPopupPresenter>().Forget());
            // Replace — swaps this screen for SecondScreen (no stack push)
            subs.Clicked(this.View.ReplaceButton, () => this.Nav.ReplaceAsync<SecondScreenPresenter>().Forget());
            return UniTask.CompletedTask;
        }

        protected override void OnActivate()
        {
            this.View.LifecycleLabel.text = "State: Active";
            this.View.NavDepthLabel.text = $"Depth: {this.Nav.Depth}";
        }

        protected override void OnSuspend()
        {
            this.View.LifecycleLabel.text = "State: Suspended";
        }

        protected override void OnResume()
        {
            this.View.LifecycleLabel.text = "State: Active (resumed)";
            this.View.NavDepthLabel.text = $"Depth: {this.Nav.Depth}";
        }
    }

    #endregion

    #region Second screen — reached by Replace or Push, shows PopToRoot + lifecycle

    public interface ISecondScreenView : IUIToolkitView
    {
        Button PushButton { get; }
        Button ModalButton { get; }
        Button PopButton { get; }
        Button PopToRootButton { get; }
        Label LifecycleLabel { get; }
        Label StateLabel { get; }
        Label NavDepthLabel { get; }
    }

    public sealed class SecondScreenView : BaseUIToolkitView, ISecondScreenView
    {
        public Button PushButton { get; }
        public Button ModalButton { get; }
        public Button PopButton { get; }
        public Button PopToRootButton { get; }
        public Label LifecycleLabel { get; }
        public Label StateLabel { get; }
        public Label NavDepthLabel { get; }

        public SecondScreenView(VisualTreeAsset asset) : base(asset)
        {
            this.StretchToParent();
            this.PushButton = this.Root.Q<Button>("btn-push");
            this.ModalButton = this.Root.Q<Button>("btn-modal");
            this.PopButton = this.Root.Q<Button>("btn-pop");
            this.PopToRootButton = this.Root.Q<Button>("btn-pop-to-root");
            this.LifecycleLabel = this.Root.Q<Label>("lifecycle-label");
            this.StateLabel = this.Root.Q<Label>("state-label");
            this.NavDepthLabel = this.Root.Q<Label>("nav-depth");
        }
    }

    public sealed class SecondScreenPresenter : BaseUIToolkitScreenPresenter<ISecondScreenView>
    {
        protected override UniTask OnBindAsync(ScreenSubscriptions subs, CancellationToken ct)
        {
            // Push another SecondScreen — builds a deep stack
            subs.Clicked(this.View.PushButton, () => this.Nav.PushAsync<SecondScreenPresenter>().Forget());
            subs.Clicked(this.View.ModalButton, () =>
                this.Nav.PushAsync<ConfirmPopupPresenter, ConfirmModel>(
                    new ConfirmModel("From Second Screen", "Modal pushed from the second screen.")).Forget());
            subs.Clicked(this.View.PopButton, () => this.Nav.PopAsync().Forget());
            // PopToRoot — pops everything down to the first screen
            subs.Clicked(this.View.PopToRootButton, () => this.Nav.PopToRootAsync().Forget());
            return UniTask.CompletedTask;
        }

        // OnBackRequested override — custom per-screen back handling
        protected override bool OnBackRequested()
        {
            Debug.Log("[SecondScreen] OnBackRequested — popping via custom handler");
            this.Nav.PopAsync().Forget();
            return true; // consumed
        }

        protected override void OnActivate()
        {
            this.View.LifecycleLabel.text = "State: Active";
            this.View.StateLabel.text = "OnActivate fired — this screen is on top.";
            this.View.NavDepthLabel.text = $"Depth: {this.Nav.Depth}";
        }

        protected override void OnDeactivate()
        {
            this.View.LifecycleLabel.text = "State: Deactivated";
            this.View.StateLabel.text = "OnDeactivate — no longer on top.";
        }

        protected override void OnSuspend()
        {
            this.View.LifecycleLabel.text = "State: Suspended";
            this.View.StateLabel.text = "OnSuspend — hidden by a full-screen push.";
        }

        protected override void OnResume()
        {
            this.View.LifecycleLabel.text = "State: Active (resumed)";
            this.View.StateLabel.text = "OnResume — back on top after pop.";
            this.View.NavDepthLabel.text = $"Depth: {this.Nav.Depth}";
        }
    }

    #endregion

    #region Info popup (modal, dims below)

    public interface IInfoPopupView : IUIToolkitView
    {
        Button CloseButton { get; }
    }

    public sealed class InfoPopupView : BaseUIToolkitView, IInfoPopupView
    {
        public Button CloseButton { get; }

        public InfoPopupView(VisualTreeAsset asset) : base(asset)
        {
            this.StretchToParent();
            this.CloseButton = this.Root.Q<Button>("btn-close");
        }
    }

    public sealed class InfoPopupPresenter : BaseUIToolkitPopupPresenter<IInfoPopupView>
    {
        protected override UniTask OnBindAsync(ScreenSubscriptions subs, CancellationToken ct)
        {
            subs.Clicked(this.View.CloseButton, this.Close);
            return UniTask.CompletedTask;
        }
    }

    #endregion

    #region Confirm popup — with model parameter

    public readonly struct ConfirmModel
    {
        public readonly string Title;
        public readonly string Body;
        public ConfirmModel(string title, string body) { this.Title = title; this.Body = body; }
    }

    public interface IConfirmPopupView : IUIToolkitView
    {
        Label TitleLabel { get; }
        Label BodyLabel { get; }
        Button CancelButton { get; }
        Button OkButton { get; }
    }

    public sealed class ConfirmPopupView : BaseUIToolkitView, IConfirmPopupView
    {
        public Label TitleLabel { get; }
        public Label BodyLabel { get; }
        public Button CancelButton { get; }
        public Button OkButton { get; }

        public ConfirmPopupView(VisualTreeAsset asset) : base(asset)
        {
            this.StretchToParent();
            this.TitleLabel = this.Root.Q<Label>("popup-title");
            this.BodyLabel = this.Root.Q<Label>("popup-body");
            this.CancelButton = this.Root.Q<Button>("btn-cancel");
            this.OkButton = this.Root.Q<Button>("btn-ok");
        }
    }

    // Model-parameterized popup — receives ConfirmModel via PushAsync<T, TModel>(model)
    public sealed class ConfirmPopupPresenter : BaseUIToolkitPopupPresenter<IConfirmPopupView, ConfirmModel>
    {
        protected override UniTask OnBindAsync(ConfirmModel model, ScreenSubscriptions subs, CancellationToken ct)
        {
            this.View.TitleLabel.text = model.Title;
            this.View.BodyLabel.text = model.Body;

            subs.Clicked(this.View.CancelButton, this.Close);
            subs.Clicked(this.View.OkButton, () =>
            {
                Debug.Log($"[ConfirmPopup] Confirmed: {model.Title}");
                this.Close();
            });
            return UniTask.CompletedTask;
        }
    }

    #endregion

    #region Settings popup

    public interface ISettingsPopupView : IUIToolkitView
    {
        Slider MusicSlider { get; }
        Slider SfxSlider { get; }
        Toggle FullscreenToggle { get; }
        Button CloseButton { get; }
    }

    public sealed class SettingsPopupView : BaseUIToolkitView, ISettingsPopupView
    {
        public Slider MusicSlider { get; }
        public Slider SfxSlider { get; }
        public Toggle FullscreenToggle { get; }
        public Button CloseButton { get; }

        public SettingsPopupView(VisualTreeAsset asset) : base(asset)
        {
            this.StretchToParent();
            this.MusicSlider = this.Root.Q<Slider>("slider-music");
            this.SfxSlider = this.Root.Q<Slider>("slider-sfx");
            this.FullscreenToggle = this.Root.Q<Toggle>("toggle-fullscreen");
            this.CloseButton = this.Root.Q<Button>("btn-close");
        }
    }

    public sealed class SettingsPopupPresenter : BaseUIToolkitPopupPresenter<ISettingsPopupView>
    {
        protected override UniTask OnBindAsync(ScreenSubscriptions subs, CancellationToken ct)
        {
            this.View.MusicSlider.RegisterValueChangedCallback(evt =>
                Debug.Log($"[Settings] Music: {evt.newValue:F0}%"));
            this.View.SfxSlider.RegisterValueChangedCallback(evt =>
                Debug.Log($"[Settings] SFX: {evt.newValue:F0}%"));
            this.View.FullscreenToggle.RegisterValueChangedCallback(evt =>
                Debug.Log($"[Settings] Fullscreen: {evt.newValue}"));

            subs.Clicked(this.View.CloseButton, this.Close);
            return UniTask.CompletedTask;
        }
    }

    #endregion

    #region Inventory popup — UIToolkitListAdapter demo

    public readonly struct InventoryItem
    {
        public readonly string Icon;
        public readonly string Name;
        public readonly string Description;
        public readonly int Quantity;

        public InventoryItem(string icon, string name, string desc, int qty)
        {
            this.Icon = icon; this.Name = name; this.Description = desc; this.Quantity = qty;
        }
    }

    public sealed class InventoryItemView : IUIToolkitItemView
    {
        public VisualElement Root { get; }
        public Label IconLabel { get; }
        public Label NameLabel { get; }
        public Label DescLabel { get; }
        public Label QtyLabel { get; }

        public InventoryItemView(VisualElement root)
        {
            this.Root = root;
            this.IconLabel = root.Q<Label>("item-icon");
            this.NameLabel = root.Q<Label>("item-name");
            this.DescLabel = root.Q<Label>("item-desc");
            this.QtyLabel = root.Q<Label>("item-qty");
        }
    }

    public sealed class InventoryItemPresenter : BaseUIToolkitItemPresenter<InventoryItemView, InventoryItem>
    {
        public override void BindData(InventoryItem param)
        {
            this.View.IconLabel.text = param.Icon;
            this.View.NameLabel.text = param.Name;
            this.View.DescLabel.text = param.Description;
            this.View.QtyLabel.text = $"x{param.Quantity}";
        }
    }

    public interface IInventoryPopupView : IUIToolkitView
    {
        ListView ItemList { get; }
        Button CloseButton { get; }
    }

    public sealed class InventoryPopupView : BaseUIToolkitView, IInventoryPopupView
    {
        public ListView ItemList { get; }
        public Button CloseButton { get; }

        public InventoryPopupView(VisualTreeAsset asset) : base(asset)
        {
            this.StretchToParent();
            this.ItemList = this.Root.Q<ListView>("item-list");
            this.CloseButton = this.Root.Q<Button>("btn-close");
        }
    }

    public sealed class InventoryPopupPresenter : BaseUIToolkitPopupPresenter<IInventoryPopupView>
    {
        private UIToolkitListAdapter<InventoryItem, InventoryItemView, InventoryItemPresenter> adapter;

        protected override UniTask OnBindAsync(ScreenSubscriptions subs, CancellationToken ct)
        {
            var itemTemplate = Resources.Load<VisualTreeAsset>("InventoryItem");
            this.adapter = new UIToolkitListAdapter<InventoryItem, InventoryItemView, InventoryItemPresenter>(
                this.View.ItemList, itemTemplate, fixedItemHeight: 56f);

            this.adapter.SetItems(new[]
            {
                new InventoryItem("\U0001F5E1", "Iron Sword", "A sturdy blade for beginners.", 1),
                new InventoryItem("\U0001F6E1", "Wooden Shield", "Blocks minor attacks.", 1),
                new InventoryItem("\U00002764", "Health Potion", "Restores 50 HP.", 5),
                new InventoryItem("\U0001F48E", "Mana Crystal", "Restores 30 MP.", 3),
                new InventoryItem("\U0001F3F9", "Elven Bow", "Long-range precision weapon.", 1),
                new InventoryItem("\U0001F525", "Fire Scroll", "Casts Fireball. Single use.", 2),
                new InventoryItem("\U00002728", "Star Fragment", "Rare crafting material.", 7),
                new InventoryItem("\U0001F48D", "Ring of Haste", "+15% movement speed.", 1),
                new InventoryItem("\U0001F9EA", "Antidote", "Cures poison status.", 4),
                new InventoryItem("\U0001F511", "Dungeon Key", "Opens sealed dungeon doors.", 1),
            });

            subs.Clicked(this.View.CloseButton, this.Close);
            return UniTask.CompletedTask;
        }
    }

    #endregion

    #region Bootstraps

    public sealed class MainBootstrap : global::VContainer.Unity.IInitializable, IDisposable
    {
        private readonly IScreenNavigator navigator;
        private readonly RootUIDocument document;
        private BackNavigationSource backSource;

        public MainBootstrap(IScreenNavigator navigator, RootUIDocument document)
        {
            this.navigator = navigator;
            this.document = document;
        }

        public void Initialize() => this.StartAsync().Forget();

        private async UniTaskVoid StartAsync()
        {
            await UniTask.Yield();

            this.backSource = new BackNavigationSource(this.document.RootVisualElement);
            this.backSource.BackHandler = this.navigator.HandleBack;
            this.navigator.RootBackPolicy = RootBackPolicy.NotHandled;

            await this.navigator.PushAsync<MainScreenPresenter>();
        }

        public void Dispose() => this.backSource?.Dispose();
    }

    #endregion
}
