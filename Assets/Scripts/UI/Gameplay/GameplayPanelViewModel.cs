namespace Scripts.UI.Gameplay
{
    using Cuvara.UIToolkit.ViewModel;
    using Unity.Properties;

    /// <summary>
    /// The gameplay panel's captions as notifying properties (the package's hybrid binding
    /// convention, like <c>HudViewModel</c>). Plain C#: no UI Toolkit element, no network type.
    /// The inventory LIST is not here — it is handed to the view as rows, because a
    /// <c>ListView</c> is fed an items source, not bound property by property.
    /// </summary>
    public sealed class GameplayPanelViewModel : BindableViewModel
    {
        private string levelCaption = "Level —";
        private string manaCaption = "Mana —";
        private string statusCaption = "No status effects";
        private string inventoryCaption = "Inventory";
        private bool inventoryVisible = true;

        [CreateProperty]
        public string LevelCaption
        {
            get => this.levelCaption;
            set => this.Set(ref this.levelCaption, value);
        }

        [CreateProperty]
        public string ManaCaption
        {
            get => this.manaCaption;
            set => this.Set(ref this.manaCaption, value);
        }

        /// <summary>"burning x2 (2.5s), rooted", or "No status effects".</summary>
        [CreateProperty]
        public string StatusCaption
        {
            get => this.statusCaption;
            set => this.Set(ref this.statusCaption, value);
        }

        /// <summary>"Inventory (3)", or the last refusal ("Inventory — inventory_full").</summary>
        [CreateProperty]
        public string InventoryCaption
        {
            get => this.inventoryCaption;
            set => this.Set(ref this.inventoryCaption, value);
        }

        [CreateProperty]
        public bool InventoryVisible
        {
            get => this.inventoryVisible;
            set => this.Set(ref this.inventoryVisible, value);
        }
    }
}
