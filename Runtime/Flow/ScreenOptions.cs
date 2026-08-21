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

        /// <summary>A press outside the modal's panel closes it.</summary>
        CloseOnTapOutside = 1 << 2,

        /// <summary>
        /// Keep the presenter and view alive across a close, instead of destroying them.
        /// </summary>
        /// <remarks>
        /// <para><b>Reach for this only with a written reason.</b> The default everywhere else in
        /// this package is destroy-on-close, and it is a deliberate choice rather than an
        /// inherited one: a screen that is rebuilt has no stale state to leak into its next open,
        /// and the cost that would normally argue against it is neutralised by caching the
        /// <c>VisualTreeAsset</c> rather than the view — the expensive part is loading the UXML,
        /// not cloning it.</para>
        ///
        /// <para>Retention brings back precisely the problems that choice avoids: state from the
        /// last open surviving into the next, and bind logic that must now be re-entrant. It
        /// exists because "rebuild a two-thousand-element world map on every open" is a real
        /// objection that caching the asset does not answer. It is also the flag most likely to
        /// be reached for casually, and the resulting bug looks like a data problem rather than a
        /// lifecycle one.</para>
        ///
        /// <para>A screen using this should have a test asserting its bind path still runs on
        /// every push.</para>
        /// </remarks>
        Retain = 1 << 3,
    }
}
