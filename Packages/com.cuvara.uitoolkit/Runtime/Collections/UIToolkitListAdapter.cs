namespace Cuvara.UIToolkit.Collections
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using Cuvara.UIToolkit.Core;
    using UnityEngine.UIElements;

    /// <summary>
    /// The UI Toolkit counterpart of the host framework's basic list adapter: one model
    /// type, one row template, one presenter type, over a virtualizing <see cref="ListView"/>.
    /// </summary>
    /// <remarks>
    /// <para><b>Not a MonoBehaviour, and it does not subclass the collection.</b> The host
    /// framework's virtualized-list library is an abstract MonoBehaviour you inherit and
    /// drop on a GameObject; <see cref="ListView"/> is a sealed-in-practice control you
    /// configure. So the adapter drives a <see cref="ListView"/> handed to it rather than
    /// being one. That also means it has no <c>Awake</c>, which is where the host
    /// framework's list adapters resolve their container — hence the optional instantiator
    /// constructor parameter.</para>
    ///
    /// <para><b>The host framework's callbacks map onto the ListView ones, but not
    /// one-for-one:</b></para>
    /// <list type="table">
    /// <item><term><c>CreateViewsHolder</c></term><description><c>makeItem</c> — clone the template, build the view, build the presenter. Runs once per <i>element</i>, not per row.</description></item>
    /// <item><term><c>UpdateViewsHolder</c></term><description><c>bindItem</c> — find the presenter already attached to this element and <c>BindData</c> it.</description></item>
    /// <item><term>— nothing —</term><description><c>unbindItem</c>, <c>destroyItem</c>. The host framework's virtualized-list library has no notion of "this element is leaving the viewport" or "this element is going away", which is exactly why its adapters call <c>presenter.Dispose()</c> on every rebind and keep using the disposed presenter. Here <c>Dispose</c> happens once, in <c>destroyItem</c>.</description></item>
    /// </list>
    ///
    /// <para><b>Virtualization: <see cref="CollectionVirtualizationMethod.FixedHeight"/>,
    /// deliberately.</b> Pass <c>fixedItemHeight</c> and this sets both properties.
    /// <c>DynamicHeight</c> measures every row as it is bound and rebinds again after
    /// layout resolves, so a row whose height depends on its content is bound at least
    /// twice per appearance; it is also the path that has historically produced scroll-jump
    /// and blank-viewport reports. A uniform row height is what a list of one template
    /// almost always has, and the fixed path is a pure index-times-height calculation. Pass
    /// <c>0</c> to leave whatever the <see cref="ListView"/> was authored with untouched —
    /// that is the escape hatch for a genuinely variable-height list, and it is the caller
    /// opting in, not the default.</para>
    /// </remarks>
    /// <typeparam name="TModel">The row model.</typeparam>
    /// <typeparam name="TView">The row view. Needs a public <c>(VisualElement)</c> constructor.</typeparam>
    /// <typeparam name="TPresenter">The row presenter, instantiated per element through the presenter instantiator.</typeparam>
    public class UIToolkitListAdapter<TModel, TView, TPresenter> : IDisposable
        where TView : IUIToolkitItemView
        where TPresenter : BaseUIToolkitItemPresenter<TView, TModel>, IDisposable
    {
        private readonly ListView        listView;
        private readonly VisualTreeAsset itemTemplate;

        private readonly Dictionary<VisualElement, TPresenter> elementToPresenter = new();
        private readonly Dictionary<int, TPresenter>           indexToPresenter   = new();

        private IPresenterInstantiator instantiator;
        private List<TModel>           models = new();
        private bool                   disposed;

        /// <summary>The models currently bound. Never null; empty before the first <see cref="SetItems"/>.</summary>
        public IReadOnlyList<TModel> Models => this.models;

        /// <summary>The <see cref="ListView"/> this adapter drives.</summary>
        public ListView ListView => this.listView;

        /// <summary>
        /// How many times <c>makeItem</c> has run — i.e. how many elements were ever
        /// allocated.
        /// </summary>
        /// <remarks>
        /// Public because it is the only externally visible proof that recycling happened:
        /// a virtualizing list over N models leaves this far below N. A test that binds
        /// 500 rows into a 300px viewport and finds this at 500 has found a list that is
        /// not virtualizing, which no other assertion catches.
        /// </remarks>
        public int CreatedElementCount { get; private set; }

        /// <param name="listView">The control to drive. Its callbacks are overwritten.</param>
        /// <param name="itemTemplate">The UXML each row is cloned from.</param>
        /// <param name="fixedItemHeight">Row height in pixels; <c>0</c> leaves the control's own virtualization settings alone. See the type remarks.</param>
        /// <param name="instantiator">Builds presenters. Null falls back to <see cref="ActivatorPresenterInstantiator"/>, the way the host framework's list adapters resolve a container in <c>Awake</c>.</param>
        public UIToolkitListAdapter(ListView listView, VisualTreeAsset itemTemplate, float fixedItemHeight = 0f, IPresenterInstantiator instantiator = null)
        {
            this.listView     = listView ?? throw new ArgumentNullException(nameof(listView));
            this.itemTemplate = itemTemplate ?? throw new ArgumentNullException(nameof(itemTemplate));
            this.instantiator = instantiator;

            if (fixedItemHeight > 0f)
            {
                this.listView.virtualizationMethod = CollectionVirtualizationMethod.FixedHeight;
                this.listView.fixedItemHeight      = fixedItemHeight;
            }

            this.listView.makeItem    = this.MakeItem;
            this.listView.bindItem    = this.BindItem;
            this.listView.unbindItem  = this.UnbindItem;
            this.listView.destroyItem = this.DestroyItem;
            this.listView.itemsSource = this.models;
        }

        private IPresenterInstantiator Instantiator => this.instantiator ??= ActivatorPresenterInstantiator.Instance;

        #region ListView callbacks

        private VisualElement MakeItem()
        {
            // CloneTree() returns a TemplateContainer wrapping the authored root. It is
            // what goes into the ListView; the view is built around the same element so
            // that view.Root and the recycled element are the same object, which is what
            // makes the element->presenter lookup in BindItem work.
            var element = this.itemTemplate.CloneTree();

            // A TemplateContainer defaults to flex-grow 0, so without this every row
            // collapses to the height of its content instead of filling the row slot.
            element.style.flexGrow = 1;

            var view      = (TView)UIToolkitItemViewFactory.Create(typeof(TView), element);
            var presenter = (TPresenter)this.Instantiator.Instantiate(typeof(TPresenter));

            presenter.SetView(view);
            presenter.OnViewReady();

            this.elementToPresenter[element] = presenter;
            ++this.CreatedElementCount;

            return element;
        }

        private void BindItem(VisualElement element, int index)
        {
            if (index < 0 || index >= this.models.Count) return;

            if (!this.elementToPresenter.TryGetValue(element, out var presenter))
            {
                // Only reachable if something else reassigned makeItem after construction.
                throw new InvalidOperationException($"{nameof(UIToolkitListAdapter<TModel, TView, TPresenter>)} was asked to bind an element it did not create.");
            }

            this.indexToPresenter[index] = presenter;
            presenter.BindData(this.models[index]);
        }

        private void UnbindItem(VisualElement element, int index)
        {
            if (this.indexToPresenter.TryGetValue(index, out var presenter)
                && this.elementToPresenter.TryGetValue(element, out var owner)
                && ReferenceEquals(presenter, owner))
            {
                this.indexToPresenter.Remove(index);
            }
        }

        private void DestroyItem(VisualElement element)
        {
            if (!this.elementToPresenter.Remove(element, out var presenter)) return;

            foreach (var index in this.indexToPresenter.Where(pair => ReferenceEquals(pair.Value, presenter)).Select(pair => pair.Key).ToList())
            {
                this.indexToPresenter.Remove(index);
            }

            presenter.Dispose();
        }

        #endregion

        #region Data

        /// <summary>Replaces the bound collection and rebuilds the list.</summary>
        /// <remarks>
        /// A null <paramref name="newModels"/> is an empty list, not a throw — the host
        /// framework's counterpart would <c>NullReferenceException</c> inside
        /// <c>ResetItems</c>, and an empty server response is not a programming error.
        /// </remarks>
        public void SetItems(IEnumerable<TModel> newModels)
        {
            this.ThrowIfDisposed();

            this.models = newModels?.ToList() ?? new List<TModel>();
            this.indexToPresenter.Clear();

            this.listView.itemsSource = this.models;

            // Rebuild rather than RefreshItems: the source object itself changed, and
            // RefreshItems would re-bind the old element/index pairing against a list of a
            // different length.
            this.listView.Rebuild();
        }

        /// <summary>Re-binds every currently visible row against the models already set.</summary>
        /// <remarks>The counterpart of the host framework's full-visible-items refresh, minus the re-entrancy trap it exists to work around.</remarks>
        public void RefreshItems()
        {
            this.ThrowIfDisposed();
            this.listView.RefreshItems();
        }

        /// <summary>The presenter currently bound to <paramref name="index"/>, or null if that row is not realized.</summary>
        /// <remarks>
        /// Null rather than a <c>KeyNotFoundException</c>, which is what the host
        /// framework's version throws: with virtualization, "that row is off screen" is
        /// the normal state of most of a list, not an error.
        /// </remarks>
        public TPresenter GetPresenterAtIndex(int index)
        {
            return this.indexToPresenter.TryGetValue(index, out var presenter) ? presenter : null;
        }

        /// <summary>Every realized presenter, ordered by row index.</summary>
        public List<TPresenter> GetPresenters()
        {
            return this.indexToPresenter.OrderBy(pair => pair.Key).Select(pair => pair.Value).ToList();
        }

        #endregion

        public void Dispose()
        {
            if (this.disposed) return;
            this.disposed = true;

            foreach (var presenter in this.elementToPresenter.Values) presenter.Dispose();

            this.elementToPresenter.Clear();
            this.indexToPresenter.Clear();
            this.models.Clear();

            this.listView.makeItem    = null;
            this.listView.bindItem    = null;
            this.listView.unbindItem  = null;
            this.listView.destroyItem = null;
            this.listView.itemsSource = null;
        }

        private void ThrowIfDisposed()
        {
            if (this.disposed) throw new ObjectDisposedException(nameof(UIToolkitListAdapter<TModel, TView, TPresenter>));
        }
    }
}
