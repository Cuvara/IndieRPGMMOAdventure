namespace Cuvara.UIToolkit.Flow
{
    using System;

    /// <summary>
    /// How a screen behaves in the stack, declared at registration.
    /// </summary>
    /// <remarks>
    /// <para><b>Every member here has behaviour and a test that fails if the behaviour is
    /// removed.</b> That is a rule, not an aspiration, and it comes from a measurement rather
    /// than a principle: the popup attribute in the framework this package replaces declared
    /// three flags, and two of them — blur and close-on-tap-outside — appeared in exactly one
    /// file each, their own declaration, and were read zero times. An author who set
    /// close-on-tap-outside to false got no behaviour and no diagnostic. Inert API is worse than
    /// absent API, because it looks configured.</para>
    ///
    /// <para>So: do not add a member here before the code that reads it and the test that pins
    /// it. If a flag is coming later, leave it out until later.</para>
    ///
    /// <para><b>Declared at registration rather than inferred from an attribute</b>, because
    /// attribute-driven configuration is read reflectively over a runtime <c>Type</c>, and this
    /// package constructs nothing by <c>Type</c>. A registration line is compiler-checked,
    /// greppable, and survives IL2CPP stripping without a <c>[Preserve]</c> anywhere.</para>
    /// </remarks>
    [Flags]
    public enum ScreenOptions
    {
        /// <summary>An ordinary full screen: goes into the show layer, suspends whatever it covers.</summary>
        None = 0,

        /// <summary>
        /// Goes into the overlay layer, above every screen.
        /// </summary>
        /// <remarks>
        /// A modal only suspends the screen beneath it when it is opaque. Combined with
        /// <see cref="DimsBelow"/> the screen below stays <see cref="ScreenLifecycleState.Active"/>
        /// — still rendering, still receiving pushes — but stops being interactive, which is what
        /// makes a dialog over a live HUD look right.
        /// </remarks>
        Modal = 1 << 0,

        /// <summary>Dim and disable interaction on what is below, without suspending it.</summary>
        DimsBelow = 1 << 1,

        /// <summary>
        /// Tapping outside the modal's view closes it. Only meaningful when combined with
        /// <see cref="Modal"/>.
        /// </summary>
        /// <remarks>
        /// The navigator inserts a full-screen scrim element into the overlay layer behind the
        /// modal's view. A <c>PointerDownEvent</c> on that scrim pops the modal. When
        /// <see cref="DimsBelow"/> is also set, the scrim is semi-transparent; otherwise it is
        /// fully transparent. The scrim is removed when the modal is closed or disposed.
        /// </remarks>
        CloseOnTapOutside = 1 << 2,

        /// <summary>
        /// The screen's scope survives pop and the instance is reused on next push.
        /// </summary>
        /// <remarks>
        /// <para>Destroy-on-close is the default, and is right for most screens. Retain is
        /// for screens where rebuilding is genuinely visible — a very large tree, or a screen
        /// holding expensive derived state (a rendered minimap texture). It buys latency with
        /// memory and with the stale-data hazard.</para>
        ///
        /// <para><b><c>OnBindAsync</c> re-runs on every push</b> even for a retained screen,
        /// with a fresh <see cref="ScreenSubscriptions"/> (old ones disposed first). This is
        /// what makes double-registration structurally impossible, even under retention.</para>
        /// </remarks>
        Retain = 1 << 3,
    }
}
