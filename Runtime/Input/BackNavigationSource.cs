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

        /// <summary>
        /// Raised once per Back press, while <see cref="Enabled"/> is true. Legacy path.
        /// </summary>
        /// <remarks>
        /// <b>An <c>Action</c> cannot report whether it did anything</b>, so a source driven
        /// only through this event has to assume every press was handled and consume it. At
        /// the root screen that means the press is swallowed and the application never sees
        /// it — on Android, Back stops exiting the app.
        ///
        /// <para>Prefer <see cref="BackHandler"/>, which returns a bool and lets the source
        /// decline to consume what the host did not handle. This event is kept because
        /// removing it would be a source-breaking change to a published package for the sake
        /// of tidiness; when both are set, <see cref="BackHandler"/> wins and this is not
        /// raised.</para>
        /// </remarks>
        public event Action BackRequested;

        /// <summary>
        /// Set by a host that can report whether Back was actually handled. Preferred.
        /// </summary>
        /// <remarks>
        /// Return true when something was closed, false when nothing was. The source consumes
        /// the event only on true, so an unhandled press keeps propagating and reaches the
        /// application — which is what makes Back exit at the root instead of being eaten by
        /// a UI layer that had nothing to close.
        ///
        /// <para><see cref="IScreenNavigator.HandleBack"/> already has this shape, so wiring
        /// is one line and needs no lambda:
        /// <code>source.BackHandler = navigator.HandleBack;</code></para>
        /// </remarks>
        public Func<bool> BackHandler { get; set; }

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

        /// <summary>
        /// How many Back presses this source saw, whether handled or not.
        /// </summary>
        /// <remarks>
        /// <para><see cref="HandledCount"/> counts presses that were acted on.
        /// <see cref="SeenCount"/> counts every press that reached this source while it was
        /// enabled and not disposed — including those where no handler was set, or where the
        /// handler returned false. The difference is "how many presses reached the platform's
        /// own Back" — on Android, that is how many exit-the-app presses landed.</para>
        /// </remarks>
        public int SeenCount { get; private set; }

        /// <summary>Registers a cancel handler on <paramref name="root"/>.</summary>
        public BackNavigationSource(VisualElement root)
        {
            this.root = root ?? throw new ArgumentNullException(nameof(root));

            this.root.RegisterCallback<NavigationCancelEvent>(this.OnNavigationCancel, TrickleDown.TrickleDown);
        }

        private void OnNavigationCancel(NavigationCancelEvent evt)
        {
            if (this.disposed || !this.Enabled) return;

            ++this.SeenCount;

            // The handler path, when a host supplied one. It is the only path that can tell
            // "I closed something" from "there was nothing to close", which is the whole
            // difference between Back working and Back being swallowed at the root.
            if (this.BackHandler != null)
            {
                if (!this.BackHandler.Invoke()) return;   // unhandled: do not consume

                ++this.HandledCount;

                if (this.ConsumeEvent) evt.StopPropagation();

                return;
            }

            // Legacy event path. Nothing subscribed means nothing wants the press; consuming
            // it anyway would silently swallow Back for whatever is underneath. With a
            // subscriber we must assume it handled the press, because an Action cannot say.
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
            this.BackHandler   = null;
        }
    }
}
