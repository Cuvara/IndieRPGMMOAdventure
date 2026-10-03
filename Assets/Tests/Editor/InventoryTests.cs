namespace Tests.Editor
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using Cysharp.Threading.Tasks;
    using NUnit.Framework;
    using Scripts.Gameplay.Inventory;
    using Scripts.Gameplay.Stats;
    using Scripts.UI.Gameplay;
    using Shared.GameLogic.Content;
    using Shared.GameLogic.Gameplay;

    /// <summary>
    /// The inventory leg of Core v3 (ADR-30.4): rows built from the wire view and the content,
    /// the service's opcodes and payloads through Shared.GameLogic's codec, push 100, and the
    /// panel presenter's commands — all against fakes, no connection.
    /// </summary>
    public sealed class InventoryTests
    {
        private static readonly ContentDatabase Items = new ContentDatabase(
            new[]
            {
                new ItemDefinition("rusty_sword", "Rusty Sword", ItemSlot.Weapon, ItemRarity.Common, 1, 3, 0, 1),
                new ItemDefinition("health_potion", "Health Potion", ItemSlot.None, ItemRarity.Common, 20, 0, 0, 1),
                new ItemDefinition("leather_cap", "Leather Cap", ItemSlot.Head, ItemRarity.Common, 1, 0, 1, 1),
            },
            "test");

        private static ItemStack Stack(string instance, string item, uint quantity, string container, string slot = "", uint bagIndex = 0) =>
            new ItemStack { InstanceId = instance, ItemId = item, Quantity = quantity, Container = container, Slot = slot, BagIndex = bagIndex };

        private static InventoryView View(params ItemStack[] stacks)
        {
            var view = new InventoryView();
            view.Items.AddRange(stacks);
            return view;
        }

        // ---- rows ----

        [Test]
        public void Rows_EquippedFirst_ThenBagByIndex_WithTheRightActions()
        {
            var rows = InventoryRows.Build(
                View(
                    Stack("i3", "health_potion", 5, ItemContainers.Bag, bagIndex: 2),
                    Stack("i1", "rusty_sword", 1, ItemContainers.Equipped, "weapon"),
                    Stack("i2", "leather_cap", 1, ItemContainers.Bag, bagIndex: 0),
                    Stack("i4", "mystery_item", 1, ItemContainers.Bag, bagIndex: 1)),
                Items);

            Assert.That(rows.ConvertAll(r => r.InstanceId), Is.EqualTo(new[] { "i1", "i2", "i4", "i3" }));

            var sword = rows[0];
            Assert.That(sword.Equipped && sword.CanUnequip && !sword.CanEquip && !sword.CanUse, Is.True);
            Assert.That(sword.Caption, Is.EqualTo("Rusty Sword [weapon]"));

            var cap = rows[1];
            Assert.That(cap.CanEquip && !cap.CanUse && !cap.CanUnequip, Is.True);
            Assert.That(cap.EquipSlot, Is.EqualTo("head"));

            var unknown = rows[2];
            Assert.That(unknown.Name, Is.EqualTo("mystery_item"), "unknown to the content: named by id");
            Assert.That(unknown.CanEquip, Is.False);
            Assert.That(unknown.CanUse, Is.True, "offered; the server decides");

            var potions = rows[3];
            Assert.That(potions.Caption, Is.EqualTo("Health Potion x5"));
            Assert.That(potions.CanUse && !potions.CanEquip, Is.True);
        }

        [Test]
        public void Rows_WithoutContent_OrView()
        {
            Assert.That(InventoryRows.Build(null, Items), Is.Empty);
            var rows = InventoryRows.Build(View(Stack("i1", "rusty_sword", 1, ItemContainers.Bag)), null);
            Assert.That(rows[0].Name, Is.EqualTo("rusty_sword"));
            Assert.That(rows[0].CanEquip, Is.False, "no content: no slot to equip into");
            Assert.That(InventoryRows.SlotName(ItemSlot.Trinket), Is.EqualTo("trinket"));
            Assert.That(InventoryRows.SlotName(ItemSlot.None), Is.Empty);
        }

        // ---- service ----

        private sealed class FakeChannel : IGameplayCommandChannel
        {
            public readonly List<(uint Opcode, byte[] Payload)> Sent = new List<(uint, byte[])>();
            public GameplayCommandResult Next = new GameplayCommandResult(true, null, null);

            public event Action<uint, byte[]> PushReceived;

            public UniTask<GameplayCommandResult> SendAsync(uint opcode, byte[] payload, CancellationToken cancellationToken)
            {
                this.Sent.Add((opcode, payload));
                return UniTask.FromResult(this.Next);
            }

            public void Push(uint opcode, byte[] payload) => this.PushReceived?.Invoke(opcode, payload);
        }

        [Test]
        public void Refresh_SendsOpcode1_AndAdoptsTheView_AnnouncedOnPump()
        {
            var channel = new FakeChannel
            {
                Next = new GameplayCommandResult(true, null, View(Stack("i1", "rusty_sword", 1, ItemContainers.Bag)).ToByteArray()),
            };
            var service = new InventoryService(channel);
            var changed = 0;
            service.Changed += () => changed++;

            Assert.That(service.RefreshAsync(CancellationToken.None).GetAwaiter().GetResult(), Is.True);

            Assert.That(channel.Sent[0].Opcode, Is.EqualTo(GameplayOpcodes.Inventory));
            Assert.That(channel.Sent[0].Payload, Is.Empty, "InventoryRequest encodes to zero bytes");
            Assert.That(service.Current.Items.Count, Is.EqualTo(1));
            Assert.That(changed, Is.EqualTo(0), "nothing is raised off the pump");
            service.Pump();
            Assert.That(changed, Is.EqualTo(1));
            service.Pump();
            Assert.That(changed, Is.EqualTo(1), "one change, one event");
        }

        [Test]
        public void Commands_EncodeTheirRequests_WithSharedGameLogicsCodec()
        {
            var channel = new FakeChannel();
            var service = new InventoryService(channel);

            service.PickupAsync("item:42", CancellationToken.None).GetAwaiter().GetResult();
            service.EquipAsync("inst-1", "weapon", CancellationToken.None).GetAwaiter().GetResult();
            service.UnequipAsync("head", CancellationToken.None).GetAwaiter().GetResult();
            service.UseAsync("inst-2", CancellationToken.None).GetAwaiter().GetResult();

            Assert.That(channel.Sent.ConvertAll(s => s.Opcode), Is.EqualTo(new[]
            {
                GameplayOpcodes.Pickup, GameplayOpcodes.Equip, GameplayOpcodes.Unequip, GameplayOpcodes.UseItem,
            }));

            Assert.That(PickupRequest.TryRead(channel.Sent[0].Payload, out var pickup) && pickup.EntityId == "item:42", Is.True);
            Assert.That(EquipRequest.TryRead(channel.Sent[1].Payload, out var equip) && equip.InstanceId == "inst-1" && equip.Slot == "weapon", Is.True);
            Assert.That(UnequipRequest.TryRead(channel.Sent[2].Payload, out var unequip) && unequip.Slot == "head", Is.True);
            Assert.That(UseItemRequest.TryRead(channel.Sent[3].Payload, out var use) && use.InstanceId == "inst-2", Is.True);
        }

        [Test]
        public void Push100_ReplacesTheView_AndARefusalIsKept()
        {
            var channel = new FakeChannel();
            var service = new InventoryService(channel);

            var changed = new InventoryChanged { View = View(Stack("a", "health_potion", 3, ItemContainers.Bag)) };
            channel.Push(GameplayOpcodes.InventoryChanged, changed.ToByteArray());
            channel.Push(999u, new byte[] { 1, 2, 3 }); // an unknown push is not ours

            Assert.That(service.Current.Items[0].Quantity, Is.EqualTo(3));
            Assert.That(service.PushesApplied, Is.EqualTo(1));

            channel.Next = new GameplayCommandResult(false, GameplayErrors.InventoryFull, null);
            Assert.That(service.PickupAsync("item:1", CancellationToken.None).GetAwaiter().GetResult(), Is.False);
            Assert.That(service.LastError, Is.EqualTo(GameplayErrors.InventoryFull));
            Assert.That(service.Current.Items.Count, Is.EqualTo(1), "a refusal changes nothing");
        }

        [Test]
        public void MalformedViewPayload_IsAnError_NotAnException()
        {
            var channel = new FakeChannel { Next = new GameplayCommandResult(true, null, new byte[] { 0x0A, 0xFF }) };
            var service = new InventoryService(channel);

            Assert.That(service.RefreshAsync(CancellationToken.None).GetAwaiter().GetResult(), Is.False);
            Assert.That(service.LastError, Is.EqualTo(GameplayErrors.InvalidPayload));
        }

        // ---- presenter ----

        private sealed class FakeView : IGameplayPanelView
        {
            public GameplayPanelViewModel Bound;
            public IReadOnlyList<InventoryRow> Rows;
            public int ShowCalls;

            public event Action<InventoryRow> EquipClicked;
            public event Action<InventoryRow> UnequipClicked;
            public event Action<InventoryRow> UseClicked;
            public event Action PickupClicked;
            public event Action RefreshClicked;

            public void Bind(GameplayPanelViewModel viewModel) => this.Bound = viewModel;

            public void ShowRows(IReadOnlyList<InventoryRow> rows)
            {
                this.Rows = rows;
                this.ShowCalls++;
            }

            public void ClickEquip(InventoryRow row) => this.EquipClicked?.Invoke(row);
            public void ClickUnequip(InventoryRow row) => this.UnequipClicked?.Invoke(row);
            public void ClickUse(InventoryRow row) => this.UseClicked?.Invoke(row);
            public void ClickPickup() => this.PickupClicked?.Invoke();
            public void ClickRefresh() => this.RefreshClicked?.Invoke();
        }

        [Test]
        public void Presenter_ShowsTheServersView_AndClicksBecomeCommands()
        {
            var channel = new FakeChannel();
            var service = new InventoryService(channel);
            var view = new FakeView();
            string nearest = null;
            var presenter = new GameplayPanelPresenter(view, service, () => Items, () => nearest);

            Assert.That(view.Bound, Is.SameAs(presenter.ViewModel));

            var two = View(Stack("s", "rusty_sword", 1, ItemContainers.Bag), Stack("p", "health_potion", 2, ItemContainers.Bag, bagIndex: 1));
            // Every answer that carries a view carries this same one, as a server with nothing
            // changed would.
            channel.Next = new GameplayCommandResult(true, null, two.ToByteArray());
            channel.Push(GameplayOpcodes.InventoryChanged, new InventoryChanged { View = two }.ToByteArray());
            service.Pump();

            Assert.That(view.Rows.Count, Is.EqualTo(2));
            Assert.That(presenter.ViewModel.InventoryCaption, Is.EqualTo("Inventory (2)"));

            view.ClickEquip(view.Rows[0]);
            view.ClickUse(view.Rows[1]);
            view.ClickUnequip(view.Rows[0]); // not equipped: offered no unequip, sends nothing
            view.ClickPickup();               // nothing in reach: says so, sends nothing
            Assert.That(presenter.ViewModel.InventoryCaption, Does.Contain("nothing to pick up"));
            nearest = "item:7";
            view.ClickPickup();
            view.ClickRefresh();

            Assert.That(channel.Sent.ConvertAll(s => s.Opcode), Is.EqualTo(new[]
            {
                GameplayOpcodes.Equip, GameplayOpcodes.UseItem, GameplayOpcodes.Pickup, GameplayOpcodes.Inventory,
            }));
            Assert.That(EquipRequest.TryRead(channel.Sent[0].Payload, out var equip) && equip.Slot == "weapon", Is.True, "the item's content slot");

            // Same view again: rows unchanged, the view is not re-fed.
            var shows = view.ShowCalls;
            service.Pump();
            presenter.ContentChanged();
            Assert.That(view.ShowCalls, Is.EqualTo(shows));

            presenter.Dispose();
        }

        [Test]
        public void Presenter_PushesStatCaptions_AndTogglesTheInventory()
        {
            var channel = new FakeChannel();
            var view = new FakeView();
            var presenter = new GameplayPanelPresenter(view, new InventoryService(channel), () => Items, null);

            presenter.PushStats(new PlayerStatsSnapshot(true, 3, 85, "burning x2"));
            Assert.That(presenter.ViewModel.LevelCaption, Is.EqualTo("Level 3"));
            Assert.That(presenter.ViewModel.ManaCaption, Is.EqualTo("Mana 85"));
            Assert.That(presenter.ViewModel.StatusCaption, Is.EqualTo("burning x2"));

            presenter.PushStats(PlayerStatsSnapshot.Absent);
            Assert.That(presenter.ViewModel.LevelCaption, Is.EqualTo("Level —"));

            Assert.That(presenter.ViewModel.InventoryVisible, Is.True);
            presenter.ToggleInventory();
            Assert.That(presenter.ViewModel.InventoryVisible, Is.False);
            Assert.That(channel.Sent, Is.Empty);
            presenter.ToggleInventory();
            Assert.That(channel.Sent.Count, Is.EqualTo(1), "showing the inventory asks for a fresh view");
        }
    }
}
