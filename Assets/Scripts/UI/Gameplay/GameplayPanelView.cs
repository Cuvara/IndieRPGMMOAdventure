namespace Scripts.UI.Gameplay
{
    using System;
    using System.Collections.Generic;
    using Cuvara.UIToolkit.View;
    using Scripts.Gameplay.Inventory;
    using Unity.Properties;
    using UnityEngine.UIElements;

    /// <summary>
    /// The gameplay panel view: binds the captions to <see cref="GameplayPanelViewModel"/> and
    /// renders the inventory as a virtualized <see cref="ListView"/> whose rows carry
    /// equip / unequip / use buttons. The ONLY layer here that knows UI Toolkit.
    /// </summary>
    /// <remarks>
    /// The other half of this <c>partial</c> is <c>Resources/Generated/GameplayPanelView.uxml.g.cs</c>,
    /// regenerated from the UXML by the uitoolkit codegen. Row elements are made once per
    /// visible slot (<c>makeItem</c>) and re-pointed at a row on bind; their buttons raise the
    /// view's events with whatever row the element currently shows, so nothing is subscribed per
    /// bind and nothing leaks when the list recycles.
    /// </remarks>
    public sealed partial class GameplayPanelView : BaseUIToolkitView, IGameplayPanelView
    {
        private readonly List<InventoryRow> rows = new List<InventoryRow>();

        public GameplayPanelView(VisualTreeAsset visualTreeAsset) : base(visualTreeAsset)
        {
            this.StretchToParent();
            this.Root.pickingMode = PickingMode.Ignore;
            this.AssignQueries(this.Root);

            var list = this.GameplayInventoryList;
            list.itemsSource = this.rows;
            list.selectionType = SelectionType.None;
            list.makeItem = this.MakeRow;
            list.bindItem = this.BindRow;

            this.GameplayPickup.clicked += () => this.PickupClicked?.Invoke();
            this.GameplayRefresh.clicked += () => this.RefreshClicked?.Invoke();
        }

        public event Action<InventoryRow> EquipClicked;

        public event Action<InventoryRow> UnequipClicked;

        public event Action<InventoryRow> UseClicked;

        public event Action PickupClicked;

        public event Action RefreshClicked;

        public void Bind(GameplayPanelViewModel viewModel)
        {
            if (viewModel == null) throw new ArgumentNullException(nameof(viewModel));

            this.Root.dataSource = viewModel;
            this.GameplayLevel.SetBinding(nameof(Label.text), ToTarget(nameof(GameplayPanelViewModel.LevelCaption)));
            this.GameplayMana.SetBinding(nameof(Label.text), ToTarget(nameof(GameplayPanelViewModel.ManaCaption)));
            this.GameplayStatuses.SetBinding(nameof(Label.text), ToTarget(nameof(GameplayPanelViewModel.StatusCaption)));
            this.GameplayInventoryCaption.SetBinding(nameof(Label.text), ToTarget(nameof(GameplayPanelViewModel.InventoryCaption)));

            var visible = ToTarget(nameof(GameplayPanelViewModel.InventoryVisible));
            visible.sourceToUiConverters.AddConverter((ref bool shown) => new StyleEnum<DisplayStyle>(shown ? DisplayStyle.Flex : DisplayStyle.None));
            this.GameplayInventory.SetBinding("style.display", visible);
        }

        public void ShowRows(IReadOnlyList<InventoryRow> next)
        {
            this.rows.Clear();
            if (next != null) this.rows.AddRange(next);

            // Same source object, new contents: RefreshItems, not Rebuild (docs/UI-ARCHITECTURE.md).
            this.GameplayInventoryList.RefreshItems();
        }

        private VisualElement MakeRow()
        {
            var row = new VisualElement();
            row.AddToClassList("gameplay-panel__item");

            var label = new Label { name = "item-label" };
            label.AddToClassList("gameplay-panel__item-label");
            row.Add(label);

            row.Add(this.RowButton(row, "item-equip", "Equip", r => this.EquipClicked?.Invoke(r)));
            row.Add(this.RowButton(row, "item-unequip", "Unequip", r => this.UnequipClicked?.Invoke(r)));
            row.Add(this.RowButton(row, "item-use", "Use", r => this.UseClicked?.Invoke(r)));
            return row;
        }

        private Button RowButton(VisualElement row, string name, string text, Action<InventoryRow> raise)
        {
            var button = new Button { name = name, text = text };
            button.AddToClassList("gameplay-panel__item-button");
            button.clicked += () =>
            {
                if (row.userData is InventoryRow current) raise(current);
            };
            return button;
        }

        private void BindRow(VisualElement element, int index)
        {
            var row = index >= 0 && index < this.rows.Count ? this.rows[index] : null;
            element.userData = row;
            element.Q<Label>("item-label").text = row?.Caption ?? string.Empty;
            Show(element.Q<Button>("item-equip"), row != null && row.CanEquip);
            Show(element.Q<Button>("item-unequip"), row != null && row.CanUnequip);
            Show(element.Q<Button>("item-use"), row != null && row.CanUse);
        }

        private static void Show(VisualElement element, bool shown) =>
            element.style.display = shown ? DisplayStyle.Flex : DisplayStyle.None;

        private static DataBinding ToTarget(string propertyName) => new DataBinding
        {
            dataSourcePath = new PropertyPath(propertyName),
            bindingMode = BindingMode.ToTarget,
        };
    }
}
