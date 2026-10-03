namespace Scripts.UI.Gameplay
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using Cysharp.Threading.Tasks;
    using Scripts.Gameplay.Inventory;
    using Scripts.Gameplay.Stats;
    using Shared.GameLogic.Content;

    /// <summary>
    /// The gameplay panel's presenter: stat block and statuses in, inventory commands out.
    /// Plain C# — it talks to <see cref="IGameplayPanelView"/> and <see cref="IInventoryService"/>
    /// and knows no UI Toolkit type, so it is tested with fakes (docs/UI-ARCHITECTURE.md).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The server's inventory is the only truth.</b> A click sends a command and changes
    /// nothing locally; the list redraws when the service reports the server's new view (the
    /// opcode 1/2 answer, or push 100 after equip/unequip/use). A refusal shows its
    /// <c>GameplayErrors</c> code in the caption.
    /// </para>
    /// <para>
    /// Item names and slots come from <paramref name="items"/> at the time of each rebuild, so
    /// rows re-label themselves once the content download lands (<see cref="ContentChanged"/>).
    /// </para>
    /// </remarks>
    public sealed class GameplayPanelPresenter : IDisposable
    {
        private readonly IGameplayPanelView view;
        private readonly IInventoryService inventory;
        private readonly Func<ContentDatabase> items;
        private readonly Func<string> nearestItem;
        private readonly CancellationTokenSource lifetime = new CancellationTokenSource();
        private List<InventoryRow> shown = new List<InventoryRow>();

        /// <param name="view">The panel view.</param>
        /// <param name="inventory">The inventory commands.</param>
        /// <param name="items">Current item definitions (content); may return null.</param>
        /// <param name="nearestItem">The id of the item entity to pick up, or null when none is in reach.</param>
        public GameplayPanelPresenter(
            IGameplayPanelView view,
            IInventoryService inventory,
            Func<ContentDatabase> items,
            Func<string> nearestItem)
        {
            this.view = view ?? throw new ArgumentNullException(nameof(view));
            this.inventory = inventory ?? throw new ArgumentNullException(nameof(inventory));
            this.items = items ?? (() => null);
            this.nearestItem = nearestItem ?? (() => null);

            this.ViewModel = new GameplayPanelViewModel();
            this.view.Bind(this.ViewModel);

            this.view.EquipClicked += this.OnEquip;
            this.view.UnequipClicked += this.OnUnequip;
            this.view.UseClicked += this.OnUse;
            this.view.PickupClicked += this.OnPickup;
            this.view.RefreshClicked += this.OnRefresh;
            this.inventory.Changed += this.Rebuild;
        }

        public GameplayPanelViewModel ViewModel { get; }

        /// <summary>The rows last handed to the view.</summary>
        public IReadOnlyList<InventoryRow> Rows => this.shown;

        /// <summary>Writes the stat block; unchanged values cost the UI nothing (the view model's Set guard).</summary>
        public void PushStats(in PlayerStatsSnapshot stats)
        {
            this.ViewModel.LevelCaption = stats.LevelCaption;
            this.ViewModel.ManaCaption = stats.ManaCaption;
            this.ViewModel.StatusCaption = stats.StatusCaption;
        }

        /// <summary>Re-labels the rows with the newest content.</summary>
        public void ContentChanged() => this.Rebuild();

        /// <summary>Shows or hides the inventory; showing it asks the server for a fresh view.</summary>
        public void ToggleInventory()
        {
            this.ViewModel.InventoryVisible = !this.ViewModel.InventoryVisible;
            if (this.ViewModel.InventoryVisible) this.OnRefresh();
        }

        /// <summary>The pickup key: the nearest item entity, if any is in reach.</summary>
        public void Pickup() => this.OnPickup();

        public void Dispose()
        {
            this.view.EquipClicked -= this.OnEquip;
            this.view.UnequipClicked -= this.OnUnequip;
            this.view.UseClicked -= this.OnUse;
            this.view.PickupClicked -= this.OnPickup;
            this.view.RefreshClicked -= this.OnRefresh;
            this.inventory.Changed -= this.Rebuild;
            this.lifetime.Cancel();
            this.lifetime.Dispose();
        }

        private void Rebuild()
        {
            var rows = InventoryRows.Build(this.inventory.Current, this.items());
            var error = this.inventory.LastError;
            this.ViewModel.InventoryCaption = string.IsNullOrEmpty(error)
                ? $"Inventory ({rows.Count})"
                : $"Inventory ({rows.Count}) — {error}";

            if (!SameRows(rows, this.shown))
            {
                this.shown = rows;
                this.view.ShowRows(rows);
            }
        }

        private void OnEquip(InventoryRow row)
        {
            if (row == null || !row.CanEquip) return;
            this.inventory.EquipAsync(row.InstanceId, row.EquipSlot, this.lifetime.Token).Forget();
        }

        private void OnUnequip(InventoryRow row)
        {
            if (row == null || !row.CanUnequip) return;
            this.inventory.UnequipAsync(row.Slot, this.lifetime.Token).Forget();
        }

        private void OnUse(InventoryRow row)
        {
            if (row == null || !row.CanUse) return;
            this.inventory.UseAsync(row.InstanceId, this.lifetime.Token).Forget();
        }

        private void OnPickup()
        {
            var target = this.nearestItem();
            if (string.IsNullOrEmpty(target))
            {
                this.ViewModel.InventoryCaption = $"Inventory ({this.shown.Count}) — nothing to pick up";
                return;
            }

            this.inventory.PickupAsync(target, this.lifetime.Token).Forget();
        }

        private void OnRefresh() => this.inventory.RefreshAsync(this.lifetime.Token).Forget();

        private static bool SameRows(IReadOnlyList<InventoryRow> a, IReadOnlyList<InventoryRow> b)
        {
            if (a.Count != b.Count) return false;
            for (var i = 0; i < a.Count; i++)
            {
                if (!a[i].Equals(b[i])) return false;
            }

            return true;
        }
    }
}
