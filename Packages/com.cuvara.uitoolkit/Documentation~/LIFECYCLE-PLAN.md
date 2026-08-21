# Runtime lifecycle for UXML screens — plan

Status: **plan only**. Nothing here is implemented. Written 2026-08-21.

Audience: whoever implements this, and whoever has to live with it afterwards. The second
audience is the one that decides whether the design is good, so the developer-facing API is
stated before the machinery.

---

## 0. What was read, and what was verified against what

Read in full: `docs/UI-ARCHITECTURE.md` (binding contract); every file under
`Packages/com.cuvara.uitoolkit/Runtime/`; `Packages/com.gdk.core/Scripts/UIModule/ScreenFlow/`
(`ScreenManager.cs`, `IScreenPresenter.cs`, `BaseScreenPresenterCore.cs`,
`ScreenInfoAttribute.cs`); `Packages/com.gdk.core/Scripts/UIModuleUITK/` (backend, presenters,
VContainer registration); `Packages/com.gdk.core/Editor/Tools/ViewCreatorWizard/`.

Verified against the installed editor at
`/mnt/c/Program Files/Unity/Hub/Editor/6000.3.9f1/Editor/Data/Managed/UnityEngine/UnityEngine.UIElementsModule.xml`:

| Member | Present |
|---|---|
| `VisualElement.SetBinding(BindingId, Binding)`, `GetBinding`, `ClearBinding`, `ClearBindings`, `GetBindingInfos` | yes |
| `VisualElement.dataSource`, `dataSourcePath`, `dataSourceType` | yes |
| `BaseVerticalCollectionView.virtualizationMethod`, `fixedItemHeight`, `makeItem`, `bindItem`, `unbindItem`, `destroyItem`, `itemsSource`, `RefreshItem(int)`, `RefreshItems()`, `Rebuild()` | yes |
| `CollectionVirtualizationMethod.FixedHeight` / `.DynamicHeight` | yes |
| `VisualElement.panel`, `RemoveFromHierarchy`, `AttachToPanelEvent`, `DetachFromPanelEvent` | yes |
| `PanelSettings.SetScreenToPanelSpaceFunction`, `UIDocument.sortingOrder` | yes |
| `VisualElement.materialOverride` | **absent** — matches the brief |

Verified against `Library/PackageCache/jp.hadashikick.vcontainer@19ee6e1cc8be/` (VContainer 1.16.9):

- `IObjectResolver.CreateScope(Action<IContainerBuilder>)` → `ScopedContainerBuilder.BuildScope()`,
  which calls `EmitCallbacks(container)` (`ContainerBuilder.cs:36-43`). **Build callbacks DO run
  for a child scope**, therefore `RegisterEntryPoint` inside a `CreateScope` installation really
  does dispatch that scope's entry points. This is the load-bearing fact for the whole design and
  it was not obvious.
- `EntryPointsBuilder.EnsureDispatcherRegistered` checks `Exists(..., includeInterfaceTypes: false)`
  with `findParentScopes` defaulting to false, so a child scope gets **its own** dispatcher rather
  than reusing the parent's (`ContainerBuilderUnityExtensions.cs:13-24`). Correct for us.
- **`IInitializable.Initialize()` runs synchronously inside `Dispatch()`**; `IStartable.Start()` and
  `IAsyncStartable.StartAsync()` are queued onto `PlayerLoopHelper` at `PlayerLoopTiming.Startup`
  (`EntryPointDispatcher.cs:26-39, 58-64, 122-130`) — i.e. **one player-loop point later, not in the
  same call as `CreateScope`**. A screen that opens from `IStartable` therefore has a one-frame gap
  the caller cannot await. Consequence: **do not use `IStartable` for screen open logic.**
- Because UniTask is installed, `com.cysharp.unitask` triggers `VCONTAINER_UNITASK_INTEGRATION`
  (`Runtime/VContainer.asmdef:22-26`), so `IAsyncStartable.StartAsync(CancellationToken)` returns
  **`UniTask`**, not `Awaitable` (`Runtime/Annotations/IAsyncStartable.cs:1-15`).
- `AsyncStartableLoopItem` `Forget()`s the task and **cancels its CTS on `Dispose()`**
  (`PlayerLoopItem.cs:337-346`). The dispatcher is `Lifetime.Scoped` and `IDisposable`, so
  **the token handed to `StartAsync` is already scope-lifetime-bound and cancels on
  `scope.Dispose()`.** That is the free half of the cancellation answer in §2.
- Entities is installed, so `VCONTAINER_ECS_INTEGRATION` is defined and every `Dispatch()` also
  resolves `ContainerLocal<IEnumerable<ComponentSystemBase>>` and sorts world helpers
  (`EntryPointDispatcher.cs:132-140`). Small, but it is a per-screen-open cost. See §10 open Q.

Project facts checked directly:

- `GDK_VCONTAINER` is a `versionDefine` on `jp.hadashikick.vcontainer` in every GameFoundation
  asmdef, so it is **on** in this project.
- `Assets/Scripts/DI/GameLifetimeScope.cs` registers **networking and Nakama only**. It does
  **not** call `RegisterScreenManager()` and does **not** call `RegisterUIToolkitViewBackend()`.
  There is currently **no wired screen flow in this project at all.** This changes the migration
  order (§12) substantially: stage 4 is not "migrate screens", it is "there is nothing to migrate
  yet, so land the design before the first screen exists".

---

## 1. The decision, in one page

### 1.1 Extend the incumbent. Do not replace it.

The contract forbids a second navigation abstraction, and — more decisively — the UI Toolkit
path through `ScreenManager` **already works end to end**:

- `IScreenViewBackend` (`ScreenFlow/BaseScreen/View/IScreenViewBackend.cs`) is the seam.
- `UIToolkitScreenViewBackend` (`UIModuleUITK/Managers/`) implements it: loads a
  `VisualTreeAsset` by the same address the uGUI path uses, builds the view through
  `Cuvara.UIToolkit.View.UIToolkitViewFactory.Create`, and hands back the three
  `VisualElementViewLayer`s off `RootUIDocument`.
- `CanHandle(Type)` discriminates on `ISurfaceScreenView`, so uGUI screens fall through the
  unchanged prefab path (`ScreenManager.cs:366-389`).
- `BaseUIToolkitScreenPresenter<TView[,TModel]>` and `BaseUIToolkitPopupPresenter<…>` exist and
  share the lifecycle bodies on `BaseScreenPresenterCore<TView>` rather than copying them.
- `ViewCreatorWizard` already emits the whole UI Toolkit triple plus a `.uxml`.

So the honest position is: **there is no case for replacement, and I am not going to argue one.**
Everything below is additive to `IScreenManager` / `BaseScreenPresenterCore`, or lives in
`com.cuvara.uitoolkit` where GameFoundation cannot be referenced at all.

### 1.2 Where I disagree with the design direction I was given

The brief says: *"Open a screen → CreateScope … close it → scope.Dispose()."*

**Half of that is wrong against this codebase**, and it matters.

`ScreenManager` deliberately caches presenters for the life of the scene:

```csharp
// ScreenManager.cs:341-393
private readonly Dictionary<Type, IScreenPresenter>       typeToLoadedScreenPresenter = new();
private readonly Dictionary<Type, Task<IScreenPresenter>> typeToPendingScreen         = new();

public async UniTask<IScreenPresenter> GetScreen(Type screenType)
{
    if (this.typeToLoadedScreenPresenter.TryGetValue(screenType, out var p)) return p;   // reuse
    …
}
```

A presenter is created **once** and reopened many times. Meanwhile
`BaseScreenPresenterCore.CloseViewAsync` and `HideView` both call `this.Dispose()`
(`BaseScreenPresenterCore.cs:156-176`) — and the object keeps living and is reopened afterwards.

So in this framework **`Dispose()` already means "release this open cycle", not "end of life".**
That collides head-on with VContainer, where `IDisposable` on a scoped registration means
"the scope ended". Bind a screen's scope to open/close and you get one of two bugs:

1. The scope is disposed on close and the presenter is then reopened from a dead scope; or
2. You stop caching, and every open reloads the UXML, re-clones the tree, re-resolves USS,
   and throws away the ListView's pooled rows and scroll position — a real, visible cost on a
   screen the player opens fifty times a session.

**Corrected direction — two nested lifetimes, both structural:**

| Lifetime | Spans | Owned by | Disposed by |
|---|---|---|---|
| **Screen scope** (`IScopedObjectResolver`) | first open → screen destroyed / scene change | `ScreenManager` | `DestroyView` / `CleanUpAllScreen` |
| **Open cycle** (`ScreenSubscriptions` bag + a linked `CancellationTokenSource`) | each `Open` → matching `Close`/`Hide` | the presenter base | the presenter base, before the next `BindData` |

The scope holds things that are *per screen instance*: the view, screen-scoped services, the
adapter. The bag holds things that are *per open*: every event handler, every ECS sink
registration, every service subscription. **The author never disposes either by hand.**

This is the design. Everything else is consequence.

---

## 2. What a developer actually types (the convenience test)

This section is deliberately first-among-equals. If this part is bad, the rest does not matter.

### 2.1 Adding a screen: the whole of it

**Step 1 — run the wizard** (`GDK ▸ View Creator Wizard`): Name `Inventory`, Type `Screen`,
Backend `UIToolkit`, Has Model ✔. It writes four files (three today; the `.uss` and the test
file are proposed in §9):

```
Assets/UI/Screens/Inventory/
    InventoryScreenView.cs        # model + view + presenter, one file (existing wizard behaviour)
    Inventory.uxml
    Inventory.uss                 # NEW — wizard should emit this
    InventoryScreenPresenterTests.cs   # NEW — wizard should emit this
```

**Step 2 — mark `Inventory.uxml` addressable** with address `Inventory` (matches the emitted
`[ScreenInfo(nameof(InventoryScreenView))]`).

**Step 3 — open it, from anywhere that has `IScreenManager` injected:**

```csharp
await this.screenManager.OpenScreen<InventoryScreenPresenter, InventoryScreenModel>(model);
```

**There is no registration step.** `ScreenManager.GetScreen` constructs the presenter through
the container (`ScreenManager.cs:363`), which is why the wizard stamps `[Preserve]` on the
generated constructor. That is already the right amount of typing, and the plan must not add to it.

### 2.2 The generated file, as it should look after this plan

The only shape change from today is the **`ScreenSubscriptions` parameter on `BindData`**.
Everything else the author writes is their own screen.

```csharp
namespace UI.Screens.Inventory
{
    using Cuvara.UIToolkit.Collections;
    using Cuvara.UIToolkit.Core;
    using Cuvara.UIToolkit.Lifecycle;          // NEW
    using Cuvara.UIToolkit.View;
    using Cysharp.Threading.Tasks;
    using GameFoundation.Scripts.UIModule.ScreenFlow.BaseScreen.Presenter;
    using GameFoundation.Scripts.UIModule.ScreenFlow.BaseScreen.View;
    using GameFoundation.Scripts.UIModule.UITK.Presenter;
    using GameFoundation.Signals;
    using UniT.Logging;
    using UnityEngine.Scripting;
    using UnityEngine.UIElements;

    // ---- ViewModel: plain data. No VisualElement, no VisualTreeAsset, no UIDocument. ----
    public readonly struct InventoryRowVm
    {
        public readonly string Name;
        public readonly int    Count;
        public InventoryRowVm(string name, int count) { this.Name = name; this.Count = count; }
    }

    public sealed class InventoryScreenModel
    {
        public string CharacterId;
    }

    // ---- View: the ONLY layer that knows VisualElement. ----
    public interface IInventoryScreenView : ISurfaceScreenView
    {
        ListView   Items { get; }
        Button     Close { get; }
        void       RenderHeader(string title, int weight);
    }

    public sealed class InventoryScreenView : BaseUIToolkitView, IInventoryScreenView
    {
        private readonly Label title;
        private readonly Label weight;

        public ListView Items { get; }
        public Button   Close { get; }

        // The (VisualTreeAsset) ctor is the one UIToolkitViewFactory calls. Do not change it.
        public InventoryScreenView(VisualTreeAsset asset) : base(asset)
        {
            this.StretchToParent();
            this.title  = this.Root.Q<Label>("title");
            this.weight = this.Root.Q<Label>("weight");
            this.Items  = this.Root.Q<ListView>("items");
            this.Close  = this.Root.Q<Button>("btn-close");
        }

        public void RenderHeader(string t, int w)
        {
            this.title.text  = t;
            this.weight.text = $"{w} kg";
        }
    }

    // ---- Presenter: no UIDocument, no VisualElement, testable as plain C#. ----
    [ScreenInfo(nameof(InventoryScreenView))]
    public sealed class InventoryScreenPresenter
        : BaseUIToolkitScreenPresenter<IInventoryScreenView, InventoryScreenModel>
    {
        private readonly IInventoryService inventory;
        private readonly VisualTreeAsset   rowTemplate;   // injected, see §2.3

        [Preserve]
        public InventoryScreenPresenter(
            SignalBus signalBus, ILoggerManager loggers,
            IInventoryService inventory, [RowTemplate("InventoryRow")] VisualTreeAsset rowTemplate)
            : base(signalBus, loggers)
        {
            this.inventory   = inventory;
            this.rowTemplate = rowTemplate;
        }

        // The ONLY overload. There is no parameterless BindData to accidentally use.
        public override async UniTask BindData(InventoryScreenModel model, ScreenSubscriptions subs)
        {
            // Everything registered through `subs` is released before the next BindData
            // and on close. Registering the same handler twice across reopens is impossible
            // because the bag was emptied in between.
            subs.Clicked(this.View.Close, this.CloseView);

            var adapter = new UIToolkitListAdapter<InventoryRowVm, InventoryRowView, InventoryRowPresenter>(
                this.View.Items, this.rowTemplate, itemHeight: 64f);
            subs.Add(adapter);                                   // IDisposable — released on close

            subs.Add(this.inventory.Changed.Subscribe(_ => Refresh()));   // R3 IDisposable
            await Refresh();

            async UniTask Refresh()
            {
                var items = await this.inventory.GetItemsAsync(model.CharacterId, subs.Token);
                this.View.RenderHeader("Inventory", items.TotalWeight);
                adapter.SetItems(items.Rows);                    // virtualized; no rebuild storm
            }
        }
    }
}
```

**Count of things the author had to remember about lifecycle: zero.** No `Dispose` override, no
`UnregisterCallback`, no `CancellationTokenSource`, no scope. `subs` is the single affordance and
it is a required parameter, so it cannot be skipped.

Compare with today's generated popup, which has to do this by hand
(`ViewCreatorTemplates.cs:343-356` — the comment there explicitly says `BindData` reruns on every
re-open):

```csharp
// TODAY — the thing this plan deletes
this.View.BtnClose.clicked -= this.OnClose;
this.View.BtnClose.clicked += this.OnClose;
…
public override void Dispose() { base.Dispose(); this.View.BtnClose.clicked -= this.OnClose; }
```

### 2.3 When a screen needs its own services or its own assets: the opt-in scope

Only then. Default is nothing.

```csharp
[ScreenInfo(nameof(InventoryScreenView))]
public sealed class InventoryScreenPresenter : …
{
    // Runs ONCE, when the screen instance is created — not per open.
    protected override void ConfigureScope(IContainerBuilder b)
    {
        b.Register<IInventoryFilter, DefaultInventoryFilter>(Lifetime.Scoped);
        b.RegisterInstance(this.rowTemplateHandle);
    }
}
```

That is the whole opt-in. `ConfigureScope` defaults to a no-op, so **the six other consumers of
`com.gdk.core` see no behaviour change at all** (§12).

**The obstacle this hits, stated rather than glossed.** `ScreenManager` does not hold an
`IObjectResolver`. It goes through `GetCurrentContainer()` →
`GameFoundation.DI.IDependencyContainer`, whose full surface is `TryResolve` / `Resolve` /
`ResolveAll` / `Instantiate` / `Inject` / `InjectGameObject` / `InstantiatePrefab`
(`Scripts/DI/IDependencyContainer.cs:8-31`). **There is no `CreateScope` on it**, and there is a
non-VContainer implementation in the tree (`ActivatorContainer`,
`Tests/Runtime/TestDoubles.cs:176`) that could not provide one.

Resolution — a **sibling interface**, matching the pattern the codebase already used for
`ISurfaceScreenView` rather than widening `IScreenView`:

```csharp
// GameFoundation.DI — new, additive, nothing is forced to implement it
public interface IScopedDependencyContainer : IDependencyContainer
{
    IDisposable CreateScope(Action<IContainerBuilder> install, out IDependencyContainer scoped);
}
```

`ScreenManager` does `if (container is IScopedDependencyContainer scoped && presenter overrides
ConfigureScope)` — and falls back to today's behaviour otherwise. A container that cannot scope
simply never scopes, which is exactly the default. `ActivatorContainer` and every consumer's
container keep compiling untouched.

### 2.4 Reuse vs recreate: what the developer types

```csharp
await screens.OpenScreen<InventoryScreenPresenter>();                 // reuse (default)
await screens.OpenScreen<InventoryScreenPresenter>(ScreenOpen.Fresh); // destroy first, then open
screens.DestroyScreen<InventoryScreenPresenter>();                    // explicit teardown
```

### 2.5 Step count

One wizard run, one Addressables address, three hand-edits inside the generated file (query the
ListView, build the adapter, render). **Five steps, of which two are tooling.** I consider that
the target and would treat any growth beyond it as a regression to be argued for.

---

## 3. The lifecycle states and the legal transitions (Q1)

`ScreenStatus` already exists — `{ Opened, Closed, Hide, Destroyed }`
(`IScreenPresenter.cs:63-69`) — and it is too coarse to describe an async open. It conflates
"never loaded" with "closed", and it has no in-flight states, so a double-open cannot be detected
from it. Proposal: **keep `ScreenStatus` verbatim as the public property** (six consumers compile
against it) and add a finer, additive `ScreenLifecycleState` beside it.

```
                    ┌──────────┐
                    │ Unloaded │◄────────────────────────┐
                    └────┬─────┘                         │
       GetScreen(): container.Instantiate + backend.CreateViewAsync
                         │  ASYNC · MAY FAIL             │
                    ┌────▼─────┐                         │
                    │ Loading  ├──fail──► Unloaded (cache NOT poisoned — see §10)
                    └────┬─────┘                         │
        SetView · CloneTree done · parented into ShowLayer · alpha 0
                    ┌────▼────────┐                      │
        ┌──────────►│ Constructed │                      │
        │           └────┬────────┘                      │
        │        BindData(model, subs)  ASYNC · MAY FAIL │
        │           ┌────▼────┐                          │
        │           │ Opening ├──fail──► Closed          │
        │           └────┬────┘   (bag cleared, no half-open state)
        │        View.Open() incl. intro transition       │
        │           ┌────▼───┐                            │
        │      ┌───►│ Opened │◄───┐                       │
        │      │    └─┬───┬──┘    │                       │
        │      │      │   │       │                       │
        │   Show()  Hide  Close   │                       │
        │      │      │   │       │                       │
        │  ┌───┴──┐ ┌─▼───▼─┐  ┌──┴─────┐                 │
        │  │Hidden│ │Closing│  │ (reopen)│                │
        │  └───┬──┘ └───┬───┘  └────────┘                 │
        │      │        │                                 │
        │      │   ┌────▼───┐                             │
        └──────┴───┤ Closed ├──────┐                      │
                   └────┬───┘      │                      │
                        │      DestroyView()              │
                   ┌────▼──────┐                          │
                   │ Destroyed ├──────────────────────────┘
                   └───────────┘   (cache entry removed)
```

### Where each transition runs, and what may fail

| Transition | Runs where | May fail? | On failure |
|---|---|---|---|
| `Unloaded → Loading` | `ScreenManager.GetScreen`, main thread, awaited | **yes** — missing address, no `(VisualTreeAsset)` ctor, no `RootUIDocument` in scene | remove the pending entry in a `finally`, stay `Unloaded`, rethrow to the caller |
| `Loading → Constructed` | `backend.CreateViewAsync` → `presenter.SetView` | no (`CloneTree` is synchronous, so there is no readiness gap) | — |
| `Constructed/Closed/Hidden → Opening` | `presenter.OpenViewAsync`, awaited | **yes** — `BindData` calls services | clear the bag, revert to `Closed`, rethrow |
| `Opening → Opened` | `View.Open()` + intro | should not — but a tween library can throw | force alpha 1 in a `finally`, log, stay `Opened` |
| `Opened → Hidden` | `HideView()`, **synchronous** | no | — |
| `Opened → Closing → Closed` | `CloseViewAsync`, awaited, runs outro | should not | force alpha 0 in a `finally` |
| `* → Destroyed` | `DestroyView()`, synchronous | must not — it is the cleanup path | swallow-and-log per step; never abort mid-teardown |

**Hide vs Close, stated plainly** (this is a real distinction in the incumbent and the plan keeps it):

| | Hide | Close |
|---|---|---|
| Synchronous? | yes | no — awaits the outro |
| Stays in `activeScreens`? | **yes** — it is still on the stack, just covered | no |
| Open-cycle bag | cleared | cleared |
| Visual tree | retained, **stays in whatever layer it is already in** | retained, reparented into `HiddenLayer` by `OnCloseScreen` (`ScreenManager.cs:581`) |
| Signal fired | **none** — `ScreenHideSignal` exists (`ScreenSignals.cs:10`) but the `Fire` is commented out (`BaseScreenPresenterCore.cs:175`) | `ScreenCloseSignal` (+ `PopupHiddenSignal` for popups) |
| Reopen path | `Show()` + `BindData` | `Open()` + `BindData` |
| Who calls it | `ScreenManager` when a new screen covers this one (`:525`, `:540`, `:629`) | the user / back navigation |

**Correction worth stating loudly, because §7's "hidden screens cost nothing" claim depends on
it:** a *hidden* screen is **not** moved to the `display:none` layer today. It is left in the
show layer with opacity 0, which means **it keeps being laid out and keeps being drawn as a
fully transparent tree**. Only a *closed* screen is reparented into the hidden layer. For a
stack three screens deep that is two full screen trees laid out every frame for nothing.

> **Change: `HideView` on the UI Toolkit presenter should also reparent into `HiddenLayer`.**
> It is a one-line addition in the UITK-only presenter (`SetViewParent(screens.HiddenLayer)`),
> leaves the uGUI path untouched, and is the difference between §7 being true and being
> aspirational. Ordering matters: reparent *after* `View.Hide()`, so a future fade-out hook
> still has a laid-out tree to animate.

There is a second wasted reparent on the open path: `OpenScreen` calls `MoveToHiddenLayer(next)`
and then immediately `MoveToActiveLayer(next)` (`ScreenManager.cs:326-327`). For UI Toolkit that
is two `VisualElement.Add()` calls and two hierarchy-dirty marks per open. Harmless but pointless;
worth removing in stage 2.

---

## 4. Async, honestly (Q2)

### 4.1 Who owns which CancellationToken

Three tokens, nested, and the developer only ever sees one.

| Token | Lifetime | Source | Cancelled by |
|---|---|---|---|
| Scene token | the scene scope | `IAsyncStartable.StartAsync(ct)` on a scene entry point — **verified** to be cancelled on `scope.Dispose()` (`PlayerLoopItem.cs:337-346`) | scene unload |
| **Screen token** | screen instance | linked CTS created when the screen scope is created | `DestroyView` / `CleanUpAllScreen` |
| **Open token** (`subs.Token`) | one open cycle | linked CTS created at the top of `OpenViewAsync`, linked to the screen token | `Close` / `Hide` / `Destroy` |

The developer sees exactly `subs.Token`, and every `await` inside `BindData` takes it. Nothing
else is exposed, because anything longer-lived than the open cycle should not be started from
`BindData` anyway.

### 4.2 Closed while still loading

The load is **shared** — `typeToPendingScreen` gives one `Task` to every concurrent caller
(`ScreenManager.cs:350-357`). Cancelling it because *one* caller lost interest would break the
others. So:

> **Rule: the load is never cancelled. The open is.**

Implementation: an `int openGeneration` on the presenter, incremented on every open request and
on every close. After each `await` in the open path:

```csharp
var generation = ++this.openGeneration;
var presenter  = await this.GetScreen(type);      // shared, uncancellable
if (generation != this.openGeneration) return;    // someone closed or re-opened while we waited
```

The loaded view is *kept* in the cache in `Constructed` state — the work is not wasted, it is
just not shown. Next open is instant.

### 4.3 Opened twice in the same frame

Two distinct cases, and today they behave differently:

1. **Two opens of a screen not yet loaded** — already correct: `typeToPendingScreen` dedupes to
   one load, both awaiters get the same presenter.
2. **Open on an already-`Opened` screen** — today
   (`BaseScreenPresenterCore.cs:142-149`) this calls `Dispose()` then re-runs `BindData()` and
   returns without re-running `View.Open()`. That is a *rebind*, not an open, and it is only
   discoverable by reading the source. Keep the behaviour (changing it would break consumers),
   **name it**: expose `ScreenLifecycleState` so a caller can ask, and document the rebind
   explicitly in the presenter base XML doc.

### 4.4 The `async void` on the critical path

```csharp
// BaseScreenPresenterCore.cs:130-136 — as it stands today
public async void SetView(IUIView viewInstance)
{
    this.View = (TView)viewInstance;
    this.ScreenId = ScreenHelper.GetScreenId<TView>();
    await this.WaitForViewReady();
    this.OnViewReady();
}
```

`async void`: unobservable exceptions, uncancellable, and `ScreenManager` proceeds to
`MoveToActiveLayer` and `OpenViewAsync` without awaiting it. For UI Toolkit `WaitForViewReady`
is `UniTask.CompletedTask` so it happens to complete synchronously — but for uGUI it is a real
wait and the race is live. Add `UniTask SetViewAsync(IUIView, CancellationToken)` additively;
keep `SetView` as `void SetView(v) => SetViewAsync(v, default).Forget();` so nothing breaks.

---

## 5. The stack and the navigation API (Q3)

**No new navigation abstraction.** `IScreenManager` is it. What it already has:

`CurrentActiveScreen` · `RootScreen` · `RootPopup` · `ScreenLayer`/`HiddenLayer`/`OverlayLayer`
(`IViewLayer`, backend-agnostic) · `GetScreen<T>()` · `OpenScreen<T>()` · `OpenScreen<T,TModel>(m)`
· `CloseCurrentScreen()` · `CloseAllLastOverlayScreenAsync()` · `CloseAllScreen()` ·
`CloseAllScreenAsync()` · `CleanUpAllScreen()` · `ActiveScreenCount` · `EnableBackToClose(bool)` ·
`IsBackToCloseEnabled` · `HandleBackNavigation()`.

Mapping onto push/pop/replace/modal:

| Concept | Existing API |
|---|---|
| push | `OpenScreen<T>()` — the covered screen is moved to the hidden layer and stays in `activeScreens` |
| pop | `CloseCurrentScreen()` |
| replace | `IsClosePrevious = true` on the presenter (`IScreenPresenter.IsClosePrevious`) |
| modal / popup | `[PopupInfo(address, isEnableBlur, isCloseWhenTapOutside, isOverlay)]` + a `BasePopupPresenter`; `isOverlay: true` parents into `OverlayLayer` |
| back | `HandleBackNavigation()` |

**The house rule these additions must respect**, recorded in the existing doc comments
(`ScreenManager.cs:94-96`, `ScreenPresenterViewType.cs:18-26`): add read-only members to
`IScreenManager` only while `ScreenManager` is its sole implementer, and **never add members to
`IScreenPresenter` or `IScreenView`** — introduce a sibling interface or reflect over the base
chain instead. That is why `BindData(model, subs)` in §2.2 is a `virtual` on the presenter *base
class*, not a member of `IScreenPresenter`, and why §2.3's scope hook is a sibling container
interface. The three manager additions below are methods rather than read-only properties, which
stretches the rule; they are proposed because `ScreenManager` is documented as the only
implementer, and if that turns out to be false in one of the six repos they should become a
sibling `IScreenLifecycleManager` instead.

**Additions, all additive:**
- `UniTask<TPresenter> OpenScreen<TPresenter>(ScreenOpen options)` — carries `Fresh`.
- `void DestroyScreen<TPresenter>()` / `void DestroyScreen(Type)`.
- `ScreenLifecycleState GetState(Type)`.

### Back, and `BackNavigationSource`

One fact that makes this urgent rather than optional: `ScreenManager.Tick`'s Escape poll is
wrapped in `#if ENABLE_LEGACY_INPUT_MANAGER` (`ScreenManager.cs:680-688`), and this project has
`activeInputHandler: 1` (Input System only), so **that symbol is undefined and the legacy back
path is compiled out entirely**. `BackNavigationSource` is therefore not an alternative to the
incumbent back handling — it is the *only* back handling that can work here, and it is currently
registered nowhere.

`BackNavigationSource` is right to take no policy decision, and the plan does not change it.
The wiring, which belongs in `com.gdk.core` (host side, where policy lives) and already half
exists in `UIModuleUITK/Managers/UIToolkitBackNavigation.cs`:

```csharp
this.backSource = new BackNavigationSource(rootUIDocument.RootVisualElement);
this.backSource.BackRequested += () => { if (screens.IsBackToCloseEnabled) screens.HandleBackNavigation(); };
```

**Back at the root** is `HandleBackNavigation`'s existing job: when `ActiveScreenCount == 1` it
opens the quit-confirmation popup instead of closing. That is policy and it stays in
GameFoundation. One caveat carried over from `BackNavigationSource`'s own doc, worth repeating
because it will bite: `NavigationCancelEvent` is routed by the panel's **focus controller**, so
with no focused element in the panel, delivery to the root is Unity's dispatch behaviour and not
guaranteed. Mitigation: the topmost open screen's root should take focus on open. That is a
concrete behaviour the plan should add to `BaseUIToolkitScreenPresenter.OpenViewAsync`, and it
is the single most likely source of "back does nothing" bug reports.

---

## 6. Instance reuse vs recreate (Q4)

**Rule: retain by default; destroy on demand.**

Why retain:
- It is what the incumbent already does, and changing it is a behaviour break for six consumers.
- The expensive parts of a UI Toolkit screen open are the Addressables load, `CloneTree`, and USS
  style resolution on first layout. All three are paid once and reused.
- A retained tree keeps ListView pooled rows, scroll offset, and text-field state — which is
  usually what a player expects when they reopen a screen within a session.

Why the escape hatch is mandatory anyway:
- **Session-scoped data.** A character-select screen retained across logout shows the previous
  account's characters until something rebinds it. `Fresh` is the honest fix.
- **Large trees.** A world map or a full crafting tree is not worth keeping resident.
- **Scene change** is already handled: `StartLoadingNewSceneSignal → CleanUpAllScreen`
  (`ScreenManager.cs:170`).

What retention costs, stated so nobody is surprised: the whole cloned `VisualElement` tree per
retained screen, plus the ListView's realized rows, plus the `VisualTreeAsset` held by
Addressables until `UnloadViewAsset` runs. For a 20-screen game that is small; for 200 screens it
is not, and at that point the answer is an LRU eviction on `typeToLoadedScreenPresenter` — worth
designing then, not now.

---

## 7. Where the visual tree lives while hidden (Q5)

Three candidates, costed:

| Approach | Layout cost while hidden | Draw cost | Retains state | Re-show cost |
|---|---|---|---|---|
| `RemoveFromHierarchy()` (detach) | zero | zero | tree yes, but `panel` becomes null → `schedule` stops, focus lost | full re-attach: `AttachToPanelEvent`, full style resolve + layout |
| `visibility: hidden` in place | **full layout still runs** | zero | yes | ~free |
| `display: none` in place / via ancestor | zero (excluded from layout) | zero | yes | one layout pass on that subtree |

**Decision: reparent into `ClosedLayer`, whose UXML already carries `display: none`**
(`Runtime/Managers/RootUIDocument.uxml`, the `root-ui-closed` element). It is the `display:none`
row of that table with none of the detach costs, and `ScreenManager.MoveToHiddenLayer` /
`MoveToActiveLayer` already drive it through `IViewSurface.SetParent(IViewLayer)`.

**This is true today for *closed* screens and false for *hidden* ones** — see the correction in
§3. A hidden screen currently sits in the show layer at opacity 0 and is still laid out and
drawn. Making `HideView` reparent is a prerequisite for this section, not an optimisation on
top of it.

**The trap this creates, which must be documented in the presenter base:** under a
`display: none` ancestor, descendants have no resolved layout. `resolvedStyle.width`,
`element.layout`, `worldBound` are zero or stale for a hidden screen and remain so until one
layout pass after it is shown. Consequences:

- Do not measure anything in `BindData`; measure in a one-shot `GeometryChangedEvent` handler
  registered through `subs`.
- `ListView` with `CollectionVirtualizationMethod.FixedHeight` is fine (heights are declared).
  `DynamicHeight` measures, so a list bound while hidden can compute a wrong viewport. **Prefer
  `FixedHeight` for screens**, and if `DynamicHeight` is genuinely needed, call `RefreshItems()`
  from the first `GeometryChangedEvent` after show.

Note also that `BaseUIToolkitView.Hide()/Show()` currently toggle **opacity + pickingMode**, not
`display`. That is correct for a view that must stay laid out (a fading popup) and wasteful for a
stacked screen — but the ancestor `display:none` on `ClosedLayer` already short-circuits it, so
the two mechanisms compose rather than conflict. Leave both; document the division.

---

## 8. Data binding (Q6)

**Default: plain setters. `SetBinding`/`dataSource`: not in v1.**

All of `SetBinding(BindingId, Binding)`, `dataSource`, `dataSourcePath`, `dataSourceType` were
verified present in 6000.3.9f1. The argument against using them here is not availability.

- The contract requires the Presenter to transform data into a ViewModel and the View to render
  it, and requires the Presenter to be testable with a **mocked view interface and no
  `VisualElement`**. A `dataSource` binding is resolved by the panel, so asserting that a binding
  produced the right text requires a live panel — the exact thing the contract's testability
  clause rules out. A `view.RenderHeader(title, weight)` call is asserted on a mock in one line.
- `dataSourcePath` in UXML puts *property-path knowledge* into the UXML file. The contract says
  UXML is "structure only … and the names/classes the View queries". A path string is a data
  contract, not structure, and it is not checked by the compiler — a renamed ViewModel field
  fails silently at runtime instead of at build.
- The View class stops being the single place that knows how the screen is populated, which is
  the layering the contract exists to protect.

**Where runtime binding would genuinely earn its keep**, and the criteria for revisiting: a row
template with eight or more fields, bound thousands of times, where the per-bind setter calls
show up in a profile. We are nowhere near that, and the collection adapters already route
`bindItem → presenter.BindData(model)`, which is testable. Revisit with a profile capture, not
with an opinion.

**When a screen gets its data, and how it refreshes:**

| Moment | Path |
|---|---|
| open | `OpenViewAsync` → `BindData(model, subs)` → `service.GetAsync(subs.Token)` → `view.Render(vm)` |
| service-driven change | `subs.Add(service.Changed.Subscribe(...))` → recompute vm → `view.Render(vm)` |
| ECS-driven change | bridge → `IViewModelSink.Push(vm)` on the presenter → `view.Render(vm)` (§9) |
| list, partial | `adapter.RefreshItems()` |
| list, source identity changed | `adapter.SetItems(rows)` (which does the `Rebuild`) |
| **never** | per frame. `ITickable` on a presenter is a design smell; say so in review. |

---

## 9. The ECS path (Q7)

The package already ships the whole adapter side, and it is correct:

`EcsViewModelBridge<TComponent, TViewModel>` — `SystemBase` (not `ISystem`: it holds a managed
`List` of sinks and calls across an interface), `[UpdateInGroup(typeof(PresentationSystemGroup))]`,
`SetChangedVersionFilter` so untouched chunks are skipped, and `Enabled = sinks.Count > 0` so a
world with no screen open does not even evaluate the query. `IViewModelSink<T>.Push(in T)`.
`EcsSinkRegistration` as an `IDisposable` that unregisters.

**The one thing the plan changes: where the registration lives.**

The `EcsSinkRegistration` doc-comment currently suggests registering it in the screen's child
scope. That is *safe* but wrong-grained: a screen that is **hidden or closed but retained** would
keep receiving pushes, keep converting components, and keep calling `view.Render` into a
`display:none` tree — invisible work, forever, for every retained screen in the game.

> **Rule: the ECS sink registration goes in the open-cycle bag, not the scope.**

```csharp
public override async UniTask BindData(HudModel model, ScreenSubscriptions subs)
{
    subs.Add(EcsSinkRegistration.Bind(this.healthBridge, this));   // unregisters on close/hide
    …
}

// Presenter implements the sink. Main thread, change-driven, never per frame.
public void Push(in HealthVm vm) => this.View.RenderHealth(vm.Current, vm.Max);
```

The scope remains the backstop: if a screen is destroyed while open, scope disposal releases
whatever the bag would have.

**How the presenter gets the bridge**, given that systems live in the `World` and DI objects live
in a scope. Two options; the plan picks the second.

```csharp
// A. World lookup — works, but it is a locator, and the contract dislikes those.
world.GetExistingSystemManaged<HealthBridge>()

// B. Register the system instance at bootstrap. Constructor injection, honest, testable.
builder.RegisterInstance(World.DefaultGameObjectInjectionWorld.GetOrCreateSystemManaged<HealthBridge>());
```

Option B is one line per bridge at bootstrap and turns every presenter's dependency on ECS into
an ordinary constructor parameter that a test can substitute. Take B.

Two things that are **not** negotiable and should be lint-checked in review: nothing in
`Cuvara.UIToolkit.Ecs` may name a `VisualElement` (the assembly does not even reference
UIElements — keep it that way), and a ViewModel must be a plain value, preferably a
`readonly struct`, because `Push` takes it by `in`.

---

## 10. Failure modes, and what structurally prevents each (Q10)

| Failure | What prevents it (structure, not discipline) |
|---|---|
| **Leaked event subscription across reopen** | `BindData(model, ScreenSubscriptions subs)` is the only overload. The bag is emptied by the base *before* each `BindData` and on close/hide. There is no API by which the author can register outside it, and nothing to remember to undo. This deletes the unregister-then-register dance in the current wizard templates. |
| **Presenter outliving its view** | `DestroyView` nulls `View` and moves the state to `Destroyed`. Every `this.View` access in the base goes through `RequireView()`, which throws naming the screen id. The state machine refuses `Destroyed → Opening` without a reload. |
| **Double-open** | Three layers: `typeToPendingScreen` dedupes the load; `openGeneration` invalidates a stale continuation; the state machine rejects `Opening → Opening`. |
| **Screen referencing a disposed scope** | Everything a presenter needs is **constructor-injected**, so there is no resolver field to use later. `ConfigureScope` runs once at creation and the scope is only disposed in `DestroyView`, which also nulls the reference. If someone does hold a resolver, VContainer's own `ObjectDisposedException` names it. |
| **ECS sink surviving its screen** | The registration is an `IDisposable` produced inside `BindData` and added to a bag the base clears. `EcsViewModelBridge` sets `Enabled = false` at zero sinks, so even a bug degrades to "system idle" rather than "system pushing into a dead view". |
| **A faulted load poisoning the screen forever** | *This is a live defect, not a hypothetical.* `ScreenManager.GetScreen` (`:350-357`) adds the task to `typeToPendingScreen` and removes it **after** the `await`. If `InstantiateScreen` throws, the faulted `Task` stays in the dictionary and every later open of that screen re-awaits the same faulted task and rethrows the original stale exception. Fix: remove in a `finally`. |
| **Unobservable exception during view attach** | `async void SetView` → additive `SetViewAsync` (§4.4). |
| **Addressables handles leaked on every scene change** | *Second live defect.* `CleanUpAllScreen` (`ScreenManager.cs:449-464`, subscribed to `StartLoadingNewSceneSignal`) calls `Dispose()` on presenters whose status is `Opened` and then clears `typeToLoadedScreenPresenter`. It never calls `DestroyView`, so `UnloadViewAsset` — the only caller of `IAssetsManager.Unload` — **never runs on the scene-change path**. Every `VisualTreeAsset` (and every uGUI screen prefab) loaded in a scene stays resident for the process lifetime. Fix: `CleanUpAllScreen` calls `DestroyView()` on every cached presenter, not `Dispose()` on the open ones. This is a behaviour change for the six consumers and must be its own reviewed step. |
| **Screen measured while `display:none`** | Documented in the base; `FixedHeight` recommended for screens; a `subs.OnFirstGeometry(...)` helper for the cases that genuinely need measurement. |
| **Back silently doing nothing** | The topmost screen focuses its root on open, so `NavigationCancelEvent` has a focus path to trickle down. |

---

## 11. Testability (Q11)

### 11.1 A presenter test, with no scene, no `UIDocument`, no `VisualElement`

```csharp
[Test]
public async Task BindData_RendersEveryRow()
{
    var view    = new FakeInventoryView();              // implements IInventoryScreenView
    var service = new FakeInventoryService(rows: 3);
    var subs    = new ScreenSubscriptions();
    var sut     = new InventoryScreenPresenter(NullSignalBus, NullLoggers, service, rowTemplate: null);

    sut.SetViewForTest(view);
    await sut.BindData(new InventoryScreenModel { CharacterId = "c1" }, subs);

    Assert.AreEqual(3, view.LastRows.Count);
    subs.Dispose();
    Assert.AreEqual(0, subs.LiveCount, "BindData leaked a subscription");
}
```

`subs.LiveCount` after disposal is the assertion that makes the leak class of bug **testable**,
not merely discouraged. Every generated test file should include it.

### 11.2 What the package should SHIP so nobody writes their own

A new `Runtime/TestSupport/` with its own asmdef (`Cuvara.UIToolkit.TestSupport`), shipped in the
package and referenced by consumers' test asmdefs. Nothing in it references GameFoundation, so
`check_standalone.py` stays green.

| Double | Replaces / enables |
|---|---|
| `FakeVisualTreeAssetLoader` | generalises the existing `StubVisualTreeAssetLoader` in `Tests/Runtime/ViewLifecycleTests.cs:24`; adds `FailFor(key)` and `DelayFrames(key, n)` so cancel-during-load is testable |
| `RecordingScreenSubscriptions` | asserts every registration was released; exposes `LiveCount` |
| `FakeViewLayer` / `RecordingViewSurface` | asserts parenting with no panel; the existing tests hand-roll a `ForeignLayer` (`ViewLifecycleTests.cs:249`) |
| `SpyViewModelSink<T>` | records `Push` calls with values and ordering |
| `ScreenLifecycleRecorder` | records the state sequence; `AssertLegalSequence()` |
| `TestScreenScope` | builds a real minimal VContainer root + child scope so scope wiring is exercised headlessly |

### 11.3 The blocker the package cannot fix

`BaseScreenPresenterCore`'s constructor requires `SignalBus` and `ILoggerManager` — GameFoundation
and UniT types. This is **not** fatal: `Packages/com.gdk.core/Tests/Runtime/TestDoubles.cs:155-160`
already constructs a UI Toolkit presenter with both, and that test assembly references
`GameFoundation.Signals`, `UniT.Logging`, `UniT.ResourceManagement` and `VContainer` directly.

What it *does* mean: **a screen author's test assembly must reference five GameFoundation/UniT
assemblies to construct one presenter**, and `com.cuvara.uitoolkit` can never ship the doubles
that would remove that (the `check_standalone.py` gate forbids it). So:

- The package ships the *view-side* and *lifecycle-side* doubles (the table above).
- `com.gdk.core` should promote its existing `Tests/Runtime/TestDoubles.cs` — `TestSceneScope`,
  `StubAssetsManager`, `ActivatorContainer` (`:176`) — out of the test assembly into a shipped
  `GameFoundation.TestSupport` assembly, and add `NullLoggerManager` alongside. Without that
  every screen author copies those doubles, which is exactly the duplication this section exists
  to prevent.
- The wizard-generated test file (§13) must emit the right asmdef references, or it will not
  compile on first run and the author will delete it. That detail decides whether generated tests
  survive.

---

## 12. Migration and sequencing (Q12)

Nothing below breaks the uGUI path. Every GameFoundation change is either in the UITK-only
assembly, or a new virtual whose default preserves today's behaviour.

| Stage | Where | Content | Risk to the six consumers |
|---|---|---|---|
| **0** | `com.cuvara.uitoolkit` | `ScreenSubscriptions`, `ScreenLifecycleState`, `Runtime/TestSupport/` + doubles. Pure addition; no GameFoundation reference. | none — they do not consume the package directly |
| **1** | `com.gdk.core` (`ScreenFlow`) | `virtual UniTask BindData(ScreenSubscriptions subs)` on `BaseScreenPresenterCore`, defaulting to `BindData()`. Base creates/clears the bag around open/close. | none — existing overrides keep compiling and keep being called |
| **2** | `com.gdk.core` (`ScreenFlow`) | Bug fixes: `finally` on `typeToPendingScreen`; additive `SetViewAsync`; `openGeneration`. | behaviour-preserving except that a previously-poisoned screen now retries |
| **3** | `com.gdk.core` (`ScreenFlow`) | `virtual void ConfigureScope(IContainerBuilder)`, default no-op; `ScreenManager` creates a child scope per presenter instance only when the presenter overrides it. Additive `DestroyScreen`, `ScreenOpen.Fresh`, `GetState`. | none — no override means no scope, exactly today's code path |
| **4** | `com.gdk.core` (`UIModuleUITK`) | Focus-the-root-on-open for back navigation; the `display:none` measurement note in the presenter base doc. | UITK-only assembly; uGUI untouched |
| **5** | **this project** | `GameLifetimeScope.Configure` gains `RegisterScreenManager()` + `RegisterUIToolkitViewBackend()`; `MainScene` gains a `RootUIDocument` GameObject with `RootUIDocument.uxml` assigned. | none — this project currently wires **neither** |
| **6** | `ViewCreatorWizard` | Emit the new `BindData(model, subs)` shape, a `.uss` file, and a presenter test file (§13). | wizard-only; nothing regenerates existing screens |

**Stage 5 is the surprising one.** The screen flow is not wired into this project at all today
(`Assets/Scripts/DI/GameLifetimeScope.cs` registers networking and Nakama and nothing else). That
is good news for sequencing: there are **no existing UXML screens in `Assets/` to migrate**, so
stages 0–4 can land and be tested in isolation, and the first real screen is written against the
finished design rather than being rewritten by it.

---

## 13. What is generated vs hand-written (Q9)

`ViewCreatorWizard` (`Packages/com.gdk.core/Editor/Tools/ViewCreatorWizard/`) already emits, from
`private const string` templates in `ViewCreatorTemplates.cs`, a single `.cs` holding model +
view + presenter, plus a `.uxml` for the UI Toolkit backend. It derives names mechanically
(`{Name}{Type}Model/View/Presenter`), derives the namespace from the folder, kebab-cases the UXML
root name, stamps `[ScreenInfo(nameof(View))]` and `[Preserve]`, and refuses to overwrite.

**What it should emit after this plan:**

1. `BindData(TModel model, ScreenSubscriptions subs)` instead of `BindData(TModel)`, and
   `subs.Clicked(this.View.BtnClose, this.CloseView)` instead of the current
   unregister-then-register plus `Dispose` override (`ViewCreatorTemplates.cs:343-356`). **This is
   the single highest-value change in the whole plan**: it deletes the one piece of lifecycle
   boilerplate every screen author currently has to type correctly, from the exact place they
   copy it from.
2. **A `.uss` file.** The wizard emits none today and puts inline `style="…"` in the UXML
   (`ViewCreatorTemplates.cs:420, 441-445`), which contradicts the contract's "prefer reusable
   classes over duplicated blocks". Emit `{Name}.uss`, reference it from the UXML, and move the
   inline styles into classes.
3. **A presenter test file** — `{Name}ScreenPresenterTests.cs` with the §11.1 skeleton, including
   the `subs.LiveCount == 0` assertion. Generated tests that assert leak-freedom are how the
   design stays true after the author has moved on.
4. An optional **"screen-scoped service"** checkbox emitting `I{Name}Service`/`{Name}Service` and
   the `ConfigureScope` override that registers them — off by default.
5. Nothing else. No DI registration file: `ScreenManager` instantiates presenters through the
   container by type, which is why there is no registration step in §2.1, and adding one would be
   a regression.

Hand-written, always: the UXML element structure, the USS beyond the scaffold, the ViewModel
shape, the service interface, and the body of `BindData`.

---

## 14. Where I think the architecture contract is wrong, or will hurt

Stated because I was asked for disagreement rather than agreement.

1. **"No service locators" is already violated by the incumbent, in the hottest path.**
   `ScreenManager.GetScreen` does `this.GetCurrentContainer().Instantiate(screenType)`
   (`ScreenManager.cs:363`), and `BaseScreenPresenterCore.UnloadViewAsset` does
   `this.GetCurrentContainer().Resolve<IAssetsManager>()` (`:123`). `DIExtensions.GetCurrentContainer()`
   is a service locator, and `CLAUDE.md` even documents it as the "service locator fallback".
   The contract should either carve this out explicitly ("the screen factory is allowed to
   resolve by `Type` because it is a factory") or the plan is quietly non-compliant on its first
   line. I recommend the carve-out: creating an object of a runtime-known `Type` genuinely cannot
   be constructor injection. But it should be *stated*, not left as a contradiction for the next
   reader to discover.

2. **"ONE navigation abstraction" is correct but under-specified about *hide*.** The single most
   confusing thing in this codebase is that `Dispose()` is called on close **and** on hide, and
   the object is then reused. `IDisposable` in C#, and specifically in VContainer, means end of
   life. Overloading it to mean "release this open cycle" is the root cause of the
   unregister-then-register pattern in the generated templates. I would rename it —
   `OnClosed()`/`ReleaseOpenCycle()` — with `Dispose()` kept as a `[Obsolete]`-flagged forwarder
   for the six consumers. The plan works around it rather than fixing it, because renaming is a
   coordinated multi-repo break, but the workaround is a cost the contract is imposing.

3. **"Presenters must be testable as plain C# with no scene" is not currently true and the
   package cannot make it true.** `BaseScreenPresenterCore` demands `SignalBus` and
   `ILoggerManager` in its constructor. Until `com.gdk.core` ships null doubles (§11.3), the
   contract's testability clause is aspirational for every UI Toolkit screen. This should be
   tracked as a defect against GameFoundation, not absorbed silently.

4. **`ScreenStatus` conflating "never loaded" with "closed"** makes several legitimate questions
   unanswerable — "is this screen loaded?", "is an open in flight?" — and is why §3 has to add a
   parallel enum instead of extending the existing one. Extending `ScreenStatus` would be
   cleaner and is a two-line change, but it is a public enum that six repos `switch` on, so
   adding members risks their `default:` branches. Additive parallel enum it is. Not elegant.

5. **Two attribute flags that do nothing.** `PopupInfoAttribute` declares `IsEnableBlur` and
   `IsCloseWhenTapOutside` (`ScreenInfoAttribute.cs:18-34`), and `ScreenManager` reads **only**
   `IsOverlay` (`:475-478`). An author who sets `isCloseWhenTapOutside: false` on a UI Toolkit
   popup gets no such behaviour and no warning. Either implement them on the UITK path — a
   tap-outside handler is four lines through `subs` — or mark them `[Obsolete]`. Silently inert
   API is worse than absent API because it looks configured.

6. **Minor, but it will bite someone:** the UI Toolkit presenter namespace is
   `GameFoundation.Scripts.UIModule.UITK.Presenter` while the folder is
   `Scripts/UIModuleUITK/Presenter/`. `CLAUDE.md` says namespace mirrors folder path. One of the
   two should move.

---

## 15. Open questions, and what would settle each

| Question | What would settle it |
|---|---|
| Does `EntryPointDispatcher.Dispatch()` on a per-screen child scope cost enough to matter, given `VCONTAINER_ECS_INTEGRATION` makes it resolve `ContainerLocal<IEnumerable<ComponentSystemBase>>` and sort world helpers every time? | A Profiler capture of 50 consecutive `CreateScope`+`Dispose` cycles in a build with Entities present. If it is non-trivial, `ConfigureScope` should avoid `RegisterEntryPoint` and the base should call presenter hooks directly. |
| Is the one-player-loop-point delay on `IStartable`/`IAsyncStartable` acceptable for anything in the screen path? | It is not, for open. Verified from source. Settled: use `IInitializable` (synchronous) or explicit calls. Recorded here so nobody re-proposes `IStartable`. |
| Does `NavigationCancelEvent` reach the panel root when nothing in the panel has focus? | A PlayMode test: open a screen with no focusable element, send a synthetic cancel, assert `BackNavigationSource.HandledCount`. This is the difference between "back works" and "back works only after you click something". |
| How much memory does a retained `display:none` screen actually hold? | Memory Profiler snapshot with 10 screens open-then-closed, comparing retained vs `Fresh`. Determines whether an LRU on `typeToLoadedScreenPresenter` is needed now or later. |
| Do the six other `com.gdk.core` consumers override `BindData()` in ways that a new overload would shadow confusingly? | Only answerable by grepping those repos, which are not on this machine. |
| Is `PanelSettings` cloned at runtime anywhere yet? | The brief warns that writing to it edits the shared project asset. Nothing in the package writes to it today; a screen that wants per-screen scaling would have to clone first. No implementation exists to check. |

---

## 16. What I could NOT verify

Listed explicitly, per the brief.

1. **The six other projects that consume the `com.gdk.core` fork.** They are not on this machine.
   Every claim in §12 about their risk is reasoning from the shape of the change (additive
   virtuals with behaviour-preserving defaults), not from reading their code. The one that would
   actually break them — renaming `Dispose()` — is the one I declined to propose for that reason.
2. **`com.gdk.3rd` (ThirdPartyServices).** Same: it is stated in existing doc comments to compile
   against `IScreenPresenter`, and I took that on trust.
3. **Runtime behaviour of any of this.** Nothing was run. No Unity Editor was opened, no test was
   executed, no build was made. Every Unity API claim is from the installed `.xml` doc files at
   `6000.3.9f1/Editor/Data/Managed/UnityEngine/`; every VContainer claim is from
   `Library/PackageCache/jp.hadashikick.vcontainer@19ee6e1cc8be/` source. Behavioural claims —
   the `display:none` layout consequences, the `NavigationCancelEvent` focus routing, the cost of
   per-scope entry-point dispatch — are **reasoned from those signatures and from the packages'
   own doc comments, not measured.**
4. **Whether `ScreenManager`'s `activeScreens` ordering behaves as a stack under
   `IsClosePrevious`, overlay popups, and `PopupBlurBgShowedSignal` in every combination.**
   `ScreenManager.cs` is 30.8 KB. The open/close/get paths, the signal wiring, the caching and
   the interface were read line by line (partly by a subagent whose findings are folded in
   above and cite line numbers); the full cross-product of orderings in
   `OnShowScreen`/`OnCloseScreen`/`OnOverlap` was not traced. Two behaviours in §5 are from the
   API surface and doc comments rather than from a trace: that a popup defers hiding the screen
   below until `PopupBlurBgShowedSignal` (`:627-630`), and that closing the top screen re-opens
   or re-shows the new last depending on its status (`:563-574`). Both should get a PlayMode
   test before anything depends on them.
5. **That the two defects in §10 reproduce.** The faulted-`typeToPendingScreen` entry is read off
   the control flow at `ScreenManager.cs:350-357`; the missing `UnloadViewAsset` on scene change
   is read off `CleanUpAllScreen` at `:449-464`. Both are well-known shapes and both are stated
   with line numbers, but **neither was reproduced.** Writing those two tests should be the first
   work in stage 2 — if either fails to reproduce, the corresponding fix comes out of the plan.

6. **That `RootUIDocument`'s `ClosedLayer` actually suppresses layout for descendants.** The UXML
   sets `display: none` on `root-ui-closed` and the CSS semantics say descendants are excluded
   from layout, but I did not measure it with the Profiler in this project. §7's whole cost
   argument rests on it. A `UIElementsUpdate` marker comparison with three screens retained vs
   destroyed would settle it in ten minutes.
