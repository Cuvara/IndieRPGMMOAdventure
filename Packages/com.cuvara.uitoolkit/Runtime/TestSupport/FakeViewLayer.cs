namespace Cuvara.UIToolkit.TestSupport
{
    using System.Collections.Generic;
    using Cuvara.UIToolkit.Core;

    /// <summary>
    /// An <see cref="IViewLayer"/> that records what was parented into it, with no panel.
    /// </summary>
    /// <remarks>
    /// Use <see cref="Children"/> to assert parenting order, or <see cref="Count"/> after a
    /// pop to assert that the view was detached.
    /// </remarks>
    public sealed class FakeViewLayer : IViewLayer
    {
        private readonly List<IViewSurface> children = new();

        /// <summary>Everything currently parented into this layer.</summary>
        public IReadOnlyList<IViewSurface> Children => this.children;

        /// <summary>How many surfaces are parented here.</summary>
        public int Count => this.children.Count;

        /// <summary>Records <paramref name="surface"/> as a child.</summary>
        public void Add(IViewSurface surface) { this.children.Add(surface); }

        /// <summary>Removes <paramref name="surface"/>.</summary>
        public bool Remove(IViewSurface surface) => this.children.Remove(surface);
    }

    /// <summary>
    /// An <see cref="IViewSurface"/> that records parenting calls rather than moving elements.
    /// </summary>
    /// <remarks>
    /// Pairs with <see cref="FakeViewLayer"/>. Asserting parenting without a live panel is
    /// the whole reason this exists.
    /// </remarks>
    public sealed class RecordingViewSurface : IViewSurface
    {
        private readonly List<IViewLayer> parentHistory = new();

        /// <summary>Every layer this surface was parented into, in order.</summary>
        public IReadOnlyList<IViewLayer> ParentHistory => this.parentHistory;

        /// <summary>The most recent layer, or null if never parented.</summary>
        public IViewLayer CurrentParent => this.parentHistory.Count > 0 ? this.parentHistory[^1] : null;

        /// <summary>How many times <see cref="SetParent"/> was called.</summary>
        public int ReparentCount => this.parentHistory.Count;

        public void SetParent(IViewLayer layer)
        {
            this.parentHistory.Add(layer);
        }
    }
}
