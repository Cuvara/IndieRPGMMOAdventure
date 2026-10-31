namespace Scripts.DI
{
    using System;
    using UnityEngine;

    /// <summary>
    /// The one holder of the gameplay input source, registered in the root scope. Forwards to the
    /// devices by default; a testlab run replaces the source with <see cref="Override"/> and
    /// restores the devices with <see cref="ClearOverride"/>. <c>DotsWorldBridge</c> and
    /// <c>GameplayPanelDriver</c> receive it as <see cref="IGameplayInputSource"/>.
    /// </summary>
    public sealed class GameplayInputRouter : IGameplayInputSource, IDisposable
    {
        private IGameplayInputSource active = DeviceGameplayInputSource.Instance;

        public GameplayInputRouter()
        {
#pragma warning disable CS0618 // the obsolete static forwarder points at the live router
            GameplayInput.AttachRouter(this);
#pragma warning restore CS0618
        }

        /// <summary>The source reads currently go to.</summary>
        public IGameplayInputSource Active => this.active;

        /// <summary>True while a source other than the devices is installed.</summary>
        public bool IsOverridden => !ReferenceEquals(this.active, DeviceGameplayInputSource.Instance);

        /// <summary>Routes every read to <paramref name="source"/> until <see cref="ClearOverride"/>.</summary>
        public void Override(IGameplayInputSource source)
        {
            this.active = source ?? throw new ArgumentNullException(nameof(source));
        }

        /// <summary>Restores the devices.</summary>
        public void ClearOverride() => this.active = DeviceGameplayInputSource.Instance;

        public void ReadMove(out float x, out float y) => this.active.ReadMove(out x, out y);

        public bool JumpPressed() => this.active.JumpPressed();

        public bool CastPressed() => this.active.CastPressed();

        public bool PickupPressed() => this.active.PickupPressed();

        public bool InventoryTogglePressed() => this.active.InventoryTogglePressed();

        public bool TryPointerPosition(out Vector2 position) => this.active.TryPointerPosition(out position);

        public bool TryAimPoint(Vector3 feet, out Vector3 aim) => this.active.TryAimPoint(feet, out aim);

        public void Dispose()
        {
#pragma warning disable CS0618
            if (ReferenceEquals(GameplayInput.Router, this)) GameplayInput.Router = null;
#pragma warning restore CS0618
        }
    }
}
