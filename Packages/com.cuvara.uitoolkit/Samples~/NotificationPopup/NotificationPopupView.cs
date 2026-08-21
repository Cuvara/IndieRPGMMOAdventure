namespace Cuvara.UIToolkit.Samples.NotificationPopup
{
    using System;
    using Cuvara.UIToolkit.View;
    using UnityEngine.UIElements;

    /// <summary>Which button row a <see cref="NotificationPopupView"/> shows.</summary>
    public enum NotificationType
    {
        /// <summary>One OK button.</summary>
        Close,

        /// <summary>Cancel and OK.</summary>
        Option,
    }

    /// <summary>
    /// The smallest complete screen this package can express: a UXML popup, a lifecycle,
    /// and two buttons — with no host framework involved at any point.
    /// </summary>
    /// <remarks>
    /// <para>This is a SAMPLE, and its job is to be readable rather than to be reused. Copy
    /// it into your project and change it; it is not compiled as part of the package
    /// (<c>Samples~</c> is invisible to Unity until imported through the Package Manager).</para>
    ///
    /// <para>Note what is NOT here, because that is the point of the package's shape:</para>
    /// <list type="bullet">
    /// <item>No presenter, no model class, no dependency injection. The view exposes an
    /// event per button and a method to set its content. Whatever drives it — an MVP
    /// presenter, a plain method, a state machine — is your architecture, not the
    /// package's.</item>
    /// <item>No pub/sub bus. <see cref="Confirmed"/> and <see cref="Cancelled"/> are plain
    /// C# events. A host with a signal bus forwards them to it in one line.</item>
    /// <item>No audio, no analytics, no logging. All three are host concerns and all three
    /// were dependencies in the framework this was extracted from.</item>
    /// </list>
    ///
    /// <para><b>The two button rows are toggled with <c>display</c>, not <c>visibility</c>.</b>
    /// A hidden element still occupies its space in the layout, so the panel would keep a
    /// gap the exact size of the row that is not showing. <c>display: none</c> takes it out
    /// of layout, which is what the uGUI habit of calling <c>SetActive(false)</c> did.</para>
    /// </remarks>
    public sealed class NotificationPopupView : BaseUIToolkitView
    {
        /// <summary>Raised when the user accepts, from either button row.</summary>
        public event Action Confirmed;

        /// <summary>Raised when the user declines.</summary>
        public event Action Cancelled;

        private readonly Label  title;
        private readonly Label  content;
        private readonly Button okButton;
        private readonly Button okNoticeButton;
        private readonly Button cancelButton;

        private readonly VisualElement closeGroup;
        private readonly VisualElement noticeGroup;

        public NotificationPopupView(VisualTreeAsset visualTreeAsset) : base(visualTreeAsset)
        {
            // CloneTree returns a TemplateContainer with no size of its own, and a popup is
            // meant to cover the layer it is parented into.
            this.StretchToParent();

            // Queried once, in the constructor, and held. The hierarchy already exists by
            // now — CloneTree is synchronous — so there is no "wait until ready" step.
            this.title          = this.Root.Q<Label>("txt-title");
            this.content        = this.Root.Q<Label>("txt-content");
            this.okButton       = this.Root.Q<Button>("btn-ok");
            this.okNoticeButton = this.Root.Q<Button>("btn-ok-notice");
            this.cancelButton   = this.Root.Q<Button>("btn-cancel");
            this.closeGroup     = this.Root.Q<VisualElement>("close-group");
            this.noticeGroup    = this.Root.Q<VisualElement>("notice-group");

            this.okButton.clicked       += this.OnConfirmed;
            this.okNoticeButton.clicked += this.OnConfirmed;
            this.cancelButton.clicked   += this.OnCancelled;
        }

        /// <summary>Sets the two texts and picks which button row is shown.</summary>
        public void SetContent(string titleText, string contentText, NotificationType type)
        {
            this.title.text   = titleText;
            this.content.text = contentText;

            SetDisplayed(this.closeGroup, type == NotificationType.Close);
            SetDisplayed(this.noticeGroup, type == NotificationType.Option);
        }

        /// <remarks>
        /// Unhooking here rather than leaving it to garbage collection: a
        /// <c>VisualElement</c> is plain managed memory, but <c>clicked</c> is a multicast
        /// delegate holding a reference back to this view, and a view that is destroyed
        /// while something still holds its root would otherwise keep responding.
        /// </remarks>
        protected override void OnDestroySelf()
        {
            this.okButton.clicked       -= this.OnConfirmed;
            this.okNoticeButton.clicked -= this.OnConfirmed;
            this.cancelButton.clicked   -= this.OnCancelled;

            this.Confirmed = null;
            this.Cancelled = null;
        }

        private void OnConfirmed() { this.Confirmed?.Invoke(); }

        private void OnCancelled() { this.Cancelled?.Invoke(); }

        private static void SetDisplayed(VisualElement element, bool displayed)
        {
            element.style.display = displayed ? DisplayStyle.Flex : DisplayStyle.None;
        }
    }
}
