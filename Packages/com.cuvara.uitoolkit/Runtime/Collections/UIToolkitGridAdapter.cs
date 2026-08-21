namespace Cuvara.UIToolkit.Collections
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using Cuvara.UIToolkit.Core;
    using UnityEngine.UIElements;

    /// <summary>
    /// The UI Toolkit counterpart of the host framework's basic grid adapter: a fixed
    /// number of cells per row, over a virtualizing <see cref="ListView"/> whose items are
    /// the ROWS.
    /// </summary>
    /// <remarks>
    /// <para><b>Why a ListView and not a grid control.</b> UI Toolkit 6000.3.9f1 ships no
    /// virtualizing grid. <see cref="MultiColumnListView"/> is not one — its columns are
    /// fields of a single item (name, size, date), sized and reordered by a header, not
    /// cells of a flow. A <see cref="ScrollView"/> with <c>flex-wrap: wrap</c> is a real
    /// grid but has no virtualization at all: 500 cells means 500 live elements.</para>
    ///
    /// <para>So the grid is a list of rows, which is also exactly what the host framework's
    /// own grid adapter is internally — it wraps a per-row cell group around a plain list.
    /// This is a port of that structure, not a workaround for the absence of one:
    /// <c>itemsSource</c> is the row count, one element is created per visible ROW, and
    /// each element holds <see cref="CellsPerRow"/> cell views built once and rebound as
    /// the row is recycled.</para>
    ///
    /// <para><b>The trailing partial row</b> hides its unused cells with
    /// <c>display: none</c> rather than leaving them bound to stale models — the same job
    /// the host framework's grid adapter does by shrinking the last cell group.</para>
    ///
    /// <para>Virtualization is <see cref="CollectionVirtualizationMethod.FixedHeight"/>
    /// for the same reason as <see cref="UIToolkitListAdapter{TModel,TView,TPresenter}"/>,
    /// and with more force: every row in a grid is the same height by construction, so the
    /// dynamic path would be measuring rows it already knows the size of.</para>
    /// </remarks>
    public class UIToolkitGridAdapter<TModel, TView, TPresenter> : IDisposable
        where TView : IUIToolkitItemView
        where TPresenter : BaseUIToolkitItemPresenter<TView, TModel>, IDisposable
    {
        /// <summary>USS class put on the row element, so a project can style the row flow.</summary>
        public const string RowUssClassName = "gdk-grid-row";

        /// <summary>USS class put on each cell wrapper.</summary>
        public const string CellUssClassName = "gdk-grid-cell";

        private readonly ListView        listView;
        private readonly VisualTreeAsset cellTemplate;

        private readonly Dictionary<VisualElement, List<TPresenter>> rowToPresenters = new();
        private readonly Dictionary<int, TPresenter>                 indexToPresenter = new();

        private IPresenterInstantiator instantiator;
        private List<TModel>           models   = new();
        private List<int>              rowIndex = new();
        private bool                   disposed;

        public IReadOnlyList<TModel> Models => this.models;

        public ListView ListView => this.listView;

        /// <summary>Cells per row. Fixed for the life of the adapter, like the host framework's grid parameters' column count.</summary>
        public int CellsPerRow { get; }

        /// <summary>How many row elements were ever allocated. Stays far below the row count on a virtualizing grid.</summary>
        public int CreatedRowCount { get; private set; }

        /// <summary>How many cell views were ever built. Equals <see cref="CreatedRowCount"/> times <see cref="CellsPerRow"/>.</summary>
        public int CreatedCellCount { get; private set; }

        public UIToolkitGridAdapter(ListView listView, VisualTreeAsset cellTemplate, int cellsPerRow, float fixedRowHeight = 0f, IPresenterInstantiator instantiator = null)
        {
            this.listView     = listView ?? throw new ArgumentNullException(nameof(listView));
            this.cellTemplate = cellTemplate ?? throw new ArgumentNullException(nameof(cellTemplate));
            this.instantiator = instantiator;

            if (cellsPerRow <= 0) throw new ArgumentOutOfRangeException(nameof(cellsPerRow), cellsPerRow, "A grid needs at least one cell per row.");
            this.CellsPerRow = cellsPerRow;

            if (fixedRowHeight > 0f)
            {
                this.listView.virtualizationMethod = CollectionVirtualizationMethod.FixedHeight;
                this.listView.fixedItemHeight      = fixedRowHeight;
            }

            this.listView.makeItem    = this.MakeRow;
            this.listView.bindItem    = this.BindRow;
            this.listView.unbindItem  = this.UnbindRow;
            this.listView.destroyItem = this.DestroyRow;
            this.listView.itemsSource = this.rowIndex;
        }

        private IPresenterInstantiator Instantiator => this.instantiator ??= ActivatorPresenterInstantiator.Instance;

        #region ListView callbacks

        private VisualElement MakeRow()
        {
            var row = new VisualElement { name = RowUssClassName };
            row.AddToClassList(RowUssClassName);
            row.style.flexDirection = FlexDirection.Row;
            row.style.flexGrow      = 1;

            var presenters = new List<TPresenter>(this.CellsPerRow);

            for (var cell = 0; cell < this.CellsPerRow; ++cell)
            {
                var element = this.cellTemplate.CloneTree();
                element.AddToClassList(CellUssClassName);

                // Equal share of the row width — the grid part of "grid".
                element.style.flexGrow  = 1;
                element.style.flexBasis = 0;

                var view      = (TView)UIToolkitItemViewFactory.Create(typeof(TView), element);
                var presenter = (TPresenter)this.Instantiator.Instantiate(typeof(TPresenter));

                presenter.SetView(view);
                presenter.OnViewReady();

                row.Add(element);
                presenters.Add(presenter);
                ++this.CreatedCellCount;
            }

            this.rowToPresenters[row] = presenters;
            ++this.CreatedRowCount;

            return row;
        }

        private void BindRow(VisualElement row, int rowNumber)
        {
            if (!this.rowToPresenters.TryGetValue(row, out var presenters))
            {
                throw new InvalidOperationException($"{nameof(UIToolkitGridAdapter<TModel, TView, TPresenter>)} was asked to bind a row it did not create.");
            }

            for (var cell = 0; cell < this.CellsPerRow; ++cell)
            {
                var presenter = presenters[cell];
                var index     = rowNumber * this.CellsPerRow + cell;

                if (index >= this.models.Count)
                {
                    // Trailing partial row: nothing to show here, and leaving the previous
                    // model on screen is the bug this branch exists to prevent.
                    presenter.SetActiveView(false);
                    continue;
                }

                presenter.SetActiveView(true);
                this.indexToPresenter[index] = presenter;
                presenter.BindData(this.models[index]);
            }
        }

        private void UnbindRow(VisualElement row, int rowNumber)
        {
            for (var cell = 0; cell < this.CellsPerRow; ++cell)
            {
                this.indexToPresenter.Remove(rowNumber * this.CellsPerRow + cell);
            }
        }

        private void DestroyRow(VisualElement row)
        {
            if (!this.rowToPresenters.Remove(row, out var presenters)) return;

            foreach (var index in this.indexToPresenter.Where(pair => presenters.Contains(pair.Value)).Select(pair => pair.Key).ToList())
            {
                this.indexToPresenter.Remove(index);
            }

            foreach (var presenter in presenters) presenter.Dispose();
        }

        #endregion

        #region Data

        /// <summary>Replaces the bound collection and rebuilds the grid. Null is an empty grid, not a throw.</summary>
        public void SetItems(IEnumerable<TModel> newModels)
        {
            this.ThrowIfDisposed();

            this.models = newModels?.ToList() ?? new List<TModel>();
            this.indexToPresenter.Clear();

            var rowCount = (this.models.Count + this.CellsPerRow - 1) / this.CellsPerRow;

            this.rowIndex = Enumerable.Range(0, rowCount).ToList();
            this.listView.itemsSource = this.rowIndex;
            this.listView.Rebuild();
        }

        /// <summary>The number of rows the current models occupy.</summary>
        public int RowCount => this.rowIndex.Count;

        public void RefreshItems()
        {
            this.ThrowIfDisposed();
            this.listView.RefreshItems();
        }

        /// <summary>The presenter bound to model <paramref name="index"/>, or null if its row is not realized.</summary>
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

            foreach (var presenter in this.rowToPresenters.Values.SelectMany(presenters => presenters)) presenter.Dispose();

            this.rowToPresenters.Clear();
            this.indexToPresenter.Clear();
            this.models.Clear();
            this.rowIndex.Clear();

            this.listView.makeItem    = null;
            this.listView.bindItem    = null;
            this.listView.unbindItem  = null;
            this.listView.destroyItem = null;
            this.listView.itemsSource = null;
        }

        private void ThrowIfDisposed()
        {
            if (this.disposed) throw new ObjectDisposedException(nameof(UIToolkitGridAdapter<TModel, TView, TPresenter>));
        }
    }
}
