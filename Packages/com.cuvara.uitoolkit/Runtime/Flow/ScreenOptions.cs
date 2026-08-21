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

        // CloseOnTapOutside deliberately does not exist yet. It was declared here with no
        // code reading it and no test pinning it, on the understanding that behaviour would
        // follow — which is exactly the excuse this enum's rule refuses. The flag it replaced in
        // the framework this package came from was doubtless also going to be implemented later;
        // it shipped for years, read zero times, silently doing nothing to anyone who set it.
        // It comes back in the commit that gives it behaviour and a test that fails when the
        // behaviour is removed, and it takes bit 2 when it does.

        // Retain deliberately does not exist yet either, and for the same reason — the
        // navigator has exactly one teardown path and nothing reads this. Its argument is real
        // ("do not rebuild a two-thousand-element world map on every open"), and it is answered
        // first by caching the VisualTreeAsset rather than the view, which is the cheap half.
        // If that turns out to be insufficient, Retain returns WITH the branch in the navigator
        // that honours it and a test asserting the bind path still re-runs on every push —
        // because retention reintroduces precisely the stale-state problems destroy-on-close
        // exists to avoid, and a flag that silently does nothing about them is worse than none.
    }
}
