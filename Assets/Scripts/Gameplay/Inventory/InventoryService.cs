namespace Scripts.Gameplay.Inventory
{
    using System;
    using System.Threading;
    using Cysharp.Threading.Tasks;
    using Shared.GameLogic.Gameplay;

    /// <summary>What the inventory panel may ask of the inventory.</summary>
    public interface IInventoryService
    {
        /// <summary>The newest complete inventory the server sent; null before the first one.</summary>
        InventoryView Current { get; }

        /// <summary>The last refusal or channel failure (<c>"inventory_full"</c>), or empty.</summary>
        string LastError { get; }

        /// <summary>
        /// Raised from <see cref="Pump"/> — on the caller's (main) thread — after
        /// <see cref="Current"/> or <see cref="LastError"/> changed.
        /// </summary>
        event Action Changed;

        /// <summary>Opcode 1: ask for the whole inventory.</summary>
        UniTask<bool> RefreshAsync(CancellationToken cancellationToken);

        /// <summary>Opcode 2: pick up the world item entity <paramref name="entityId"/>.</summary>
        UniTask<bool> PickupAsync(string entityId, CancellationToken cancellationToken);

        /// <summary>Opcode 3: wear bag item <paramref name="instanceId"/> in <paramref name="slot"/>.</summary>
        UniTask<bool> EquipAsync(string instanceId, string slot, CancellationToken cancellationToken);

        /// <summary>Opcode 4: move whatever is worn in <paramref name="slot"/> back to the bag.</summary>
        UniTask<bool> UnequipAsync(string slot, CancellationToken cancellationToken);

        /// <summary>Opcode 5: use (consume) bag item <paramref name="instanceId"/>.</summary>
        UniTask<bool> UseAsync(string instanceId, CancellationToken cancellationToken);

        /// <summary>Delivers pending changes as <see cref="Changed"/>. Call once per frame on the main thread.</summary>
        void Pump();
    }

    /// <summary>
    /// The client half of the inventory commands (ADR-30.4, <c>gameplay.proto</c> opcodes 1-5
    /// and push 100), encoded with <c>Shared.GameLogic</c>'s gameplay codec — the same
    /// implementation the server decodes with.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The server's view is the only state.</b> Every answer that carries an
    /// <see cref="InventoryView"/> (opcodes 1 and 2, push 100) replaces <see cref="Current"/>
    /// whole — the view is always complete, never a delta, so a missed push cannot drift. Equip,
    /// unequip and use answer with an empty payload; their effect arrives as push 100. Nothing
    /// here edits the inventory optimistically.
    /// </para>
    /// <para>
    /// <b>Threads.</b> A push may arrive on a network thread, and an awaited result may resume on
    /// one. State is therefore written under a lock and announced only by <see cref="Pump"/>,
    /// which the owner calls from the main thread, so <see cref="Changed"/> handlers may touch
    /// UI freely.
    /// </para>
    /// </remarks>
    public sealed class InventoryService : IInventoryService, IDisposable
    {
        private readonly IGameplayCommandChannel channel;
        private readonly object gate = new object();
        private InventoryView current;
        private string lastError = string.Empty;
        private bool dirty;

        public InventoryService(IGameplayCommandChannel channel)
        {
            this.channel = channel ?? throw new ArgumentNullException(nameof(channel));
            this.channel.PushReceived += this.OnPush;
        }

        public event Action Changed;

        public InventoryView Current
        {
            get { lock (this.gate) return this.current; }
        }

        public string LastError
        {
            get { lock (this.gate) return this.lastError; }
        }

        /// <summary>Pushes received and applied so far. Diagnostics and tests.</summary>
        public int PushesApplied { get; private set; }

        public UniTask<bool> RefreshAsync(CancellationToken cancellationToken) =>
            this.SendAsync(GameplayOpcodes.Inventory, new InventoryRequest().ToByteArray(), expectsView: true, cancellationToken);

        public UniTask<bool> PickupAsync(string entityId, CancellationToken cancellationToken)
        {
            if (string.IsNullOrEmpty(entityId)) throw new ArgumentException("entity id must not be empty", nameof(entityId));
            return this.SendAsync(GameplayOpcodes.Pickup, new PickupRequest { EntityId = entityId }.ToByteArray(), expectsView: true, cancellationToken);
        }

        public UniTask<bool> EquipAsync(string instanceId, string slot, CancellationToken cancellationToken)
        {
            if (string.IsNullOrEmpty(instanceId)) throw new ArgumentException("instance id must not be empty", nameof(instanceId));
            if (string.IsNullOrEmpty(slot)) throw new ArgumentException("slot must not be empty", nameof(slot));
            return this.SendAsync(GameplayOpcodes.Equip, new EquipRequest { InstanceId = instanceId, Slot = slot }.ToByteArray(), expectsView: false, cancellationToken);
        }

        public UniTask<bool> UnequipAsync(string slot, CancellationToken cancellationToken)
        {
            if (string.IsNullOrEmpty(slot)) throw new ArgumentException("slot must not be empty", nameof(slot));
            return this.SendAsync(GameplayOpcodes.Unequip, new UnequipRequest { Slot = slot }.ToByteArray(), expectsView: false, cancellationToken);
        }

        public UniTask<bool> UseAsync(string instanceId, CancellationToken cancellationToken)
        {
            if (string.IsNullOrEmpty(instanceId)) throw new ArgumentException("instance id must not be empty", nameof(instanceId));
            return this.SendAsync(GameplayOpcodes.UseItem, new UseItemRequest { InstanceId = instanceId }.ToByteArray(), expectsView: false, cancellationToken);
        }

        public void Pump()
        {
            bool raise;
            lock (this.gate)
            {
                raise = this.dirty;
                this.dirty = false;
            }

            if (raise)
            {
                this.Changed?.Invoke();
            }
        }

        public void Dispose()
        {
            this.channel.PushReceived -= this.OnPush;
        }

        private async UniTask<bool> SendAsync(uint opcode, byte[] payload, bool expectsView, CancellationToken cancellationToken)
        {
            var result = await this.channel.SendAsync(opcode, payload, cancellationToken);
            if (!result.Ok)
            {
                this.SetError(string.IsNullOrEmpty(result.Error) ? "command_failed" : result.Error);
                return false;
            }

            if (expectsView)
            {
                if (!InventoryView.TryRead(result.Payload, out var view))
                {
                    this.SetError(GameplayErrors.InvalidPayload);
                    return false;
                }

                this.SetView(view);
                return true;
            }

            this.SetError(string.Empty);
            return true;
        }

        private void OnPush(uint opcode, byte[] payload)
        {
            if (opcode != GameplayOpcodes.InventoryChanged)
            {
                return;
            }

            if (InventoryChanged.TryRead(payload ?? Array.Empty<byte>(), out var changed))
            {
                // An InventoryChanged with no view is "the inventory is empty", not "no news":
                // the message exists only to carry the complete new state.
                this.SetView(changed.View ?? new InventoryView());
                this.PushesApplied++;
            }
            else
            {
                this.SetError(GameplayErrors.InvalidPayload);
            }
        }

        private void SetView(InventoryView view)
        {
            lock (this.gate)
            {
                this.current = view;
                this.lastError = string.Empty;
                this.dirty = true;
            }
        }

        private void SetError(string error)
        {
            lock (this.gate)
            {
                if (this.lastError == error) return;
                this.lastError = error;
                this.dirty = true;
            }
        }
    }
}
