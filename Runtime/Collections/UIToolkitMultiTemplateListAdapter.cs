namespace Cuvara.UIToolkit.Collections
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using Cuvara.UIToolkit.Core;
    using UnityEngine.UIElements;

    /// <summary>
    /// The model a <see cref="UIToolkitMultiTemplateListAdapter{TModel,TView,TPresenter}"/>
    /// row carries. The counterpart of the host framework's multi-prefab row model, one member wider.
    /// </summary>
    /// <remarks>
    /// <c>PrefabName</c> becomes <see cref="TemplateName"/> — a key into the adapter's
    /// template dictionary rather than a prefab name — and <see cref="PresenterType"/>
    /// carries over unchanged. <see cref="ViewType"/> is the new one, and it is new because
    /// the host framework's uGUI adapter did not need it: it recovered the view with
    /// <c>GetComponentInChildren&lt;TView&gt;</c> off an instantiated prefab, so the view
    /// type was already baked into the prefab. Nothing instantiates a
    /// <see cref="VisualElement"/> subclass for us here — the adapter constructs the view,
    /// so it has to be told which one.
    /// </remarks>
    public abstract class MultiTemplateModel
    {
        /// <summary>Key into the adapter's template dictionary.</summary>
        public abstract string TemplateName { get; }

        /// <summary>Concrete presenter type, resolved through the presenter instantiator.</summary>
        public abstract Type PresenterType { get; }

        /// <summary>Concrete view type. Must be assignable to the adapter's <c>TView</c>.</summary>
        public abstract Type ViewType { get; }
    }

    /// <summary>
    /// The UI Toolkit counterpart of the host framework's multi-prefab list adapter:
    /// heterogeneous rows, each with its own UXML template, view type and presenter type,
    /// over one virtualizing <see cref="ListView"/>.
    /// </summary>
    /// <remarks>
    /// <para><b>The awkward case, and the shape chosen for it.</b> A
    /// <see cref="ListView"/> has exactly ONE <c>makeItem</c>, so the three obvious
    /// readings of "heterogeneous rows" all have to be weighed:</para>
    ///
    /// <list type="number">
    /// <item><description><b>A superset element with toggled <c>display</c></b> — clone
    /// every template into every row and show one. Rejected: the cost is
    /// <i>templates × pooled elements</i>, so adding a fourth row type makes every
    /// already-existing row 33% heavier whether or not it is ever that type; the hidden
    /// subtrees still take part in style resolution; and every presenter for every type is
    /// constructed for every element, so a chat list of 30 elements builds 120 presenters
    /// to use 30. It is only defensible for two or three cheap, similar templates.</description></item>
    /// <item><description><b><see cref="TreeView"/></b> — rejected outright, and it is the
    /// suggestion worth being most careful about because it sounds right and is not.
    /// <c>TreeView</c> models <i>hierarchy</i>: expand/collapse state, indentation, an id
    /// per item, <c>TreeViewItemData</c> wrappers. It does not solve heterogeneity at all —
    /// it has the same single <c>makeItem</c> — so it buys a flat list nothing and costs it
    /// a data model it has no use for.</description></item>
    /// <item><description><b>An empty shell plus per-template pools</b> — chosen.
    /// <c>makeItem</c> returns a bare container; <c>bindItem</c> looks at the model's
    /// template, and if the shell is already holding a slot of that template it simply
    /// rebinds, otherwise it returns the current slot to its template's pool and takes one
    /// from the pool the model wants. Cost is bounded by what is actually on screen, and
    /// a scroll through a run of same-template rows never touches the pools at all.</description></item>
    /// </list>
    ///
    /// <para>That third shape is not a novel invention — it is precisely what the host
    /// framework's multi-prefab list adapter's recyclability check expresses (recycle this
    /// views holder only for an item of the same presenter type). The host framework's
    /// virtualized-list library can push the decision into the library because its recycler
    /// asks; <see cref="ListView"/>'s does not ask, so the pool moves into the adapter. Same
    /// policy, different owner.</para>
    ///
    /// <para><b>Virtualization: <see cref="CollectionVirtualizationMethod.DynamicHeight"/>,
    /// and this is the deliberate exception</b> to the fixed-height rule the other two
    /// adapters follow. Rows of different templates have different heights — that is what
    /// makes them different templates — and <c>FixedHeight</c> can express exactly one
    /// height for the whole list. The host framework's uGUI original says the same thing in
    /// its own terms: it requests a per-item size change and layout update out of its item
    /// init step. The dynamic path is slower and has the worse history; it is used here
    /// because the alternative is wrong, not because it is good. A caller whose templates
    /// genuinely share a height can pass <c>fixedItemHeight</c> and get the fast path
    /// back.</para>
    /// </remarks>
    public class UIToolkitMultiTemplateListAdapter<TModel, TView, TPresenter> : IDisposable
        where TModel : MultiTemplateModel
        where TView : IUIToolkitItemView
        where TPresenter : BaseUIToolkitItemPresenter<TView, TModel>, IDisposable
    {
        /// <summary>USS class on the shell element each row is swapped inside.</summary>
        public const string ShellUssClassName = "gdk-multi-template-shell";

        private sealed class Slot
        {
            public string        TemplateName;
            public VisualElement Root;
            public TPresenter    Presenter;
        }

        private readonly ListView                                 listView;
        private readonly Dictionary<string, VisualTreeAsset>      templates;
        private readonly Dictionary<string, Stack<Slot>>          pools            = new();
        private readonly Dictionary<VisualElement, Slot>          shellToSlot      = new();
        private readonly Dictionary<int, TPresenter>              indexToPresenter = new();
        private readonly List<Slot>                               allSlots         = new();

        private IPresenterInstantiator instantiator;
        private List<TModel>           models = new();
        private bool                   disposed;

        public IReadOnlyList<TModel> Models => this.models;

        public ListView ListView => this.listView;

        /// <summary>How many shell elements <c>makeItem</c> ever produced.</summary>
        public int CreatedShellCount { get; private set; }

        /// <summary>How many template clones were ever built. The number the pooling exists to hold down.</summary>
        public int CreatedSlotCount { get; private set; }

        /// <summary>How many times a bind took a slot out of a pool instead of cloning one.</summary>
        public int PooledSlotReuseCount { get; private set; }

        /// <param name="listView">The control to drive.</param>
        /// <param name="templates">Template per <see cref="MultiTemplateModel.TemplateName"/>. Copied; later edits to the caller's dictionary are not seen.</param>
        /// <param name="fixedItemHeight">Non-zero switches this list to the fixed-height fast path. Only correct when every template really is that tall. See the type remarks.</param>
        /// <param name="instantiator">Builds presenters; null falls back to <see cref="ActivatorPresenterInstantiator"/>.</param>
        public UIToolkitMultiTemplateListAdapter(ListView listView, IReadOnlyDictionary<string, VisualTreeAsset> templates, float fixedItemHeight = 0f, IPresenterInstantiator instantiator = null)
        {
            this.listView     = listView ?? throw new ArgumentNullException(nameof(listView));
            this.instantiator = instantiator;

            if (templates == null) throw new ArgumentNullException(nameof(templates));
            if (templates.Count == 0) throw new ArgumentException("A multi-template list needs at least one template.", nameof(templates));

            this.templates = templates.ToDictionary(pair => pair.Key, pair => pair.Value ?? throw new ArgumentException($"Template '{pair.Key}' is null.", nameof(templates)));

            if (fixedItemHeight > 0f)
            {
                this.listView.virtualizationMethod = CollectionVirtualizationMethod.FixedHeight;
                this.listView.fixedItemHeight      = fixedItemHeight;
            }
            else
            {
                this.listView.virtualizationMethod = CollectionVirtualizationMethod.DynamicHeight;
            }

            this.listView.makeItem    = this.MakeShell;
            this.listView.bindItem    = this.BindShell;
            this.listView.unbindItem  = this.UnbindShell;
            this.listView.destroyItem = this.DestroyShell;
            this.listView.itemsSource = this.models;
        }

        private IPresenterInstantiator Instantiator => this.instantiator ??= ActivatorPresenterInstantiator.Instance;

        #region ListView callbacks

        private VisualElement MakeShell()
        {
            var shell = new VisualElement { name = ShellUssClassName };
            shell.AddToClassList(ShellUssClassName);
            shell.style.flexGrow = 1;

            ++this.CreatedShellCount;
            return shell;
        }

        private void BindShell(VisualElement shell, int index)
        {
            if (index < 0 || index >= this.models.Count) return;

            var model = this.models[index];

            if (model == null) throw new InvalidOperationException($"Row {index} of a multi-template list is null; a row model must at least say which template it wants.");

            this.shellToSlot.TryGetValue(shell, out var current);

            if (current == null || current.TemplateName != model.TemplateName)
            {
                if (current != null)
                {
                    current.Root.RemoveFromHierarchy();
                    this.Pool(current.TemplateName).Push(current);
                }

                var slot = this.Rent(model);

                shell.Add(slot.Root);
                this.shellToSlot[shell] = slot;
                current                 = slot;
            }

            this.indexToPresenter[index] = current.Presenter;
            current.Presenter.BindData(model);
        }

        private void UnbindShell(VisualElement shell, int index)
        {
            this.indexToPresenter.Remove(index);
        }

        private void DestroyShell(VisualElement shell)
        {
            // The slot the shell is holding goes back to its pool rather than being
            // disposed: ListView destroys elements when the source is rebuilt, and the
            // views are still perfectly good for the next build. Everything is disposed
            // for real in Dispose().
            if (!this.shellToSlot.Remove(shell, out var slot)) return;

            slot.Root.RemoveFromHierarchy();
            this.Pool(slot.TemplateName).Push(slot);

            foreach (var index in this.indexToPresenter.Where(pair => ReferenceEquals(pair.Value, slot.Presenter)).Select(pair => pair.Key).ToList())
            {
                this.indexToPresenter.Remove(index);
            }
        }

        #endregion

        #region Slots

        private Stack<Slot> Pool(string templateName)
        {
            if (!this.pools.TryGetValue(templateName, out var pool)) this.pools[templateName] = pool = new Stack<Slot>();
            return pool;
        }

        private Slot Rent(TModel model)
        {
            var pool = this.Pool(model.TemplateName);

            if (pool.Count > 0)
            {
                ++this.PooledSlotReuseCount;
                return pool.Pop();
            }

            if (!this.templates.TryGetValue(model.TemplateName, out var template))
            {
                throw new KeyNotFoundException(
                    $"No template registered under '{model.TemplateName}', wanted by {model.GetType().Name}. Registered: {string.Join(", ", this.templates.Keys.OrderBy(key => key))}.");
            }

            if (model.ViewType == null || model.PresenterType == null)
            {
                throw new InvalidOperationException($"{model.GetType().Name} must name both a ViewType and a PresenterType; a multi-template row is built from them.");
            }

            if (!typeof(TView).IsAssignableFrom(model.ViewType))
            {
                throw new InvalidOperationException($"{model.GetType().Name}.ViewType is {model.ViewType.Name}, which is not a {typeof(TView).Name}.");
            }

            var element = template.CloneTree();
            element.style.flexGrow = 1;

            var view = (TView)UIToolkitItemViewFactory.Create(model.ViewType, element);

            if (this.Instantiator.Instantiate(model.PresenterType) is not TPresenter presenter)
            {
                throw new InvalidOperationException($"{model.GetType().Name}.PresenterType is {model.PresenterType.Name}, which is not a {typeof(TPresenter).Name}.");
            }

            presenter.SetView(view);
            presenter.OnViewReady();

            var slot = new Slot { TemplateName = model.TemplateName, Root = element, Presenter = presenter };

            this.allSlots.Add(slot);
            ++this.CreatedSlotCount;

            return slot;
        }

        #endregion

        #region Data

        /// <summary>Replaces the bound collection and rebuilds the list. Null is an empty list, not a throw.</summary>
        public void SetItems(IEnumerable<TModel> newModels)
        {
            this.ThrowIfDisposed();

            this.models = newModels?.ToList() ?? new List<TModel>();
            this.indexToPresenter.Clear();

            this.listView.itemsSource = this.models;
            this.listView.Rebuild();
        }

        public void RefreshItems()
        {
            this.ThrowIfDisposed();
            this.listView.RefreshItems();
        }

        /// <summary>The presenter bound to <paramref name="index"/>, or null if that row is not realized.</summary>
        public TPresenter GetPresenterAtIndex(int index)
        {
            return this.indexToPresenter.TryGetValue(index, out var presenter) ? presenter : null;
        }

        public List<TPresenter> GetPresenters()
        {
            return this.indexToPresenter.OrderBy(pair => pair.Key).Select(pair => pair.Value).ToList();
        }

        #endregion

        public void Dispose()
        {
            if (this.disposed) return;
            this.disposed = true;

            foreach (var slot in this.allSlots) slot.Presenter.Dispose();

            this.allSlots.Clear();
            this.pools.Clear();
            this.shellToSlot.Clear();
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
            if (this.disposed) throw new ObjectDisposedException(nameof(UIToolkitMultiTemplateListAdapter<TModel, TView, TPresenter>));
        }
    }
}
