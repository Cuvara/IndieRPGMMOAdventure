namespace Scripts.Gameplay.Inventory
{
    using System;
    using System.Collections.Generic;
    using Shared.GameLogic.Content;
    using Shared.GameLogic.Gameplay;

    /// <summary>
    /// One line of the inventory panel: an item stack plus what the player may ask the server
    /// to do with it. Built by <see cref="InventoryRows.Build"/>; plain data, no UI type.
    /// </summary>
    /// <remarks>
    /// The <c>Can*</c> flags decide which buttons are offered, nothing more. The server decides
    /// whether the request succeeds (slot match, level requirement, ownership) and answers with
    /// a <c>GameplayErrors</c> code when it does not.
    /// </remarks>
    public sealed class InventoryRow : IEquatable<InventoryRow>
    {
        public string InstanceId;
        public string ItemId;

        /// <summary>The content name, or the item id when the content does not know the item.</summary>
        public string Name;

        public uint Quantity;

        /// <summary>True when worn (<c>container == "equipped"</c>).</summary>
        public bool Equipped;

        /// <summary>The slot it is worn in when equipped; empty in the bag.</summary>
        public string Slot;

        public uint BagIndex;

        /// <summary>The lowercase slot the item can be worn in (<c>"weapon"</c>), or empty when it cannot.</summary>
        public string EquipSlot;

        public bool CanEquip;
        public bool CanUnequip;
        public bool CanUse;

        /// <summary>"Rusty Sword x1 [weapon]" — the label the panel shows.</summary>
        public string Caption =>
            (this.Quantity > 1 ? $"{this.Name} x{this.Quantity}" : this.Name) +
            (this.Equipped ? $" [{this.Slot}]" : string.Empty);

        public bool Equals(InventoryRow other) =>
            other != null && this.InstanceId == other.InstanceId && this.ItemId == other.ItemId &&
            this.Name == other.Name && this.Quantity == other.Quantity && this.Equipped == other.Equipped &&
            this.Slot == other.Slot && this.BagIndex == other.BagIndex && this.EquipSlot == other.EquipSlot &&
            this.CanEquip == other.CanEquip && this.CanUnequip == other.CanUnequip && this.CanUse == other.CanUse;

        public override bool Equals(object obj) => this.Equals(obj as InventoryRow);

        public override int GetHashCode() => (this.InstanceId ?? string.Empty).GetHashCode();

        public override string ToString() => this.Caption;
    }

    /// <summary>Turns a wire <see cref="InventoryView"/> into panel rows.</summary>
    public static class InventoryRows
    {
        /// <summary>
        /// Rows for <paramref name="view"/>: equipped items first (by slot), then the bag (by
        /// bag index). <paramref name="items"/> names and classifies each item; null or an item
        /// it does not know leaves the row named by its id and not equippable.
        /// </summary>
        public static List<InventoryRow> Build(InventoryView view, ContentDatabase items)
        {
            var rows = new List<InventoryRow>();
            if (view == null)
            {
                return rows;
            }

            foreach (var stack in view.Items)
            {
                if (stack == null) continue;

                ItemDefinition definition = null;
                var known = items != null && items.TryGetItem(stack.ItemId, out definition) && definition != null;
                var equipped = string.Equals(stack.Container, ItemContainers.Equipped, StringComparison.Ordinal);
                var equipSlot = known && definition.IsEquippable ? SlotName(definition.Slot) : string.Empty;

                rows.Add(new InventoryRow
                {
                    InstanceId = stack.InstanceId,
                    ItemId = stack.ItemId,
                    Name = known && !string.IsNullOrEmpty(definition.Name) ? definition.Name : stack.ItemId,
                    Quantity = stack.Quantity,
                    Equipped = equipped,
                    Slot = equipped ? stack.Slot : string.Empty,
                    BagIndex = stack.BagIndex,
                    EquipSlot = equipSlot,
                    CanEquip = !equipped && equipSlot.Length > 0,
                    CanUnequip = equipped && !string.IsNullOrEmpty(stack.Slot),
                    // Consumables are what is used; wearables are equipped. An item the content
                    // does not know is offered "use" and the server says whether it applies.
                    CanUse = !equipped && (!known || !definition.IsEquippable),
                });
            }

            rows.Sort((a, b) =>
            {
                if (a.Equipped != b.Equipped) return a.Equipped ? -1 : 1;
                if (a.Equipped) return string.CompareOrdinal(a.Slot, b.Slot);
                var byIndex = a.BagIndex.CompareTo(b.BagIndex);
                return byIndex != 0 ? byIndex : string.CompareOrdinal(a.InstanceId, b.InstanceId);
            });

            return rows;
        }

        /// <summary>The wire spelling of a slot: the lowercase enum name (<c>"weapon"</c>).</summary>
        public static string SlotName(ItemSlot slot) => slot == ItemSlot.None ? string.Empty : slot.ToString().ToLowerInvariant();
    }
}
