namespace Cuvara.UIToolkit.Tests.Collections
{
    using System;
    using System.Collections;
    using System.Collections.Generic;
    using System.Linq;
    using Cysharp.Threading.Tasks;
    using Cuvara.UIToolkit.Collections;
    using NUnit.Framework;
    using UnityEngine;
    using UnityEngine.TestTools;
    using UnityEngine.UIElements;
    using Object = UnityEngine.Object;

    /// <summary>
    /// Exercises the UI Toolkit collection adapters — the counterparts of the three
    /// virtualized-list adapters in the host framework this package was extracted from,
    /// which stay exactly where they are.
    /// </summary>
    /// <remarks>
    /// PlayMode, and for a sharper reason than the screen-flow tests: virtualization is
    /// not a data structure, it is a consequence of layout. <c>ListView</c> creates
    /// elements when its viewport resolves a size and its scroller reports a range, so a
    /// test that never lays out never proves anything about recycling — it would assert
    /// over an empty list and pass. Every test here that claims something about
    /// virtualization builds a real <c>UIDocument</c>, gives it a viewport with a real
    /// height, and yields frames until the panel has laid out; each of those also asserts
    /// that something WAS realized, so "nothing happened" can never read as a pass.
    ///
    /// <para>The pure ones — the factory, the presenter base, argument validation — are
    /// plain <c>[Test]</c>s with no panel at all, which is the whole reason those seams
    /// were split out.</para>
    /// </remarks>
    public class UIToolkitCollectionsTests
    {
        private const string ItemUxmlPath = "Packages/com.cuvara.uitoolkit/Tests/Runtime/Collections/TestListItem.uxml";
        private const string WideUxmlPath = "Packages/com.cuvara.uitoolkit/Tests/Runtime/Collections/TestWideItem.uxml";

        private const float ItemHeight     = 30f;

        /// <summary>Row count for the alternating-template scroll test. See that test for why it is not larger.</summary>
        private const int MultiTemplateRowCount = 80;

        private const float ViewportHeight = 300f;

        private GameObject                     documentObject;
        private PanelSettings                  panelSettings;
        private CountingPresenterInstantiator  instantiator;

        [SetUp]
        public void SetUp()
        {
            // A runtime panel with no theme style sheet logs about it; that is a rendering
            // concern and not what these tests are about.
            LogAssert.ignoreFailingMessages = true;
            this.instantiator                = new CountingPresenterInstantiator();
        }

        [TearDown]
        public void TearDown()
        {
            if (this.documentObject != null) Object.DestroyImmediate(this.documentObject);
            if (this.panelSettings != null) Object.DestroyImmediate(this.panelSettings);

            this.documentObject             = null;
            this.panelSettings              = null;
            LogAssert.ignoreFailingMessages = false;
        }

        #region The list adapter, on a real panel

        [UnityTest]
        public IEnumerator List_BindsTheVisibleRowsAndRecyclesElementsInsteadOfAllocatingPerRow() => UniTask.ToCoroutine(async () =>
        {
            var listView = this.BuildListView();
            using var adapter = new UIToolkitListAdapter<RowModel, RowView, RowPresenter>(listView, LoadUxml(ItemUxmlPath), ItemHeight, this.instantiator);

            adapter.SetItems(RowModel.Range(500));
            await this.Settle();

            var realized = adapter.GetPresenters();

            Assert.That(realized, Is.Not.Empty, "No row was ever bound; the ListView never laid out, so nothing here is being measured.");

            // The claim that matters. A ListView that is not virtualizing builds one
            // element per model, and nothing else in the API tells you that happened.
            Assert.That(adapter.CreatedElementCount, Is.LessThan(100),
                $"{adapter.CreatedElementCount} elements for 500 rows in a {ViewportHeight}px viewport — this list is not virtualizing.");

            Assert.That(adapter.CreatedElementCount, Is.GreaterThanOrEqualTo(realized.Count));

            // Bound, not merely created: the row shows its own model's text.
            var first = adapter.GetPresenterAtIndex(0);
            Assert.That(first, Is.Not.Null, "Row 0 is on screen but has no presenter.");
            Assert.That(first.View.Label.text, Is.EqualTo("row 0"));
            Assert.That(first.BindCount, Is.GreaterThanOrEqualTo(1));
        });

        [UnityTest]
        public IEnumerator List_ScrollingFarDownRebindsTheSameElementsRatherThanBuildingMore() => UniTask.ToCoroutine(async () =>
        {
            var listView = this.BuildListView();

            // Let the panel resolve the viewport BEFORE the source is set: the
            // virtualization controller sizes the scrollable content when it first runs,
            // and running it against a not-yet-laid-out ListView leaves the ScrollView
            // believing the content is only as tall as the handful of realized rows — at
            // which point there is nothing to scroll and the test proves nothing.
            await this.Settle();

            using var adapter = new UIToolkitListAdapter<RowModel, RowView, RowPresenter>(listView, LoadUxml(ItemUxmlPath), ItemHeight, this.instantiator);

            adapter.SetItems(RowModel.Range(500));
            await this.Settle();

            Assert.That(adapter.GetPresenters(), Is.Not.Empty, "The list never laid out.");

            var elementsAfterFirstFill = adapter.CreatedElementCount;
            var presentersBuilt        = this.instantiator.InstantiateCount;
            var boundBefore            = adapter.GetPresenters().Where(presenter => presenter.LastModel != null).Select(presenter => presenter.LastModel.Index).OrderBy(index => index).ToList();

            ScrollDown(listView, 400, 400 * ItemHeight);
            await this.Settle();

            Assert.That(adapter.CreatedElementCount, Is.EqualTo(elementsAfterFirstFill),
                "Scrolling allocated new elements; the pool is not being reused.");

            Assert.That(this.instantiator.InstantiateCount, Is.EqualTo(presentersBuilt),
                "Scrolling built new presenters. A presenter belongs to an element, not to a row.");

            var far = adapter.GetPresenters();
            Assert.That(far, Is.Not.Empty, "Nothing is bound after scrolling.");

            var boundAfter = far.Where(presenter => presenter.LastModel != null).Select(presenter => presenter.LastModel.Index).OrderBy(index => index).ToList();

            // The window MOVED — i.e. the same elements now carry different models, which
            // is the observable difference between recycling and not scrolling at all.
            //
            // Movement rather than a specific destination, and that is a limit worth
            // stating: in this headless panel the ScrollView clamps its scrollable range
            // to the realized rows rather than to itemsSource.Count * fixedItemHeight, so
            // a request to jump to row 400 lands a few rows down instead. That is a
            // property of the test panel, not of the adapter, and it is why this asserts
            // that the bound window advanced rather than that it reached row 400.
            Assert.That(boundAfter.Min(), Is.GreaterThan(boundBefore.Min()),
                $"The bound window did not advance. Before: [{string.Join(",", boundBefore)}]; after: [{string.Join(",", boundAfter)}]; "
                + $"scrollOffset={listView.Q<ScrollView>()?.scrollOffset.y}.");

            Assert.That(far.Any(presenter => presenter.BindCount > 1),
                "No element was rebound, so the scroll did not recycle anything.");
        });

        [UnityTest]
        public IEnumerator List_AnEmptySourceRealizesNothingAndDoesNotThrow() => UniTask.ToCoroutine(async () =>
        {
            var listView = this.BuildListView();
            using var adapter = new UIToolkitListAdapter<RowModel, RowView, RowPresenter>(listView, LoadUxml(ItemUxmlPath), ItemHeight, this.instantiator);

            Assert.DoesNotThrow(() => adapter.SetItems(new List<RowModel>()));
            await this.Settle();

            Assert.That(adapter.Models, Is.Empty);
            Assert.That(adapter.GetPresenters(), Is.Empty);
            Assert.That(adapter.GetPresenterAtIndex(0), Is.Null, "An unrealized row must read as null, not throw.");

            // And null, which the host framework's counterpart NullReferenceExceptions on.
            Assert.DoesNotThrow(() => adapter.SetItems(null));
            await this.Settle();

            Assert.That(adapter.Models, Is.Empty);

            // Non-empty after empty still works — i.e. the empty pass did not wedge it.
            adapter.SetItems(RowModel.Range(20));
            await this.Settle();

            Assert.That(adapter.GetPresenters(), Is.Not.Empty, "The list never recovered from being bound to an empty source.");
        });

        [UnityTest]
        public IEnumerator List_ShrinkingTheSourceDropsTheRowsThatNoLongerExist() => UniTask.ToCoroutine(async () =>
        {
            var listView = this.BuildListView();
            using var adapter = new UIToolkitListAdapter<RowModel, RowView, RowPresenter>(listView, LoadUxml(ItemUxmlPath), ItemHeight, this.instantiator);

            adapter.SetItems(RowModel.Range(50));
            await this.Settle();
            Assert.That(adapter.GetPresenters(), Is.Not.Empty);

            adapter.SetItems(RowModel.Range(2));
            await this.Settle();

            Assert.That(adapter.Models.Count, Is.EqualTo(2));
            Assert.That(adapter.GetPresenters().Count, Is.LessThanOrEqualTo(2),
                "Rows past the end of a shrunken source are still bound.");
            Assert.That(adapter.GetPresenterAtIndex(40), Is.Null);
        });

        #endregion

        #region The grid adapter

        [UnityTest]
        public IEnumerator Grid_PacksCellsPerRowAndHidesTheTrailingRemainder() => UniTask.ToCoroutine(async () =>
        {
            var listView = this.BuildListView();
            using var adapter = new UIToolkitGridAdapter<RowModel, RowView, RowPresenter>(listView, LoadUxml(ItemUxmlPath), 3, ItemHeight, this.instantiator);

            adapter.SetItems(RowModel.Range(7));
            await this.Settle();

            Assert.That(adapter.RowCount, Is.EqualTo(3), "7 models at 3 per row is 3 rows.");
            Assert.That(adapter.GetPresenters(), Is.Not.Empty, "The grid never laid out.");
            Assert.That(adapter.CreatedCellCount, Is.EqualTo(adapter.CreatedRowCount * 3));

            var trailing = adapter.GetPresenterAtIndex(6);
            Assert.That(trailing, Is.Not.Null, "The one real cell of the trailing row is not bound.");
            Assert.That(trailing.View.Label.text, Is.EqualTo("row 6"));

            // The two padding cells of the last row must be out of layout, not left
            // showing whatever they held last.
            Assert.That(adapter.GetPresenterAtIndex(7), Is.Null);
            Assert.That(adapter.GetPresenterAtIndex(8), Is.Null);

            var rows = listView.Query<VisualElement>(className: UIToolkitGridAdapter<RowModel, RowView, RowPresenter>.RowUssClassName).ToList();
            Assert.That(rows, Is.Not.Empty, "No grid row element was ever built.");

            var hidden = rows.SelectMany(row => row.Children())
                .Count(cell => cell.style.display.value == DisplayStyle.None);

            Assert.That(hidden, Is.EqualTo(2), "The trailing row's two padding cells are not hidden.");
        });

        [UnityTest]
        public IEnumerator Grid_VirtualizesByRowRatherThanByCell() => UniTask.ToCoroutine(async () =>
        {
            var listView = this.BuildListView();
            using var adapter = new UIToolkitGridAdapter<RowModel, RowView, RowPresenter>(listView, LoadUxml(ItemUxmlPath), 4, ItemHeight, this.instantiator);

            adapter.SetItems(RowModel.Range(1000));
            await this.Settle();

            Assert.That(adapter.RowCount, Is.EqualTo(250));
            Assert.That(adapter.GetPresenters(), Is.Not.Empty, "The grid never laid out.");
            Assert.That(adapter.CreatedRowCount, Is.LessThan(60),
                $"{adapter.CreatedRowCount} rows built for 250 — the grid is not virtualizing.");
            Assert.That(adapter.CreatedCellCount, Is.LessThan(1000),
                "Every cell was built; the grid is materialising the whole source.");
        });

        [UnityTest]
        public IEnumerator Grid_AnEmptySourceRealizesNothingAndDoesNotThrow() => UniTask.ToCoroutine(async () =>
        {
            var listView = this.BuildListView();
            using var adapter = new UIToolkitGridAdapter<RowModel, RowView, RowPresenter>(listView, LoadUxml(ItemUxmlPath), 3, ItemHeight, this.instantiator);

            Assert.DoesNotThrow(() => adapter.SetItems(null));
            await this.Settle();

            Assert.That(adapter.RowCount, Is.Zero);
            Assert.That(adapter.CreatedCellCount, Is.Zero, "An empty grid built cells.");
            Assert.That(adapter.GetPresenterAtIndex(0), Is.Null);
        });

        [Test]
        public void Grid_RefusesZeroCellsPerRow()
        {
            Assert.Throws<ArgumentOutOfRangeException>(
                () => new UIToolkitGridAdapter<RowModel, RowView, RowPresenter>(new ListView(), LoadUxml(ItemUxmlPath), 0, ItemHeight, this.instantiator));
        }

        #endregion

        #region The multi-template adapter

        [UnityTest]
        public IEnumerator MultiTemplate_BuildsEachRowFromItsOwnTemplateViewAndPresenter() => UniTask.ToCoroutine(async () =>
        {
            var listView = this.BuildListView();
            using var adapter = this.BuildMultiTemplate(listView);

            adapter.SetItems(new List<MultiRowModel>
            {
                new NarrowRowModel(0),
                new WideRowModel(1),
                new NarrowRowModel(2),
            });

            await this.Settle();

            Assert.That(adapter.GetPresenters(), Is.Not.Empty, "The multi-template list never laid out.");

            Assert.That(adapter.GetPresenterAtIndex(0), Is.TypeOf<NarrowPresenter>());
            Assert.That(adapter.GetPresenterAtIndex(1), Is.TypeOf<WidePresenter>(),
                "The heterogeneous row was built by the wrong presenter type.");
            Assert.That(adapter.GetPresenterAtIndex(2), Is.TypeOf<NarrowPresenter>());

            // Built from the right UXML, not merely by the right presenter: the wide
            // template is the only one carrying a 'headline' label.
            Assert.That(adapter.GetPresenterAtIndex(1).View.Root.Q<Label>("headline"), Is.Not.Null,
                "Row 1 is not a clone of the wide template.");
            Assert.That(adapter.GetPresenterAtIndex(0).View.Root.Q<Label>("headline"), Is.Null,
                "Row 0 was cloned from the wide template.");
        });

        [UnityTest]
        public IEnumerator MultiTemplate_ReusesPooledSlotsWhenScrollingThroughAlternatingRowTypes() => UniTask.ToCoroutine(async () =>
        {
            var listView = this.BuildListView();
            await this.Settle();

            using var adapter = this.BuildMultiTemplate(listView);

            var models = Enumerable.Range(0, MultiTemplateRowCount)
                .Select(index => index % 2 == 0 ? (MultiRowModel)new NarrowRowModel(index) : new WideRowModel(index))
                .ToList();

            adapter.SetItems(models);
            await this.Settle();

            Assert.That(adapter.GetPresenters(), Is.Not.Empty, "The multi-template list never laid out.");

            // Deliberately modest: DynamicHeight measures every row it binds, and a
            // several-hundred-row dynamic list in a headless panel trips Unity's own
            // "layout update is struggling" recursion guard. That is not a flaw in this
            // test — it is the cost of the dynamic path, stated out loud in the adapter's
            // own remarks — but it is not what this test is about, so the list is sized
            // below it.
            ScrollDown(listView, MultiTemplateRowCount - 1, MultiTemplateRowCount * 45f);
            await this.Settle();

            Assert.That(adapter.GetPresenters(), Is.Not.Empty, "Nothing is bound after scrolling.");

            // The point of the shell-plus-pool shape: a scroll through 400 alternating
            // rows must not clone 400 templates.
            Assert.That(adapter.CreatedSlotCount, Is.LessThan(MultiTemplateRowCount / 2),
                $"{adapter.CreatedSlotCount} template clones for {MultiTemplateRowCount} rows — the pool is not being used.");

            Assert.That(adapter.PooledSlotReuseCount, Is.GreaterThan(0),
                "No bind ever took a slot out of a pool; the swap path is cloning every time.");
        });

        [UnityTest]
        public IEnumerator MultiTemplate_AnEmptySourceRealizesNothingAndDoesNotThrow() => UniTask.ToCoroutine(async () =>
        {
            var listView = this.BuildListView();
            using var adapter = this.BuildMultiTemplate(listView);

            Assert.DoesNotThrow(() => adapter.SetItems(null));
            await this.Settle();

            Assert.That(adapter.Models, Is.Empty);
            Assert.That(adapter.CreatedSlotCount, Is.Zero, "An empty list cloned a template.");
            Assert.That(adapter.GetPresenterAtIndex(0), Is.Null);
        });

        [Test]
        public void MultiTemplate_ARowWantingAnUnregisteredTemplateNamesTheKeyAndWhatIsRegistered()
        {
            var listView = new ListView();
            using var adapter = this.BuildMultiTemplate(listView);

            adapter.SetItems(new List<MultiRowModel> { new UnknownTemplateRowModel(0) });

            // Driven through the control's own callbacks rather than through layout: the
            // throw would otherwise happen inside a panel update, where Unity swallows it
            // into the log and the assertion becomes a string match on console output.
            var shell     = listView.makeItem();
            var exception = Assert.Throws<KeyNotFoundException>(() => listView.bindItem(shell, 0));

            Assert.That(exception.Message, Does.Contain("not-a-registered-template"), "The error does not name the key that was missing.");
            Assert.That(exception.Message, Does.Contain(NarrowRowModel.Template), "The error does not say what IS registered, which is the half that makes it actionable.");
        }

        [Test]
        public void MultiTemplate_ARowNamingAViewTypeThatIsNotTheCollectionsViewIsReported()
        {
            var listView = new ListView();
            using var adapter = this.BuildMultiTemplate(listView);

            adapter.SetItems(new List<MultiRowModel> { new WrongViewTypeRowModel(0) });

            var shell     = listView.makeItem();
            var exception = Assert.Throws<InvalidOperationException>(() => listView.bindItem(shell, 0));

            Assert.That(exception.Message, Does.Contain(nameof(RowView)));
        }

        [Test]
        public void MultiTemplate_RefusesAnEmptyTemplateSetANullTemplateAndANullDictionary()
        {
            Assert.Throws<ArgumentException>(
                () => new UIToolkitMultiTemplateListAdapter<MultiRowModel, RowView, MultiPresenter>(new ListView(), new Dictionary<string, VisualTreeAsset>(), 0f, this.instantiator));

            Assert.Throws<ArgumentException>(
                () => new UIToolkitMultiTemplateListAdapter<MultiRowModel, RowView, MultiPresenter>(new ListView(), new Dictionary<string, VisualTreeAsset> { ["x"] = null }, 0f, this.instantiator));

            Assert.Throws<ArgumentNullException>(
                () => new UIToolkitMultiTemplateListAdapter<MultiRowModel, RowView, MultiPresenter>(new ListView(), null, 0f, this.instantiator));
        }

        [Test]
        public void MultiTemplate_WithoutAFixedHeightChoosesDynamicVirtualization()
        {
            var listView = new ListView { virtualizationMethod = CollectionVirtualizationMethod.FixedHeight };

            using var adapter = this.BuildMultiTemplate(listView);

            // Not incidental: rows of different templates have different heights, and
            // FixedHeight can express exactly one.
            Assert.That(listView.virtualizationMethod, Is.EqualTo(CollectionVirtualizationMethod.DynamicHeight));
        }

        [Test]
        public void MultiTemplate_WithAFixedHeightTakesTheFastPathInstead()
        {
            var listView = new ListView();

            using var adapter = new UIToolkitMultiTemplateListAdapter<MultiRowModel, RowView, MultiPresenter>(
                listView,
                new Dictionary<string, VisualTreeAsset> { [NarrowRowModel.Template] = LoadUxml(ItemUxmlPath) },
                ItemHeight,
                this.instantiator);

            Assert.That(listView.virtualizationMethod, Is.EqualTo(CollectionVirtualizationMethod.FixedHeight));
            Assert.That(listView.fixedItemHeight, Is.EqualTo(ItemHeight).Within(0.001f));
        }

        #endregion

        #region The row factory and the presenter base — no panel needed

        [Test]
        public void ItemViewFactory_BuildsTheViewAroundTheElementItWasGiven()
        {
            var element = LoadUxml(ItemUxmlPath).CloneTree();
            var view    = (RowView)UIToolkitItemViewFactory.Create(typeof(RowView), element);

            Assert.That(view.Root, Is.SameAs(element), "The view is not wrapped around the element the collection recycles.");
            Assert.That(view.Label, Is.Not.Null);
            Assert.That(view.Button, Is.Not.Null);
        }

        [Test]
        public void ItemViewFactory_RejectsATypeThatIsNotAnItemView()
        {
            var exception = Assert.Throws<ArgumentException>(() => UIToolkitItemViewFactory.Create(typeof(string), new VisualElement()));

            Assert.That(exception.Message, Does.Contain(nameof(IUIToolkitItemView)));
        }

        [Test]
        public void ItemViewFactory_RejectsAnAbstractViewByName()
        {
            var exception = Assert.Throws<ArgumentException>(() => UIToolkitItemViewFactory.Create(typeof(BaseUIToolkitItemView), new VisualElement()));

            Assert.That(exception.Message, Does.Contain(nameof(BaseUIToolkitItemView)));
            Assert.That(exception.Message, Does.Contain("abstract"));
        }

        [Test]
        public void ItemViewFactory_RejectsAViewWithoutTheElementConstructorAndSaysWhatItFound()
        {
            var exception = Assert.Throws<ArgumentException>(() => UIToolkitItemViewFactory.Create(typeof(NoElementConstructorView), new VisualElement()));

            Assert.That(exception.Message, Does.Contain(nameof(NoElementConstructorView)));
            Assert.That(exception.Message, Does.Contain(nameof(VisualElement)));
            Assert.That(exception.Message, Does.Contain("String"), "The message does not report the constructor that IS there.");
        }

        [Test]
        public void ItemViewFactory_RejectsNullArguments()
        {
            Assert.Throws<ArgumentNullException>(() => UIToolkitItemViewFactory.Create(null, new VisualElement()));
            Assert.Throws<ArgumentNullException>(() => UIToolkitItemViewFactory.Create(typeof(RowView), null));
        }

        [Test]
        public void ItemView_RefusesToBeBuiltAroundANullElement()
        {
            Assert.Throws<ArgumentNullException>(() => new RowView(null));
        }

        [Test]
        public void Presenter_SetActiveView_TakesTheRowOutOfLayoutRatherThanJustHidingIt()
        {
            var presenter = new RowPresenter();
            presenter.SetView((RowView)UIToolkitItemViewFactory.Create(typeof(RowView), LoadUxml(ItemUxmlPath).CloneTree()));

            presenter.SetActiveView(false);
            Assert.That(presenter.View.Root.style.display.value, Is.EqualTo(DisplayStyle.None),
                "A hidden row must leave layout; visibility:hidden would leave a hole.");

            presenter.SetActiveView(true);
            Assert.That(presenter.View.Root.style.display.value, Is.EqualTo(DisplayStyle.Flex));
        }

        [Test]
        public void Presenter_HandedTheWrongViewType_SaysSoRatherThanInvalidCasting()
        {
            var untyped = (IUIToolkitItemPresenter)new RowPresenter();

            var exception = Assert.Throws<ArgumentException>(() => untyped.SetView(new NoElementConstructorView("not a RowView")));

            Assert.That(exception.Message, Does.Contain(nameof(RowView)));
            Assert.That(exception.Message, Does.Contain(nameof(NoElementConstructorView)));
        }

        [Test]
        public void Presenter_HandedNull_SaysSoRatherThanNullReferencing()
        {
            var untyped = (IUIToolkitItemPresenter)new RowPresenter();

            var exception = Assert.Throws<ArgumentException>(() => untyped.SetView(null));

            Assert.That(exception.Message, Does.Contain("null"));
        }

        #endregion

        #region Argument validation and disposal

        [Test]
        public void List_RefusesANullListViewOrANullTemplate()
        {
            Assert.Throws<ArgumentNullException>(() => new UIToolkitListAdapter<RowModel, RowView, RowPresenter>(null, LoadUxml(ItemUxmlPath), ItemHeight, this.instantiator));
            Assert.Throws<ArgumentNullException>(() => new UIToolkitListAdapter<RowModel, RowView, RowPresenter>(new ListView(), null, ItemHeight, this.instantiator));
        }

        [UnityTest]
        public IEnumerator List_Dispose_DisposesEveryPresenterAndUnhooksTheControl() => UniTask.ToCoroutine(async () =>
        {
            var listView = this.BuildListView();
            var adapter  = new UIToolkitListAdapter<RowModel, RowView, RowPresenter>(listView, LoadUxml(ItemUxmlPath), ItemHeight, this.instantiator);

            adapter.SetItems(RowModel.Range(100));
            await this.Settle();

            var presenters = adapter.GetPresenters();
            Assert.That(presenters, Is.Not.Empty, "The list never laid out.");

            adapter.Dispose();

            Assert.That(presenters.All(presenter => presenter.DisposeCount == 1), Is.True,
                "A presenter was disposed zero times or more than once.");

            Assert.That(listView.makeItem, Is.Null, "Dispose left the adapter wired to the control.");
            Assert.That(listView.bindItem, Is.Null);
            Assert.That(listView.itemsSource, Is.Null);

            Assert.Throws<ObjectDisposedException>(() => adapter.SetItems(RowModel.Range(5)));

            // Idempotent — a using block plus an explicit Dispose is a normal shape.
            Assert.DoesNotThrow(adapter.Dispose);
        });

        [UnityTest]
        public IEnumerator List_APresenterIsDisposedOnceWhenItsElementDies_NotOnEveryRebind() => UniTask.ToCoroutine(async () =>
        {
            var listView = this.BuildListView();
            await this.Settle();

            using var adapter = new UIToolkitListAdapter<RowModel, RowView, RowPresenter>(listView, LoadUxml(ItemUxmlPath), ItemHeight, this.instantiator);

            adapter.SetItems(RowModel.Range(500));
            await this.Settle();

            ScrollDown(listView, 400, 400 * ItemHeight);
            await this.Settle();

            var presenters = adapter.GetPresenters();
            Assert.That(presenters, Is.Not.Empty, "The list never laid out.");

            // The host framework's virtualized-list adapters call Dispose() on every rebind
            // and keep using the presenter afterwards. This path deliberately does not, and
            // that difference is the reason BaseUIToolkitItemPresenter.Dispose documents
            // itself the way it does.
            Assert.That(presenters.All(presenter => presenter.DisposeCount == 0), Is.True,
                "A live presenter was disposed while it was still bound.");

            Assert.That(presenters.Any(presenter => presenter.BindCount > 1), Is.True,
                "No element was ever rebound, so scrolling did not actually recycle anything.");
        });

        #endregion

        #region Scene and fixtures

        /// <summary>Builds a UIDocument with one ListView in it, sized so virtualization has something to virtualize.</summary>
        private ListView BuildListView()
        {
            // Re-asserted here and not only in [SetUp]: for a [UnityTest] the framework
            // resets LogAssert state when it starts pumping the test body, which is AFTER
            // SetUp ran — so the value set there is gone by the time the panel logs
            // anything. Every panel test calls this first.
            LogAssert.ignoreFailingMessages = true;

            this.panelSettings           = ScriptableObject.CreateInstance<PanelSettings>();
            this.panelSettings.scaleMode = PanelScaleMode.ConstantPixelSize;

            this.documentObject = new GameObject(nameof(UIToolkitCollectionsTests));
            this.documentObject.SetActive(false);

            var uiDocument = this.documentObject.AddComponent<UIDocument>();
            uiDocument.panelSettings = this.panelSettings;

            this.documentObject.SetActive(true);

            var root = uiDocument.rootVisualElement;
            Assert.That(root, Is.Not.Null, "The UIDocument produced no root element.");

            root.style.width  = 400;
            root.style.height = ViewportHeight;

            var listView = new ListView
            {
                virtualizationMethod = CollectionVirtualizationMethod.FixedHeight,
                fixedItemHeight      = ItemHeight,
            };

            listView.style.width  = 400;
            listView.style.height = ViewportHeight;

            root.Add(listView);

            return listView;
        }

        private UIToolkitMultiTemplateListAdapter<MultiRowModel, RowView, MultiPresenter> BuildMultiTemplate(ListView listView)
        {
            return new UIToolkitMultiTemplateListAdapter<MultiRowModel, RowView, MultiPresenter>(
                listView,
                new Dictionary<string, VisualTreeAsset>
                {
                    [NarrowRowModel.Template] = LoadUxml(ItemUxmlPath),
                    [WideRowModel.Template]   = LoadUxml(WideUxmlPath),
                },
                0f,
                this.instantiator);
        }

        /// <summary>Yields until the panel has laid out and the collection has filled.</summary>
        /// <remarks>
        /// A fixed frame count rather than a poll on some "is laid out" flag, because there
        /// is not one: ListView fills across a geometry-changed event and then a scheduled
        /// refresh, so the honest wait is "a few frames". Every caller separately asserts
        /// that something was realized, so if this were ever too short the tests fail
        /// rather than silently assert over nothing.
        /// </remarks>
        private async UniTask Settle()
        {
            await UniTask.DelayFrame(8);
        }


        /// <summary>Scrolls the collection to <paramref name="offsetY"/> pixels down.</summary>
        /// <remarks>
        /// <c>ScrollToItem</c> alone is not enough here: it defers to the scroller, which
        /// in a headless test panel can resolve after the frames this test yields, and the
        /// test then quietly asserts against an unscrolled list. Driving the inner
        /// ScrollView's offset as well makes the scroll a fact before the next frame; the
        /// value is clamped to the content by the ScrollView itself.
        /// </remarks>
        private static void ScrollDown(ListView listView, int index, float offsetY)
        {
            listView.ScrollToItem(index);

            var scrollView = listView.Q<ScrollView>();
            Assert.That(scrollView, Is.Not.Null, "The ListView has no ScrollView; it never built its hierarchy.");

            scrollView.scrollOffset = new Vector2(0, offsetY);
        }

        private static VisualTreeAsset LoadUxml(string path)
        {
            #if UNITY_EDITOR
            var asset = UnityEditor.AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(path);
            Assert.That(asset, Is.Not.Null, $"Could not load {path}.");
            return asset;
            #else
            Assert.Ignore("These tests load their UXML through the AssetDatabase and only run in the Editor.");
            return null;
            #endif
        }

        #endregion

        #region Test types

        public sealed class RowModel
        {
            public int Index;

            public static List<RowModel> Range(int count) => Enumerable.Range(0, count).Select(index => new RowModel { Index = index }).ToList();
        }

        public class RowView : BaseUIToolkitItemView
        {
            public Label  Label  { get; }
            public Button Button { get; }

            public RowView(VisualElement root) : base(root)
            {
                this.Label  = root.Q<Label>("label");
                this.Button = root.Q<Button>("button");
            }
        }

        public sealed class WideRowView : RowView
        {
            public Label Headline { get; }

            public WideRowView(VisualElement root) : base(root)
            {
                this.Headline = root.Q<Label>("headline");
            }
        }

        /// <summary>An item view whose only constructor is the wrong one; the factory must say so.</summary>
        public sealed class NoElementConstructorView : IUIToolkitItemView
        {
            public VisualElement Root { get; } = new();

            public NoElementConstructorView(string _)
            {
            }
        }

        public class RowPresenter : BaseUIToolkitItemPresenter<RowView, RowModel>
        {
            public int      BindCount    { get; private set; }
            public int      DisposeCount { get; private set; }
            public RowModel LastModel    { get; private set; }

            public override void BindData(RowModel param)
            {
                ++this.BindCount;
                this.LastModel = param;

                if (this.View.Label != null) this.View.Label.text = $"row {param.Index}";
            }

            public override void Dispose() { ++this.DisposeCount; }
        }

        public abstract class MultiRowModel : MultiTemplateModel
        {
            public int Index { get; }

            protected MultiRowModel(int index) { this.Index = index; }
        }

        public sealed class NarrowRowModel : MultiRowModel
        {
            public const string Template = "narrow";

            public NarrowRowModel(int index) : base(index) { }

            public override string TemplateName  => Template;
            public override Type   PresenterType => typeof(NarrowPresenter);
            public override Type   ViewType      => typeof(RowView);
        }

        public sealed class WideRowModel : MultiRowModel
        {
            public const string Template = "wide";

            public WideRowModel(int index) : base(index) { }

            public override string TemplateName  => Template;
            public override Type   PresenterType => typeof(WidePresenter);
            public override Type   ViewType      => typeof(WideRowView);
        }

        public sealed class UnknownTemplateRowModel : MultiRowModel
        {
            public UnknownTemplateRowModel(int index) : base(index) { }

            public override string TemplateName  => "not-a-registered-template";
            public override Type   PresenterType => typeof(NarrowPresenter);
            public override Type   ViewType      => typeof(RowView);
        }

        public sealed class WrongViewTypeRowModel : MultiRowModel
        {
            public WrongViewTypeRowModel(int index) : base(index) { }

            public override string TemplateName  => NarrowRowModel.Template;
            public override Type   PresenterType => typeof(NarrowPresenter);
            public override Type   ViewType      => typeof(string);
        }

        public abstract class MultiPresenter : BaseUIToolkitItemPresenter<RowView, MultiRowModel>
        {
            public int BindCount { get; private set; }

            public override void BindData(MultiRowModel param)
            {
                ++this.BindCount;
                if (this.View.Label != null) this.View.Label.text = $"row {param.Index}";
            }
        }

        public sealed class NarrowPresenter : MultiPresenter
        {
        }

        public sealed class WidePresenter : MultiPresenter
        {
        }

        #endregion
    }
}
