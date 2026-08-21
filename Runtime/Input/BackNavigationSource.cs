namespace Cuvara.UIToolkit.Input
{
    using System;
    using UnityEngine.UIElements;

    /// <summary>
    /// Raises a plain C# event when the user presses Back — Escape, gamepad B, or the
    /// Android back button, all of which UI Toolkit delivers as
    /// <see cref="NavigationCancelEvent"/>.
    /// </summary>
    /// <remarks>
    /// <para><b>It decides nothing.</b> It does not close a screen, does not know what a
    /// screen is, and does not open a quit dialog. "What does Back mean" is an application
    /// question with a different answer in a menu, in a dialog, and at the root of the
    /// stack, and a package that answered it would be imposing one host's policy on every
    /// other host. Subscribe to <see cref="BackRequested"/> and write the policy where the
    /// policy belongs.</para>
    ///
    /// <para><b>Why an event source rather than a poll.</b> The obvious implementation is
    /// <c>Input.GetKeyDown(KeyCode.Escape)</c> in an update loop. That is the legacy Input
    /// Manager, and in a project whose Active Input Handling is "Input System Package
    /// (New)" — where <c>ENABLE_LEGACY_INPUT_MANAGER</c> is undefined —
    /// <c>UnityEngine.Input</c> throws rather than returning false, so such a poll is not
    /// merely disabled, it is a per-frame exception. <c>NavigationCancelEvent</c> is raised
    /// by whichever input backend is active, needs no update loop, and covers gamepad and
    /// Android back for free.</para>
    ///
    /// <para><b>Where the callback is registered, and the caveat.</b> On the element handed
    /// in, with <c>TrickleDown</c>, so a cancel aimed at any focused descendant passes
    /// through the root first and is handled once regardless of what has focus. Navigation
    /// events are routed by the panel's focus controller; if the panel has no focused
    /// element at all, whether the event reaches the root is Unity's dispatch behaviour and
    /// not something this class can guarantee. Register on the panel root for the widest
    /// coverage.</para>
    /// </remarks>
    public sealed class BackNavigationSource : IDisposable
    {
        private readonly VisualElement root;

        private bool disposed;

        /// <summary>Raised once per Back press, while <see cref="Enabled"/> is true.</summary>
        public event Action BackRequested;

        /// <summary>
        /// Gate for this source, defaulting to true.
        /// </summary>
        /// <remarks>
        /// Constructing the source IS the opt-in, so requiring a second flag to be set as
        /// well would just be a way to have it silently do nothing. Set it false to suspend
        /// Back handling — during a cutscene, say — without tearing the registration down.
        /// </remarks>
        public bool Enabled { get; set; } = true;

        /// <summary>
        /// Whether a handled event stops propagating. True by default.
        /// </summary>
        /// <remarks>
        /// A cancel that keeps trickling after the screen flow has acted on it can dismiss
        /// a focused control underneath as well, so one press closes two things. Set false
        /// only if something below genuinely needs to see the same press.
        /// </remarks>
        public bool ConsumeEvent { get; set; } = true;

        /// <summary>How many Back presses this source has raised. For tests and telemetry.</summary>
        public int HandledCount { get; private set; }

        /// <summary>Registers a cancel handler on <paramref name="root"/>.</summary>
        public BackNavigationSource(VisualElement root)
        {
            this.root = root ?? throw new ArgumentNullException(nameof(root));

            this.root.RegisterCallback<NavigationCancelEvent>(this.OnNavigationCancel, TrickleDown.TrickleDown);
        }

        private void OnNavigationCancel(NavigationCancelEvent evt)
        {
            if (this.disposed || !this.Enabled) return;

            // Nothing subscribed means nothing wants the press. Consuming it anyway would
            // silently swallow Back for whatever is underneath.
            if (this.BackRequested == null) return;

            ++this.HandledCount;

            if (this.ConsumeEvent) evt.StopPropagation();

            this.BackRequested.Invoke();
        }

        public void Dispose()
        {
            if (this.disposed) return;
            this.disposed = true;

            this.root.UnregisterCallback<NavigationCancelEvent>(this.OnNavigationCancel, TrickleDown.TrickleDown);
            this.BackRequested = null;
        }
    }
}
