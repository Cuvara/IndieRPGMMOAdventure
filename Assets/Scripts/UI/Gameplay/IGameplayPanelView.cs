namespace Scripts.UI.Gameplay
{
    using System;
    using System.Collections.Generic;
    using Scripts.Gameplay.Inventory;

    /// <summary>What the gameplay panel presenter may say to, and hear from, its view.</summary>
    public interface IGameplayPanelView
    {
        /// <summary>Makes <paramref name="viewModel"/> the captions this view displays, now and as they change.</summary>
        void Bind(GameplayPanelViewModel viewModel);

        /// <summary>Replaces the inventory rows shown.</summary>
        void ShowRows(IReadOnlyList<InventoryRow> rows);

        event Action<InventoryRow> EquipClicked;

        event Action<InventoryRow> UnequipClicked;

        event Action<InventoryRow> UseClicked;

        event Action PickupClicked;

        event Action RefreshClicked;
    }
}
