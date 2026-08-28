namespace Cuvara.UIToolkit.View
{
    using System;
    using Cuvara.UIToolkit.Core;
    using UnityEngine.UIElements;

    /// <summary>The UI Toolkit implementation of <see cref="IViewLayer"/>: a VisualElement.</summary>
    /// <remarks>
    /// Constructed once per root and cached, not allocated per reparent — a screen flow
    /// reparents on every open and every close, and a wrapper per call would put garbage
    /// on a path that runs during transitions.
    /// </remarks>
    public sealed class VisualElementViewLayer : IViewLayer
    {
        public VisualElement Element { get; }

        public VisualElementViewLayer(VisualElement element)
        {
            this.Element = element ?? throw new ArgumentNullException(nameof(element));
        }
    }

    /// <summary>The UI Toolkit implementation of <see cref="IViewSurface"/>: a VisualElement.</summary>
    public sealed class VisualElementViewSurface : IViewSurface
    {
        private readonly VisualElement element;

        public VisualElementViewSurface(VisualElement element)
        {
            this.element = element ?? throw new ArgumentNullException(nameof(element));
        }

        public void SetParent(IViewLayer layer)
        {
            // Fails loudly rather than silently doing nothing. Handing a layer from another
            // backend to a UI Toolkit surface is a wiring mistake, and a no-op here would
            // surface much later as a screen that renders nowhere at all, with nothing in
            // the log to say why.
            if (layer is not VisualElementViewLayer visualElementLayer)
            {
                throw new InvalidOperationException(
                    $"A UI Toolkit view can only be parented into a {nameof(VisualElementViewLayer)}, got " +
                    $"{layer?.GetType().Name ?? "null"}.");
            }

            // Add() detaches from the previous parent first, so this is a reparent, not a
            // double-parent — matching Transform.SetParent's semantics.
            visualElementLayer.Element.Add(this.element);
        }
    }
}
