namespace Cuvara.UIToolkit.Collections
{
    using System;
    using UnityEngine.UIElements;

    /// <summary>
    /// The UI Toolkit analogue of the host framework's view marker interface, for a row inside a collection.
    /// </summary>
    /// <remarks>
    /// That marker interface is itself an empty one, but the host framework's uGUI adapters
    /// lean on <c>TView : MonoBehaviour</c> for everything they actually do with a row —
    /// read its GameObject, toggle it active, find it with <c>GetComponentInChildren</c>.
    /// None of that exists here, so the marker has to carry the one thing that replaces it:
    /// the element the row is drawn from.
    /// </remarks>
    public interface IUIToolkitItemView
    {
        /// <summary>The root element of this row. Never null.</summary>
        VisualElement Root { get; }
    }

    /// <summary>
    /// A row view built around an already-cloned <see cref="VisualElement"/>.
    /// </summary>
    /// <remarks>
    /// Deliberately the same shape as <see cref="View.BaseUIToolkitView"/> — a plain C#
    /// class, constructed synchronously, querying its own children in the constructor —
    /// but without the screen-flow members (<c>Open</c>, <c>Close</c>, <c>IViewSurface</c>,
    /// the three lifecycle events). A row is not a screen: it is never opened, never
    /// reparented between layers, and never destroyed on its own; the collection owns it.
    ///
    /// <para>Rows take a <see cref="VisualElement"/> rather than a
    /// <see cref="VisualTreeAsset"/>, which is the one signature difference from
    /// <c>BaseUIToolkitView</c> and it is load-bearing: <c>ListView.makeItem</c> hands back
    /// elements that are cloned once and then bound to many different models, so the clone
    /// has to happen in the adapter — which owns the recycling — not in the view.</para>
    /// </remarks>
    public abstract class BaseUIToolkitItemView : IUIToolkitItemView
    {
        public VisualElement Root { get; }

        protected BaseUIToolkitItemView(VisualElement root)
        {
            this.Root = root ?? throw new ArgumentNullException(nameof(root));
        }
    }
}
