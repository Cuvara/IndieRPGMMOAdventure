namespace Cuvara.UIToolkit.Flow
{
    /// <summary>
    /// Every state a screen is actually in, from registration to disposal.
    /// </summary>
    /// <remarks>
    /// <para><b>This enum carries the whole lifecycle on purpose.</b> The framework this package
    /// replaces had a four-value status — opened, closed, hidden, destroyed — that could not
    /// answer "is this loaded?" or "is an open in flight?". Because it was public and switched
    /// on across six repositories it could not be extended either, so the answer there was a
    /// second, parallel enum. Two enums describing one object's state is a bug generator: they
    /// drift, and every reader has to know which one is authoritative.</para>
    ///
    /// <para>So the rule for this package is that there is one, and it is complete. If a state
    /// the flow really has is missing here, add it here rather than tracking it beside this.</para>
    ///
    /// <para><b>The values are ordered by progression</b>, not alphabetically and not by
    /// convenience, so <c>&lt;</c> and <c>&gt;</c> comparisons mean what they look like:
    /// everything before <see cref="Active"/> is still coming up, and
    /// <see cref="Closing"/>/<see cref="Disposed"/> are terminal. Do not renumber them to insert
    /// a state in the middle without checking for such comparisons.</para>
    /// </remarks>
    public enum ScreenLifecycleState
    {
        /// <summary>Known to the container, never opened. The state a screen type sits in between pushes.</summary>
        Registered = 0,

        /// <summary>Scope created, asset loading, view being constructed. Asynchronous, and may fail.</summary>
        Creating = 1,

        /// <summary>
        /// The view exists and the presenter is resolved; nothing has been bound yet.
        /// </summary>
        /// <remarks>
        /// There is no "ready to use" flag anywhere in this package, and this state is why one is
        /// not needed: <c>CloneTree</c> is synchronous, so a view is usable the instant it is
        /// constructed. The uGUI world needs such a flag for the frame gap between
        /// <c>Instantiate</c> and <c>Awake</c>; there is no gap here.
        /// </remarks>
        Constructed = 2,

        /// <summary><c>OnBindAsync</c> is running. Asynchronous, calls services, and may fail.</summary>
        Binding = 3,

        /// <summary>Parented into the show layer, running its intro transition.</summary>
        Opening = 4,

        /// <summary>Visible, interactive, receiving pushes. The only state in which a screen does work.</summary>
        Active = 5,

        /// <summary>
        /// Covered by another screen: moved to the hidden layer, <c>display:none</c>, sinks
        /// unbound — but its scope is alive and all its state is retained.
        /// </summary>
        /// <remarks>
        /// Distinct from <see cref="Disposed"/> in the way that matters most: a suspended screen
        /// resumes without rebinding, and a disposed one does not exist. Conflating the two is
        /// what makes a "hidden" screen that has had <c>Dispose()</c> called on it, which is the
        /// single worst inherited hazard this package is written to avoid.
        /// </remarks>
        Suspended = 6,

        /// <summary>Running its outro transition and detaching. Terminal — a screen never leaves this except to <see cref="Disposed"/>.</summary>
        Closing = 7,

        /// <summary>The scope is disposed. Presenter, view, subscriptions and screen services are all released. Terminal and final.</summary>
        Disposed = 8,
    }
}
