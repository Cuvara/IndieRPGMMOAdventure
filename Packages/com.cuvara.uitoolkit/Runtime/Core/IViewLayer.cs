namespace Cuvara.UIToolkit.Core
{
    /// <summary>Somewhere a view can be parented.</summary>
    /// <remarks>
    /// <para>Deliberately empty. A layer is an identity, not a capability: the only thing
    /// anyone does with one is hand it to <see cref="IViewSurface.SetParent"/>, and the
    /// surface is what knows how to perform the adoption. Putting an <c>Add</c> method here
    /// would force every layer implementation to know about every surface implementation.</para>
    ///
    /// <para>The package ships one implementation, <c>VisualElementViewLayer</c>. A host
    /// that also has uGUI screens keeps its own <c>Transform</c>-backed layer type and its
    /// own surface type; the two families never have to meet, and a surface that is handed
    /// the wrong family throws rather than silently doing nothing.</para>
    /// </remarks>
    public interface IViewLayer
    {
    }

    /// <summary>The other half: a thing that can be moved into an <see cref="IViewLayer"/>.</summary>
    public interface IViewSurface
    {
        /// <summary>
        /// Reparents this surface into <paramref name="layer"/>, detaching it from wherever
        /// it currently sits.
        /// </summary>
        /// <exception cref="System.InvalidOperationException">
        /// <paramref name="layer"/> is not a layer this surface can be parented into — for
        /// example a uGUI layer handed to a UI Toolkit surface.
        /// </exception>
        void SetParent(IViewLayer layer);
    }
}
