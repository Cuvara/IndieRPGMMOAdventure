# Runtime lifecycle for UXML screens — plan (v2)

Status: **plan only**. Nothing here is implemented. v1 written 2026-08-21; **v2 same day**,
after the premise changed.

---

## 0. Delta from v1 — read this first

The premise changed under v1: **GameFoundation is done. No further development there. The project
consumes `com.cuvara.uitoolkit` directly and all future UI work happens in the package.**

Two measured facts drive the rewrite:

1. The game wires GameFoundation **nowhere** — zero references in `Assets/` to
   `RegisterScreenManager`, `IScreenManager`, or any of it. This is **greenfield, not a port**.
2. The package has **no screen flow at all**. Present: `BaseUIToolkitView`, `UIToolkitViewFactory`,
   `RootUIDocument` + layers, three collection adapters, SafeArea/PanelScale, `BackNavigationSource`,
   the ECS bridge, `IPresenterInstantiator`, `IVisualTreeAssetLoader`. **Absent**: any manager,
   navigation, stack, modal handling, Open/Close/Hide flow, or presenter base *for a screen*
   (`BaseUIToolkitItemPresenter` is for rows inside a collection). The screen-side presenter bases,
   the view backend and the back-navigation policy all stayed in GameFoundation and are now orphaned.

| v1 section | Fate in v2 | Why |
|---|---|---|
| §1.1 "extend the incumbent, do not replace it" | **DELETED** | There is no incumbent. Replaced by §1 "the package owns the flow". |
| §1.2 "two nested lifetimes, because ScreenManager caches" | **REWRITTEN, conclusion changed** | The reasoning was *forced* by `typeToLoadedScreenPresenter`. Freed from it, I re-decided and **flipped the default to destroy-on-close** (§6). The two lifetimes survive but are now *scope* + *activation*, not *scope* + *open cycle*, and they are chosen, not inherited. |
| §2 developer-facing API | **REWRITTEN** | No `SignalBus`/`ILoggerManager` in a presenter constructor any more. Registration became explicit and AOT-safe. Step count re-derived. |
| §3 states / transitions | **KEPT, simplified** | The state machine was sound. It loses the "reopen from cache" edges that only existed because of the incumbent's caching, and gains Suspended/Resumed. |
| §4 async & cancellation | **KEPT, simplified** | The shared-load/`openGeneration` machinery existed to protect the incumbent's shared `typeToPendingScreen`. With per-open scopes the token story collapses to one linked CTS. The verified VContainer facts are unchanged and still load-bearing. |
| §5 stack & navigation API | **REWRITTEN** | Was "map onto `IScreenManager`". Now the package defines the API. Back-at-root is now settled explicitly (§5.4) rather than deferred. |
| §6 reuse vs recreate | **REWRITTEN, conclusion changed** | v1 said retain-by-default *because the incumbent did*. v2 says **destroy-by-default, retain opt-in**, and neutralises the cost by caching the `VisualTreeAsset` instead of the view. |
| §7 where the tree lives while hidden | **KEPT** | Still `display:none` via a layer. The v1 correction (hide must reparent) is now just how it is built. |
| §8 data binding | **KEPT VERBATIM** | Nothing about `SetBinding`/`dataSource` depended on the host. Conclusion stands. |
| §9 ECS path | **KEPT, improved** | The bridge is package-owned and unchanged. Sink registration becomes **automatic** (§9.2) — the author no longer writes it at all. |
| §10 failure modes | **REWRITTEN** | Two of the entries were defects in `ScreenManager`, now fixed on the fork and irrelevant here. Replaced with the failure modes of the *new* design. |
| §11 testability | **REWRITTEN, blocker removed** | v1's headline blocker — presenters need `SignalBus` + `ILoggerManager` — **is gone**. A package presenter's constructor takes only its own dependencies. |
| §12 migration | **MOSTLY MOOT, kept honestly** | See §12: nothing to unwind, nothing to sequence around six consumers. What survives is the *ordering within the package* and one real carry-over. |
| §13 wizard generation | **REWRITTEN** | The wizard lives in GameFoundation, which is frozen. §13 now says what that means. |
| §14 disagreements | **RESTRUCTURED, none dropped** | v1's six complaints all targeted GameFoundation. They are **not** moot: §14.1 recasts each as a "do not inherit this" design rule with a named enforcement point, which is worth more than a bug report against a frozen fork. §14.2 adds new ones about this package. |
| §15/§16 open questions / unverified | **KEPT AND EXTENDED** | |
| **NEW §17** | Naming constraints imposed by `check_standalone.py` | The gate bans the obvious names. This bites at implementation time and nowhere else, so it is written down. |

**What v1 got right and is preserved unchanged:** the verified VContainer facts (§0.1), the
`display:none` layer decision, the plain-setters-over-`SetBinding` argument, the ECS adapter
boundary, and the principle that the author never writes teardown code.

---

## 0.1 What was verified, and against what

Unchanged from v1 and still load-bearing. Verified against
`6000.3.9f1/Editor/Data/Managed/UnityEngine/UnityEngine.UIElementsModule.xml`:
`SetBinding(BindingId, Binding)`, `GetBinding`, `ClearBinding(s)`, `dataSource`,
`dataSourcePath`, `dataSourceType`, `BaseVerticalCollectionView.{virtualizationMethod,
fixedItemHeight, makeItem, bindItem, unbindItem, destroyItem, itemsSource, RefreshItem(int),
RefreshItems(), Rebuild()}`, `CollectionVirtualizationMethod.{FixedHeight,DynamicHeight}`,
`VisualElement.{panel, RemoveFromHierarchy}`, `AttachToPanelEvent`, `DetachFromPanelEvent`,
`PanelSettings.SetScreenToPanelSpaceFunction`, `UIDocument.sortingOrder`.
`VisualElement.materialOverride` is **absent**.

Verified against `Library/PackageCache/jp.hadashikick.vcontainer@19ee6e1cc8be/`:

- `ScopedContainerBuilder.BuildScope()` calls `EmitCallbacks(container)`
  (`ContainerBuilder.cs:36-43`) — **build callbacks run for a child scope**, so
  `RegisterEntryPoint` inside a `CreateScope` installation really does dispatch that scope's
  entry points. The whole design rests on this.
- `EnsureDispatcherRegistered` checks `Exists(..., includeInterfaceTypes:false)` with
  `findParentScopes` false (`ContainerBuilderUnityExtensions.cs:13-24`) — a child scope gets its
  **own** dispatcher.
- **`IInitializable.Initialize()` is synchronous inside `Dispatch()`**;
  `IStartable`/`IAsyncStartable` are queued to `PlayerLoopTiming.Startup`
  (`EntryPointDispatcher.cs:26-39, 58-64, 122-130`) — **one player-loop point later**.
  → **Never use `IStartable` for screen open logic.** Settled, recorded so it is not re-proposed.
- UniTask installed ⇒ `VCONTAINER_UNITASK_INTEGRATION` (`VContainer.asmdef:22-26`) ⇒
  `IAsyncStartable.StartAsync(CancellationToken)` returns **`UniTask`**
  (`Annotations/IAsyncStartable.cs:1-15`).
- `AsyncStartableLoopItem` `Forget()`s the task and **cancels its CTS on `Dispose()`**
  (`PlayerLoopItem.cs:337-346`); the dispatcher is `Lifetime.Scoped` + `IDisposable`, so
  **the token handed to `StartAsync` is scope-lifetime-bound and cancels on `scope.Dispose()`**.
- Entities installed ⇒ `VCONTAINER_ECS_INTEGRATION` ⇒ every `Dispatch()` also resolves
  `ContainerLocal<IEnumerable<ComponentSystemBase>>` and sorts world helpers
  (`EntryPointDispatcher.cs:132-140`). A per-screen-open cost. Open question in §15.

Verified in the package, this session: `check_standalone.py` runs clean over 39 files
(`scanned 39 files under Runtime, Tests / standalone: no host-framework references`), and the
`gdk-*` USS class names v1 reported are gone — they are `cuvara-grid-row`, `cuvara-grid-cell`,
`cuvara-multi-template-shell` today.

---

## 1. The decision

### 1.1 The package owns the flow. There is exactly one navigation abstraction, and it lives here.

`docs/UI-ARCHITECTURE.md` §Navigation says: *"One navigation abstraction, centralised … Do not
scatter `SetActive(true/false)` through gameplay code, and do not introduce a second navigation
system beside the one that already exists. Follow the existing API."*

**I agree with the re-brief's reading: this now permits package-owned navigation, and in fact
requires it.** The clause forbids *plurality*, not *location* — its stated harm is a second
system beside an existing one, and its stated remedy is "follow the existing API". With
GameFoundation frozen and unwired, the flow in `com.gdk.core` is not "the one that already
exists" in any operative sense: nothing in `Assets/` can reach it, and nothing ever will. Build
one in the package and the count is one. Build one in the package *while also wiring the
GameFoundation one* and the count is two — that is the thing to refuse, and §12 refuses it
explicitly.

One caveat I will not smooth over: the clause also says *"If an abstraction exists, reuse it
rather than building a parallel one."* The GameFoundation flow **does** exist as code. The
argument that it should not be reused is not "it is bad" — it is fine code — it is that the
project has decided not to develop it, the package may not reference it (§17), and a frozen
dependency that cannot be fixed is worse than no dependency. That is a **project decision I am
recording, not one I derived.** If the decision is reversed, §1 is the section to reopen.

### 1.2 Lifetimes — re-argued from scratch, conclusion changed

v1 concluded "two nested lifetimes" because `ScreenManager` caches presenters per type forever
and calls `Dispose()` on close while keeping the object alive. That constraint is gone. So:

**Chosen: a screen's lifetime IS a VContainer child scope, spanning open → close.**

```
nav.PushAsync<InventoryPresenter>()
    -> resolver.CreateScope(b => { register view, presenter, screen services })
    -> presenter opens
       ...
    -> nav.PopAsync()
    -> scope.Dispose()   // presenter, view, subscriptions, screen services, ECS sinks — all at once
```

One concept, one `Dispose`, nothing for the author to remember. This is the direction the
original brief gave; it was wrong against `ScreenManager` and is right here, and I want that
distinction on the record rather than looking like I flip-flopped.

**A second, narrower lifetime survives — but it is activation, not open/close.** A screen that
is *covered* by another (pushed over, or a modal on top) must keep its state and its scope, and
must **stop doing work**: no ECS pushes into a `display:none` tree, no service polling. So:

| Lifetime | Spans | Holds | Released by |
|---|---|---|---|
| **Screen scope** (`IScopedObjectResolver`) | `Push` → `Pop` | view, presenter, screen-scoped services, everything in `ScreenSubscriptions` | `scope.Dispose()` on pop |
| **Activation** | `Activate` → `Deactivate` (covered / uncovered) | ECS sink registrations, per-activation subscriptions | the base class, automatically (§9.2) |

The author writes code for neither. That is the point.

### 1.3 What I am NOT building

Stated so scope creep is visible: no transition/animation framework (the view's
`PlayIntroAnim`/`PlayOutroAnim` hooks stay empty by design), no theming system, no localisation
hook, no save/restore of screen state across sessions, no world-space panel support. The contract
also reserves world-space and combat UI for prefab/uGUI, and nothing here touches that path.

---

## 2. What a developer actually types

The convenience test. If this part is bad the rest does not matter.

### 2.1 Once per project

```csharp
public sealed class UILifetimeScope : LifetimeScope
{
    protected override void Configure(IContainerBuilder builder)
    {
        builder.RegisterUIToolkit();                              // existing: RootUIDocument + view factory
        builder.RegisterInstance<IVisualTreeAssetLoader>(myLoader);  // host decides what a key means
        builder.RegisterScreenFlow();                             // NEW: navigator, asset cache, back source

        // One line per screen. Generated (§13); hand-written only if you skip the generator.
        builder.RegisterScreen<InventoryPresenter, InventoryView>("Inventory");
        builder.RegisterPopup <ConfirmPresenter,   ConfirmView>  ("Confirm");
    }
}
```

### 2.2 Once per screen — three files

```
Assets/UI/Screens/Inventory/
    Inventory.uxml
    Inventory.uss
    InventoryScreen.cs          # ViewModel + IView + View + Presenter, one file
```

```csharp
namespace UI.Screens.Inventory
{
    using Cuvara.UIToolkit.Collections;
    using Cuvara.UIToolkit.Ecs;
    using Cuvara.UIToolkit.Flow;
    using Cuvara.UIToolkit.View;
    using Cysharp.Threading.Tasks;
    using System.Threading;
    using UnityEngine.UIElements;

    // ---- ViewModel: plain data. No VisualElement, no VisualTreeAsset, no UIDocument. ----
    public readonly struct InventoryRowVm
    {
        public readonly string Name; public readonly int Count;
        public InventoryRowVm(string n, int c) { this.Name = n; this.Count = c; }
    }

    public sealed class InventoryModel { public string CharacterId; }

    // ---- View: the ONLY layer that knows VisualElement. ----
    public interface IInventoryView : IUIToolkitView
    {
        ListView Items { get; }
        Button   Close { get; }
        void     RenderHeader(string title, int weightKg);
    }

    public sealed class InventoryView : BaseUIToolkitView, IInventoryView
    {
        private readonly Label title, weight;
        public ListView Items { get; }
        public Button   Close { get; }

        // The (VisualTreeAsset) ctor is the one UIToolkitViewFactory calls. Do not change it.
        public InventoryView(VisualTreeAsset asset) : base(asset)
        {
            this.StretchToParent();
            this.title  = this.Root.Q<Label>("title");
            this.weight = this.Root.Q<Label>("weight");
            this.Items  = this.Root.Q<ListView>("items");
            this.Close  = this.Root.Q<Button>("btn-close");
        }

        public void RenderHeader(string t, int kg) { this.title.text = t; this.weight.text = $"{kg} kg"; }
    }

    // ---- Presenter: plain C#. No UIDocument, no VisualElement, no framework base ctor. ----
    public sealed class InventoryPresenter
        : BaseUIToolkitScreenPresenter<IInventoryView, InventoryModel>,
          IViewModelSink<CarryWeightVm>                       // opt-in: ECS pushes, auto-bound (§9.2)
    {
        private readonly IInventoryService inventory;
        private readonly VisualTreeAsset   rowTemplate;

        // Pure constructor injection. Nothing framework-shaped is forced in here.
        public InventoryPresenter(IInventoryService inventory, [ScreenAsset("InventoryRow")] VisualTreeAsset rowTemplate)
        {
            this.inventory   = inventory;
            this.rowTemplate = rowTemplate;
        }

        // The ONLY bind entry point. `subs` and `ct` are required parameters, so they
        // cannot be forgotten, and there is no other overload to reach for.
        protected override async UniTask OnBindAsync(InventoryModel model, ScreenSubscriptions subs, CancellationToken ct)
        {
            subs.Clicked(this.View.Close, () => this.Nav.PopAsync().Forget());

            var rows = new UIToolkitListAdapter<InventoryRowVm, InventoryRowView, InventoryRowPresenter>(
                this.View.Items, this.rowTemplate, itemHeight: 64f);
            subs.Add(rows);                                        // IDisposable -> released with the scope

            subs.Add(this.inventory.Changed.Subscribe(_ => Refresh().Forget()));
            await Refresh();

            async UniTask Refresh()
            {
                var page = await this.inventory.GetItemsAsync(model.CharacterId, ct);
                this.View.RenderHeader("Inventory", page.TotalWeight);
                rows.SetItems(page.Rows);                          // virtualized; no rebuild storm
            }
        }

        // ECS -> adapter -> ViewModel -> here -> View. Main thread, change-driven.
        // Registered when this screen activates, unregistered when it deactivates. Not by you.
        public void Push(in CarryWeightVm vm) => this.View.RenderHeader("Inventory", vm.Kg);
    }
}
```

### 2.3 Opening it

```csharp
await this.nav.PushAsync<InventoryPresenter, InventoryModel>(new InventoryModel { CharacterId = id });
```

### 2.4 Step count

| # | Step | Hand-typed? |
|---|---|---|
| 1 | Run the generator → `Inventory.uxml`, `Inventory.uss`, `InventoryScreen.cs`, `InventoryPresenterTests.cs` | no |
| 2 | Give the UXML its loader key (Addressables address, or a dictionary entry) | one string |
| 3 | `builder.RegisterScreen<InventoryPresenter, InventoryView>("Inventory");` | **generated** into a partial (§13); one line if hand-written |
| 4 | Write the body of `OnBindAsync` — the actual screen | yes, this is the work |
| 5 | `await nav.PushAsync<InventoryPresenter, InventoryModel>(model);` | one line |

**Four steps plus the screen's own logic**, and of those four only step 2 and step 5 are
genuinely typed. **Zero lifecycle code**: no `Dispose` override, no `UnregisterCallback`, no
`CancellationTokenSource`, no scope handling, no ECS sink registration.

**The one step v1 did not have is step 3**, and I am adding it deliberately. v1 got zero
registration by having `ScreenManager` do `container.Instantiate(screenType)` off a
`[ScreenInfo]` attribute — reflection over a runtime `Type`. Three reasons to pay one line
instead:

1. **AOT.** Android and WebGL are IL2CPP with **Minimal** managed stripping
   (`IndieRPGMMOAdventure/CLAUDE.md`). Reflective construction needs `[Preserve]` on every
   generated ctor and care with `link.xml` — and per that same file, a StandaloneWindows64 build
   exercises **neither IL2CPP nor the stripper**, so the quickest build **cannot validate it**
   and a green result there is misleading. An explicit generic registration is checked by the
   compiler and needs none of that.
2. **It is real constructor injection.** `RegisterScreen<TPresenter,TView>` registers the
   presenter type, so VContainer builds it with its actual dependencies. `Instantiate(Type)` off
   a service locator is what the contract's "no service locators" clause is about, and v1 had to
   argue for a carve-out. v2 does not need one.
3. **It is greppable.** "Which screens exist?" is answered by reading one file.

If step 3 ever feels like a cost, the generator writing it (§13) reduces it to zero typing while
keeping all three properties.

---

## 3. Lifecycle states and legal transitions

Simplified from v1: the "reopen from cache" edges are gone with retain-by-default (§6), and
Suspended/Resumed replaces them.

```
        Unregistered ──(RegisterScreen at container build)──► Registered
                                                                  │
                                              nav.PushAsync<T>()  │
                                                                  ▼
                            ┌──────────── Creating ────────────────┐
                            │  CreateScope · load VisualTreeAsset  │ ASYNC · MAY FAIL
                            │  · CloneTree · construct view        │
                            │  · resolve presenter                 │
                            └──────────────┬───────────────────────┘
                                           │ fail ► scope.Dispose(); rethrow; stack unchanged
                                           ▼
                                      Constructed
                                           │  OnBindAsync(model, subs, ct)   ASYNC · MAY FAIL
                                           ▼
                                       Binding ──fail──► scope.Dispose(); rethrow
                                           │
                                           ▼
                                       Opening  (parent into ShowLayer · PlayIntroAnim)
                                           │
                                           ▼
                    ┌──────────────────► Active ◄──────────────────┐
                    │                    │    │                    │
        Resume (uncovered)      Deactivate    Pop / Replace     Suspend
                    │            (covered)    │                    │
                    │                 │       │                    │
                    └───────────── Suspended ─┴────────────────────┘
                       (in HiddenLayer, display:none, sinks unbound,
                        scope ALIVE, state retained)
                                     │
                                     ▼  Pop / Replace / CloseAll
                                  Closing   (PlayOutroAnim · detach)
                                     │
                                     ▼
                                  Disposed   (scope.Dispose(); presenter, view,
                                              subs, screen services all released)
```

| Transition | Where | May fail? | On failure |
|---|---|---|---|
| `Registered → Creating` | `ScreenNavigator.PushAsync`, main thread, awaited | **yes** — missing key, no `(VisualTreeAsset)` ctor, unresolvable presenter dependency, no `RootUIDocument` | dispose the half-built scope, leave the stack untouched, rethrow to the caller |
| `Creating → Constructed` | `UIToolkitViewFactory.Create` | no — `CloneTree` is synchronous, so there is no readiness gap and no `IsReadyToUse` flag | — |
| `Constructed → Binding → Opening` | `OnBindAsync` | **yes** — it calls services | dispose the scope, stack untouched, rethrow. **A failed open leaves no half-open screen.** |
| `Opening → Active` | `View.Open()` + intro | should not; a tween library can | force alpha 1 in a `finally`, log, land in `Active` |
| `Active ⇄ Suspended` | navigator, **synchronous** | no | — |
| `Active/Suspended → Closing → Disposed` | `PopAsync`, awaited, runs outro | should not | force detach in a `finally`; **never** abort teardown |

**Suspend vs Close, plainly** — v1 called these Hide and Close and the distinction survives:

| | Suspend | Close |
|---|---|---|
| Synchronous | yes | no — awaits the outro |
| Stays on the stack | **yes** — covered, not removed | no |
| Scope | **alive** | disposed |
| ECS sinks / activation subscriptions | unbound | released with the scope |
| Visual tree | retained, reparented to `HiddenLayer` (`display:none`) | detached and dropped |
| Resume path | `Resume()` — no rebind unless the presenter opts in | n/a — a new `Push` builds a new instance |

**`Suspend` reparents.** v1 found that the incumbent's `HideView` did *not*, leaving a covered
screen laid out and drawn at opacity 0. Building it fresh means simply doing it right: `Suspend`
is `View.Hide()` then `SetViewParent(HiddenLayer)`; `Resume` is the reverse, in that order, so a
future fade hook still has a laid-out tree to animate.

---

## 4. Async, honestly

### 4.1 Cancellation — one token, one owner

Per-open scopes collapse v1's three-token story to two, and the developer sees one.

| Token | Lifetime | Source | Cancelled by |
|---|---|---|---|
| Scene token | the scene scope | `IAsyncStartable.StartAsync(ct)` on a scene entry point — **verified** cancelled on `scope.Dispose()` (`PlayerLoopItem.cs:337-346`) | scene unload |
| **Screen token** (`ct` in `OnBindAsync`) | `Push` → `Pop` | a linked CTS created by the navigator when the screen scope is created, linked to the scene token | `scope.Dispose()` on pop, and on scene unload through the link |

The base class owns the CTS and disposes it with the scope. **The author never constructs one
and never cancels one** — they receive `ct` and pass it to every `await`.

### 4.2 Closed while still loading

With per-open scopes there is no shared load task to protect, so v1's `openGeneration` counter is
gone. The load is simply **cancellable**, because it belongs to exactly one screen:

```csharp
var asset = await this.assetCache.LoadAsync(key, ct);   // ct is the screen token
ct.ThrowIfCancellationRequested();
```

`PopAsync` on a screen still in `Creating` cancels the token, the load unwinds through
`OperationCanceledException`, the navigator catches **that specific exception**, disposes the
scope, and leaves the stack as it was. A cancelled open is not an error and must not be logged
as one — that distinction is the difference between a clean log and a log nobody reads.

### 4.3 Opened twice in the same frame

Two `PushAsync<InventoryPresenter>()` in one frame now means **two instances**, which is almost
never wanted. The navigator serialises pushes through a single in-flight queue:

```csharp
// ScreenNavigator holds one operation at a time. A push arriving while another is in flight
// is queued, not interleaved — the stack is a stack, and two concurrent mutations of it
// produce an order nobody can reason about.
private UniTask pending = UniTask.CompletedTask;
```

Plus a declared policy per screen: `ScreenOptions.Single` (default) makes a push of a type
already on the stack **bring that instance forward and rebind it** rather than create a second;
`ScreenOptions.Multiple` allows genuine duplicates (a chain of item-detail popups). This is the
one place v1's behaviour was surprising-and-undocumented; here it is a named enum value.

### 4.4 No `async void`, anywhere

v1's §4.4 was about `BaseScreenPresenterCore.SetView` being `async void` — unobservable
exceptions on the critical path. Building fresh, the rule is simply: **every lifecycle method
returns `UniTask` or `void`-and-is-synchronous. No `async void` in `Runtime/Flow/`.** Worth a
review checklist line, because `async void` compiles silently and is easy to reach for when an
event handler needs an await.

---

## 5. The stack, the navigation API, and Back

### 5.1 The API, as it would be typed

```csharp
namespace Cuvara.UIToolkit.Flow
{
    // NOT "IScreenManager" — check_standalone.py bans that identifier. See §17.
    public interface IScreenNavigator
    {
        ScreenLifecycleState StateOf<TPresenter>() where TPresenter : IUIToolkitScreenPresenter;
        int  Depth        { get; }
        bool IsBusy       { get; }                        // an operation is in flight
        IUIToolkitScreenPresenter Top { get; }

        UniTask<TPresenter> PushAsync<TPresenter>()                                  where TPresenter : IUIToolkitScreenPresenter;
        UniTask<TPresenter> PushAsync<TPresenter, TModel>(TModel model)              where TPresenter : IUIToolkitScreenPresenter<TModel>;
        UniTask<TPresenter> ReplaceAsync<TPresenter>()                               where TPresenter : IUIToolkitScreenPresenter;
        UniTask<TPresenter> ReplaceAsync<TPresenter, TModel>(TModel model)           where TPresenter : IUIToolkitScreenPresenter<TModel>;
        UniTask<TPresenter> ShowModalAsync<TPresenter, TModel>(TModel model)         where TPresenter : IUIToolkitScreenPresenter<TModel>;

        UniTask PopAsync();
        UniTask PopToRootAsync();
        UniTask PopAllAsync();

        // Back policy — explicit, not emergent. See §5.4.
        RootBackPolicy RootBackPolicy { get; set; }
        event Action   RootBackRequested;

        event Action<IUIToolkitScreenPresenter> ScreenActivated;
        event Action<IUIToolkitScreenPresenter> ScreenDeactivated;
    }
}
```

Plain C# events rather than a signal bus, matching the package's existing choice
(`IUIToolkitView`'s three events) and its stated reason: a bus would be a dependency, and a host
that has one forwards these in one line.

### 5.2 The stack

A real `List<ScreenEntry>` used as a stack, where `ScreenEntry` is `{ scope, presenter, options }`.

| Operation | Effect |
|---|---|
| `PushAsync` | build + open on top; the previous top is **suspended** (not closed) |
| `PushAsync` on a `Modal` | previous top is suspended **only if the modal is opaque**; a `Modal` with `DimsBelow` leaves the screen below `Active` but non-interactive (`pickingMode = Ignore` on the layer below) |
| `PopAsync` | close + dispose the top; **resume** the new top |
| `ReplaceAsync` | close + dispose the top, then push — the screen below is never resumed, so it never flashes |
| `PopToRootAsync` | pop everything above index 0 |
| `PopAllAsync` | pop everything; `Depth` becomes 0 |

Modals go into `RootUIDocument.OverlayLayer`, screens into `ShowLayer`, suspended into
`ClosedLayer`. All three already exist.

### 5.3 What "modal" means here

Declared at registration, not guessed from an attribute:

```csharp
builder.RegisterPopup<ConfirmPresenter, ConfirmView>("Confirm",
    ScreenOptions.Modal | ScreenOptions.DimsBelow | ScreenOptions.CloseOnTapOutside);
```

`CloseOnTapOutside` is **implemented**, not merely declared.

The precedent, measured rather than recalled: GameFoundation's `PopupInfoAttribute` declares
three flags. `IsEnableBlur` and `IsCloseWhenTapOutside` each appear in **exactly one file — their
own declaration — and `ScreenManager` reads them zero times**. `IsOverlay` appears in two files
and is read seven times. So two of three declared popup options did nothing and warned about
nothing; an author setting `isCloseWhenTapOutside: false` got no behaviour and no diagnostic.

> **Rule for this package: a flag that nothing reads does not ship.** Each `ScreenOptions` member
> gets a test that fails if its behaviour is removed. Silently inert API is worse than absent API,
> because it looks configured.

### 5.4 Back, and what it means at the root — settled

**The defect, confirmed by reading `Runtime/Input/BackNavigationSource.cs`.** `OnNavigationCancel`
returns early if `BackRequested == null`, then — with any subscriber at all — increments
`HandledCount`, calls `evt.StopPropagation()`, and invokes. **Consumption is decided by
"is anyone listening", not by "did anyone act".** Previously *not-consumed* and *did-nothing*
were the same case; they no longer are. On Android that means the system Back button stops
exiting the app once a subscriber exists and nothing is open — the press is swallowed and the
app just sits there. No existing test catches it because the assertions are on the policy's
counter, not on the source's propagation.

**Fix — make "handled" a return value, so the two cases are one again by construction:**

```csharp
// BackNavigationSource — BREAKING CHANGE to a 0.1.0 package with one known consumer.
/// <summary>Handlers return true if they consumed the press. First true wins.</summary>
public event Func<bool> BackRequested;

private void OnNavigationCancel(NavigationCancelEvent evt)
{
    if (this.disposed || !this.Enabled) return;
    if (this.BackRequested == null) return;

    var handled = false;
    foreach (Func<bool> handler in this.BackRequested.GetInvocationList())
    {
        if (handler()) { handled = true; break; }
    }

    if (!handled) return;                  // <-- the fix: not-handled means not-consumed
    ++this.HandledCount;
    if (this.ConsumeEvent) evt.StopPropagation();
}
```

`HandledCount` now counts presses that were *acted on*, which is what its name always claimed
and what a test should assert. A companion `SeenCount` for presses observed-but-not-handled makes
the two testable apart.

**And the policy, visible in the API rather than emergent:**

```csharp
public enum RootBackPolicy
{
    /// Report NOT handled. The event keeps propagating and Android's default Back
    /// (exit the app) still runs. THE DEFAULT, because it is the platform behaviour a
    /// user expects and the only one that cannot strand them.
    NotHandled = 0,
    /// Consume and do nothing. For a screen the player must not leave — a mid-match HUD.
    Consume,
    /// Consume and raise RootBackRequested. The app shows a quit dialog, or whatever it likes.
    Raise,
}
```

The navigator's handler is then the whole of the policy, and it is four lines:

```csharp
private bool OnBackRequested()
{
    if (this.Depth > 1)  { this.PopAsync().Forget(); return true; }
    if (this.Depth == 1 || this.RootBackPolicy == RootBackPolicy.NotHandled) { /* fall through */ }
    switch (this.RootBackPolicy)
    {
        case RootBackPolicy.Consume: return true;
        case RootBackPolicy.Raise:   this.RootBackRequested?.Invoke(); return true;
        default:                     return false;      // NotHandled -> Android exits
    }
}
```

**Two caveats that will generate bug reports if they are not designed for now.**

1. `NavigationCancelEvent` is routed by the panel's **focus controller**. With nothing focused in
   the panel, whether it reaches the root is Unity's dispatch behaviour and not something the
   source can guarantee — `BackNavigationSource`'s own doc comment says so. **Mitigation: the
   navigator focuses the top screen's root on activate.** One line, and it is the difference
   between "Back works" and "Back works only after you tap something".
2. `IsBusy`. A Back press during a push/pop animation must not start a second operation. The
   handler returns `true` (consumed, deliberately) while `IsBusy`, so a double-tap does not pop
   two screens. That is a real decision, not an oversight, and it should be tested.

---

## 6. Instance reuse vs recreate — re-decided

**v1 said retain-by-default. v2 says destroy-by-default. This is the biggest conclusion change
in the rewrite.**

v1's reasoning was: the incumbent already retains, so changing it is a behaviour break for six
consumers. That reason is gone — there are no consumers and no behaviour to preserve. Deciding
fresh:

### Why destroy-on-close is the right default

1. **It makes the model one concept instead of two.** Scope == screen lifetime == open→close.
   `scope.Dispose()` is the entire teardown story. With retention, the author has to hold "the
   presenter survives close, so `Dispose` does not mean what it means everywhere else in C#" —
   which is precisely the confusion that produced GameFoundation's unregister-then-register
   pattern in every generated template. **The convenience argument and the correctness argument
   point the same way here**, which is rare enough to be worth acting on.
2. **It deletes the stale-data class of bug.** A retained character-select screen shows the
   previous account's characters after logout until something rebinds it. There is no version of
   that bug in a design that rebuilds.
3. **`OnBindAsync` runs exactly once per instance**, so it cannot double-register anything, so
   the whole failure mode disappears rather than being defended against.

### What it costs, and why the cost is small

Per open, a rebuilt screen pays: asset load + `CloneTree` + first-layout style resolution.
**The expensive one is the load, and it is the one we keep.**

```csharp
// ScreenAssetCache — caches the VisualTreeAsset, not the view.
// CloneTree over an in-memory asset is cheap; the Addressables round trip is not.
public interface IScreenAssetCache
{
    UniTask<VisualTreeAsset> LoadAsync(string key, CancellationToken ct);
    void Release(string key);          // refcounted; call on scene teardown, not on pop
}
```

So closing a screen drops the tree and keeps the asset. Reopening is a `CloneTree` plus one
layout pass, not a disk or bundle hit. **This is what makes destroy-by-default affordable**, and
it is the piece v1 did not have.

### The escape hatch, and what a developer types

```csharp
// Default — destroyed on pop, rebuilt on next push.
builder.RegisterScreen<InventoryPresenter, InventoryView>("Inventory");

// Opt-in retention: the scope survives pop and the instance is reused.
builder.RegisterScreen<WorldMapPresenter, WorldMapView>("WorldMap", ScreenOptions.Retain);
```

`Retain` is for screens where rebuilding is genuinely visible: a very large tree, or a screen
holding expensive derived state (a rendered minimap texture). It buys latency with memory and
with the stale-data hazard, so it is a per-screen decision made in one greppable place, and a
retained screen's `OnBindAsync` **does** re-run on each push — with `subs` cleared in between, so
the double-registration hazard is still structurally impossible.

**Scene teardown** disposes every scope and releases every cached asset. That is one call the
host makes on scene unload, and it is the fix for the leak v1 found in the incumbent
(`CleanUpAllScreen` never unloaded anything).

---

## 7. Where the visual tree lives while suspended

Unchanged from v1 in decision; unchanged in reasoning; now simply built correctly from the start.

| Approach | Layout while hidden | Draw | Retains state | Re-show cost |
|---|---|---|---|---|
| `RemoveFromHierarchy()` | zero | zero | tree yes, but `panel` becomes null → `schedule` stops, focus lost | full re-attach + style resolve + layout |
| `visibility: hidden` in place | **full layout still runs** | zero | yes | ~free |
| **`display: none` via the layer** | **zero** | zero | yes | one layout pass on that subtree |

**Decision: reparent into `ClosedLayer`**, whose UXML already carries `display: none`
(`Runtime/Managers/RootUIDocument.uxml`, element `root-ui-closed`).

**The trap, which must be in the presenter base's XML doc:** under a `display: none` ancestor,
descendants have no resolved layout — `resolvedStyle.width`, `element.layout`, `worldBound` are
zero or stale until one layout pass after resume. Therefore:

- Never measure in `OnBindAsync`. Measure in a one-shot `GeometryChangedEvent` handler, and the
  base should ship the affordance so nobody hand-rolls it: `subs.OnFirstGeometry(el, cb)`.
- `ListView` with `CollectionVirtualizationMethod.FixedHeight` is safe (heights are declared).
  `DynamicHeight` measures, so a list bound while suspended computes a wrong viewport.
  **Prefer `FixedHeight` for screens**; if `DynamicHeight` is genuinely needed, call
  `RefreshItems()` from the first `GeometryChangedEvent` after resume.

`BaseUIToolkitView.Hide()/Show()` toggle opacity + `pickingMode`, not `display`. That is right for
a view that must stay laid out (a fading popup) and wasteful for a suspended screen — but the
ancestor's `display:none` short-circuits it, so the two compose. Keep both; document the division.

---

## 8. Data binding — unchanged from v1

**Default: plain setters. `SetBinding`/`dataSource`: not in v1 of the flow.**

All of `SetBinding(BindingId, Binding)`, `dataSource`, `dataSourcePath`, `dataSourceType` are
present in 6000.3.9f1 (verified). The argument against is not availability:

- The contract requires the Presenter to transform data into a ViewModel and the View to render
  it, and requires the Presenter to be testable with a **mocked view interface and no
  `VisualElement`**. A `dataSource` binding is resolved by the panel, so asserting that a binding
  produced the right text requires a live panel — exactly what the testability clause rules out.
  `view.RenderHeader(title, kg)` is asserted on a mock in one line.
- `dataSourcePath` in UXML puts property-path knowledge into the UXML file. The contract says
  UXML is "structure only … and the names/classes the View queries". A path string is a data
  contract, not structure, and it is not compiler-checked — a renamed ViewModel field fails
  silently at runtime instead of at build.
- The View class stops being the single place that knows how the screen is populated, which is
  the layering the contract exists to protect.

**When to revisit:** a row template with eight-plus fields, bound thousands of times, where the
per-bind setter calls show up in a profile. We are nowhere near that. Revisit with a capture,
not an opinion.

| Moment | Path |
|---|---|
| open | `PushAsync` → `OnBindAsync(model, subs, ct)` → `service.GetAsync(ct)` → `view.Render(vm)` |
| service-driven change | `subs.Add(service.Changed.Subscribe(...))` → recompute → `view.Render(vm)` |
| ECS-driven change | bridge → `IViewModelSink.Push(vm)` → `view.Render(vm)` (§9) |
| list, partial | `adapter.RefreshItems()` |
| list, source identity changed | `adapter.SetItems(rows)` |
| **never** | per frame. `ITickable` on a presenter is a design smell; say so in review. |

---

## 9. The ECS path

### 9.1 The adapter, unchanged

The package already ships it and it is correct: `EcsViewModelBridge<TComponent, TViewModel>` —
`SystemBase` (not `ISystem`: it holds a managed `List` of sinks and calls across an interface),
`[UpdateInGroup(typeof(PresentationSystemGroup))]`, `SetChangedVersionFilter` so untouched chunks
are skipped, `Enabled = sinks.Count > 0` so a world with no screen open pays nothing, and the
catch-up pass added in `468d520` so a sink registering into an idle world still sees current
state. `IViewModelSink<T>.Push(in T)` is the boundary and it stops at the ViewModel.

Two rules that are not negotiable and should be review-checked: nothing in
`Cuvara.UIToolkit.Ecs` may name a `VisualElement` (the assembly does not reference UIElements —
keep it that way), and a ViewModel must be a plain value, preferably a `readonly struct`, since
`Push` takes it by `in`.

### 9.2 Registration becomes automatic — improved from v1

v1 had the author write `subs.Add(EcsSinkRegistration.Bind(bridge, this))` in `BindData`, and
argued it belonged in the open-cycle bag rather than the scope so that a closed-but-retained
screen stops receiving pushes.

**v2 removes the line entirely.** The presenter base detects sink implementation and binds on
**activate**, unbinds on **deactivate**:

```csharp
// BaseUIToolkitScreenPresenter — pseudocode, runs in the base, never in a screen
protected internal void OnActivate()
{
    foreach (var sinkInterface in this.GetType().GetInterfaces()
                                     .Where(i => i.IsGenericType &&
                                                 i.GetGenericTypeDefinition() == typeof(IViewModelSink<>)))
    {
        this.activeSinks.Add(this.bridges.Bind(sinkInterface, this));   // resolved from the scope
    }
}
protected internal void OnDeactivate() { this.activeSinks.Dispose(); }   // clears
```

Consequences, all of them good:
- The author writes `public void Push(in CarryWeightVm vm) => …` and **nothing else**. There is
  no registration call to forget.
- Binding on *activate*, not on open, means a **suspended** screen stops receiving pushes — the
  invisible-work problem v1 identified, now solved structurally instead of by convention.
- `scope.Dispose()` is the backstop if a screen is destroyed while active.

The reflection here is once per presenter type and cacheable, and it is over the presenter's own
interfaces rather than over a container — it is not a service locator. It does need `[Preserve]`
consideration under IL2CPP; §15 records that as a thing to verify on an Android build, because
per `CLAUDE.md` a Windows build cannot validate it.

### 9.3 How the presenter gets the bridge

Systems live in the `World`; DI objects live in a scope. Two options; take the second.

```csharp
// A. World lookup — works, but it is a locator, and the contract dislikes those.
world.GetExistingSystemManaged<CarryWeightBridge>()

// B. Register the system instance at bootstrap. Constructor injection, honest, testable.
builder.RegisterInstance(World.DefaultGameObjectInjectionWorld.GetOrCreateSystemManaged<CarryWeightBridge>());
```

One line per bridge at bootstrap turns every presenter's ECS dependency into an ordinary
constructor parameter a test can substitute.

---

## 10. Failure modes, and what structurally prevents each

v1's table had two entries that were `ScreenManager` defects; both are fixed on the fork
(`a3b4e257`) and neither can recur here because that code is not in the path. Replaced with the
failure modes this design actually has.

| Failure | What prevents it (structure, not discipline) |
|---|---|
| **Leaked event subscription** | `OnBindAsync(model, subs, ct)` — `subs` is a **required parameter** and there is no other bind entry point. Everything registered through it is released by `scope.Dispose()`. With destroy-by-default, `OnBindAsync` runs once per instance, so double-registration is not merely defended against, it is unreachable. |
| **Presenter outliving its view** | Both are `Lifetime.Scoped` in the same scope, created together and disposed together. There is no code path that disposes one and not the other. `View` is nulled in teardown and every base access goes through a `RequireView()` that throws naming the screen key. |
| **Double-open / two instances of one screen** | The navigator serialises operations (`IsBusy`), and `ScreenOptions.Single` (the default) brings an existing instance forward instead of creating a second. `Multiple` is the opt-in. |
| **Screen referencing a disposed scope** | Everything is **constructor-injected**, so there is no resolver field to use later. The navigator holds the scope, not the presenter. If someone does hold one, VContainer's own `ObjectDisposedException` names it. |
| **ECS sink surviving its screen** | The author never registers it (§9.2). Bind on activate, unbind on deactivate, scope disposal as backstop. `EcsViewModelBridge` sets `Enabled = false` at zero sinks, so even a bug degrades to "system idle". |
| **Work continuing in a covered screen** | Suspend unbinds sinks *and* reparents into `display:none`, so both the push path and the layout path stop. |
| **A cancelled open logged as an error** | `PopAsync` during `Creating` cancels the token; the navigator catches `OperationCanceledException` **specifically** and disposes quietly. Any other exception is logged and rethrown. |
| **A failed open leaving a half-open screen** | The scope is built *before* anything is shown, and any failure before `Active` disposes it and leaves the stack byte-identical. There is no state to unwind because nothing was committed. |
| **Back swallowed at the root (Android cannot exit)** | `BackRequested` is `Func<bool>`; not-handled means not-consumed, so the platform default runs (§5.4). Default policy is `NotHandled`. |
| **`async void` swallowing an exception** | Banned in `Runtime/Flow/` by review rule; every lifecycle member returns `UniTask` or is synchronous. |
| **Asset handles leaked on scene change** | `IScreenAssetCache` is refcounted and released on scene teardown, which is one host call. This is the exact defect v1 found in the incumbent, designed out rather than fixed later. |

---

## 11. Testability — v1's blocker is gone

### 11.1 The blocker, and its removal

v1's §11.3 said: constructing a presenter needs `SignalBus` + `ILoggerManager`, so a screen
author's test assembly must reference five GameFoundation/UniT assemblies, and the package can
never ship doubles for them because `check_standalone.py` forbids it.

**That evaporates.** A package presenter's constructor takes only its own dependencies:

```csharp
public InventoryPresenter(IInventoryService inventory, VisualTreeAsset rowTemplate)
```

No framework base-class constructor arguments at all. This is, on its own, a strong argument for
the premise change, and it is worth stating because it was the single worst thing about v1.

**Design rule that keeps it true:** `BaseUIToolkitScreenPresenter` has a **parameterless
protected constructor**. Anything the base needs — the navigator, the bridge locator, the view —
is set by the flow through internal setters after construction, not demanded through the ctor.
The moment the base takes a constructor parameter, every screen's ctor and every screen's test
grows it. That is a one-line decision with a large blast radius; make it deliberately.

### 11.2 A presenter test, no scene, no `UIDocument`, no `VisualElement`

```csharp
[Test]
public async Task OnBind_RendersEveryRow_AndLeaksNothing()
{
    var view = new FakeInventoryView();          // implements IInventoryView; no VisualElement
    var svc  = new FakeInventoryService(rows: 3);
    var subs = new ScreenSubscriptions();
    var sut  = new InventoryPresenter(svc, rowTemplate: null);

    sut.AttachForTest(view);                     // TestSupport; sets the internal view field
    await sut.BindForTest(new InventoryModel { CharacterId = "c1" }, subs, CancellationToken.None);

    Assert.AreEqual(3, view.LastRows.Count);
    Assert.AreEqual("Inventory", view.LastTitle);

    subs.Dispose();
    Assert.AreEqual(0, subs.LiveCount, "OnBindAsync leaked a subscription");
}
```

`subs.LiveCount` after disposal is what makes the leak class **testable rather than merely
discouraged**. Every generated test file includes that assertion.

### 11.3 What the package SHIPS so nobody writes their own

A new `Runtime/TestSupport/` with its own asmdef (`Cuvara.UIToolkit.TestSupport`), shipped, and
referenced by consumers' test asmdefs. Nothing in it may name a host symbol (§17).

| Double | Purpose |
|---|---|
| `FakeVisualTreeAssetLoader` | dictionary-backed; `FailFor(key)` and `DelayFrames(key, n)` so **cancel-during-load is testable** |
| `RecordingScreenSubscriptions` | `LiveCount`, and asserts every registration was released |
| `FakeViewLayer` / `RecordingViewSurface` | assert parenting with no panel |
| `SpyViewModelSink<T>` | records `Push` values and ordering |
| `ScreenLifecycleRecorder` | records the state sequence; `AssertLegalSequence()` |
| `TestNavigatorHost` | a real minimal VContainer root + navigator, headless, so push/pop/suspend/resume and **scope disposal** are exercised without a scene |
| `FakeBackSource` | raises `BackRequested` and **reports whether it was consumed**, so §5.4 is testable |

`TestNavigatorHost` and `FakeBackSource` are the two that matter most, because they cover the two
things v1 could only reason about: that a scope really is disposed on pop, and that an unhandled
Back really does propagate.

---

## 12. Sequencing — what survives of v1's migration section

**Most of v1 §12 is moot and I am saying so rather than deleting it quietly.** It sequenced
changes across `com.gdk.core` and six consuming repos with behaviour-preserving defaults. There
is nothing to preserve: the game wires GameFoundation nowhere, and there are no UXML screens in
`Assets/` to port. This is greenfield.

**What survives, and it is not nothing:**

1. **The "do not end up with two" rule.** The contract permits package-owned navigation because
   the count will be one (§1.1). It stops permitting it the moment someone also calls
   `RegisterScreenManager()`. That should be an explicit project decision recorded in
   `docs/UI-ARCHITECTURE.md`, not left implicit: *GameFoundation's ScreenFlow is not used in this
   project; `com.cuvara.uitoolkit`'s `IScreenNavigator` is the one navigation abstraction.*
2. **The uGUI path is still permanent and still untouched.** The contract reserves world-space
   UI, combat UI, HP bars, damage numbers, anything with a `Transform`, `Animator`, DOTween or
   `ParticleSystem`, and anything pooled and spawned frequently, for prefab/uGUI. Nothing in this
   plan touches that, and nothing here should be read as making it legacy.
3. **The orphaned GameFoundation types** — `BaseUIToolkitScreenPresenter`,
   `BaseUIToolkitPopupPresenter`, `UIToolkitScreenViewBackend`, `UIToolkitBackNavigation` — are
   now dead code in a frozen fork. They are also the **best available reference implementation**
   for what is being rebuilt: the popup flow, the layer parenting, the back policy. Read them
   before writing the package equivalents; do not reference them.

**Ordering within the package** (each stage independently testable, none depends on a consumer):

| Stage | Content | Gate |
|---|---|---|
| **0** | `ScreenSubscriptions`, `ScreenLifecycleState`, `ScreenOptions`, `Runtime/TestSupport/` + doubles | unit tests, no panel |
| **1** | `BackNavigationSource` → `Func<bool>` (breaking; §5.4), `SeenCount`/`HandledCount` split, tests asserting **propagation** not just the counter | PlayMode test with a real panel |
| **2** | `IScreenAssetCache` + refcounting + release-on-teardown | unit tests over `FakeVisualTreeAssetLoader` |
| **3** | `IUIToolkitScreenPresenter`, `BaseUIToolkitScreenPresenter<TView[,TModel]>`, `BaseUIToolkitPopupPresenter<…>` | presenter tests, no scene (§11.2) |
| **4** | `IScreenNavigator` + `ScreenNavigator`: stack, push/pop/replace, suspend/resume, `IsBusy` serialisation | `TestNavigatorHost`, headless |
| **5** | `RegisterScreenFlow()` / `RegisterScreen<,>()` / `RegisterPopup<,>()` in the VContainer assembly | integration test in a throwaway project — CI already bootstraps one |
| **6** | Modals: overlay layer, `DimsBelow`, `CloseOnTapOutside`, **one test per flag** (§5.3) | PlayMode |
| **7** | Automatic `IViewModelSink` bind/unbind on activate/deactivate | `SpyViewModelSink` + a driven bridge |
| **8** | Project: `RootUIDocument` in the scene, `UILifetimeScope`, first real screen | the real thing |

Stage 1 is first among the risky ones because it is the only **breaking** change and the package
is 0.1.0 with one known consumer — cheapest it will ever be.

---

## 13. What is generated vs hand-written

**The generator situation changed and not for the better.** `ViewCreatorWizard` lives in
`Packages/com.gdk.core/Editor/Tools/ViewCreatorWizard/`, which is frozen. Its UI Toolkit templates
emit `BaseUIToolkitScreenPresenter` from the GameFoundation namespace, `[ScreenInfo]`,
`[Preserve]`, and `SignalBus`/`ILoggerManager` constructors — **every one of which is wrong for
the package design**. v1 planned to amend it; that is no longer available.

Three options, and I recommend the second:

| Option | Cost | Verdict |
|---|---|---|
| Keep using it and hand-fix every generated file | ~15 lines of edits per screen, every screen, forever | **no** — it makes the generator a net negative |
| **Port it into the package as an Editor tool** (`Editor/ScreenCreator/`) | one-off; the wizard is ~1000 lines across two files, templates are `const string`, and roughly half is uGUI paths the package does not need | **yes** |
| Ship no generator; document the three files | zero cost now; every screen author retypes the same 40 lines and they drift | no |

**What the ported generator must emit**, given the design above:

1. `{Name}.uxml` — with a `SafeAreaElement` wrapper for screens, and **not** for rows (nesting a
   second safe area double-insets every row; GameFoundation's templates already got this right).
2. **`{Name}.uss`** — the existing wizard emits none and puts inline `style="…"` in the UXML,
   which contradicts the contract's "prefer reusable classes over duplicated blocks". Emit a real
   stylesheet and reference it.
3. `{Name}Screen.cs` — ViewModel + `I{Name}View` + `{Name}View` + `{Name}Presenter`, with
   `OnBindAsync(model, subs, ct)` and **no lifecycle boilerplate whatsoever**. Specifically: no
   `Dispose` override, and none of the `clicked -= ; clicked += ;` dance that the current
   templates carry and that §10 makes unreachable.
4. `{Name}PresenterTests.cs` — the §11.2 skeleton **including the `subs.LiveCount == 0`
   assertion**, and with the correct asmdef references, because a generated test that does not
   compile on first run gets deleted and never comes back.
5. **`ScreenRegistrations.g.cs`** — a partial holding one `RegisterScreen<,>` line per generated
   screen. This is what returns step 3 of §2.4 to zero typing while keeping it compiler-checked
   and greppable.

Hand-written, always: the UXML element structure, the USS beyond the scaffold, the ViewModel
shape, the service interface, and the body of `OnBindAsync`. That is the screen; everything else
is scaffolding and should not be typed.

---

## 14. Where I disagree, or expect pain

### 14.1 Six inherited hazards — not "moot", but "do not repeat"

v1 raised six complaints, all aimed at GameFoundation. With that fork frozen they stop being
"fix GameFoundation" and become **"do not inherit this in the package"** — which is worth more,
because the package is being written now and can avoid all six from the start rather than
discovering them at v0.6. Each is a design rule with a named enforcement point.

| # | The hazard, as observed in the frozen fork | The rule for this package | Enforced by |
|---|---|---|---|
| 1 | **Service locator on the hot path.** `GetCurrentContainer()` has three call sites in `ScreenFlow` — `ScreenManager.cs:363` constructs every presenter through it, `BaseScreenPresenterCore.cs:123` resolves the asset manager through it — and the repo's own `CLAUDE.md` documents it as the "service locator fallback", which the UI contract forbids outright. | **No `GetCurrentContainer()` equivalent anywhere in `Runtime/`.** Presenters are constructor-injected; the navigator holds the scope. Where the flow genuinely must construct by runtime `Type`, that carve-out is **stated in the package's own docs** rather than left as a contradiction for a reader to find. §2.4 removed the need for it: `RegisterScreen<TPresenter,TView>` is generic, so nothing is constructed by `Type` at all. | code review; the absence is greppable |
| 2 | **`Dispose()` meaning two different things.** `CloseViewAsync` and `HideView` both call `Dispose()` on an object that then keeps living and is reopened. In C#, and specifically in VContainer, `IDisposable` means end of life. **This is the root of the unregister-then-register pattern** in every generated template, and it is the one I most want carried across. | **`Dispose()` means end of life and nothing else.** Close and suspend have their own names — `CloseAsync`, `Suspend`, `Resume` — and the only `Dispose` in the flow is `scope.Dispose()`, which really does end the screen. | §3's state machine; the fact that §6's destroy-by-default makes scope-end and screen-end the same moment |
| 3 | **Presenter bases demanding framework services in the constructor.** `BaseScreenPresenterCore(SignalBus, ILoggerManager)` forces both into every screen's ctor and every screen's test, which is why the contract's "testable as plain C#" clause stayed aspirational. | **`BaseUIToolkitScreenPresenter` has a parameterless protected constructor.** Everything the base needs — navigator, view, bridges — is set through internal setters after construction. Stated as a design rule in §11.1 because it is one line with a large blast radius. | §11.2's test compiles with no framework references, or the rule was broken |
| 4 | **A status enum that conflates "never loaded" with "closed".** `ScreenStatus { Opened, Closed, Hide, Destroyed }` cannot answer "is this loaded?" or "is an open in flight?", which is why v1 was forced to add a *parallel* enum rather than extend it — the original being public and `switch`ed on in six repos. | **`ScreenLifecycleState` carries every state the flow actually has** — `Registered, Creating, Constructed, Binding, Opening, Active, Suspended, Closing, Disposed` (§3). No parallel enum is needed because the first one is right. | §3; `ScreenLifecycleRecorder` in TestSupport asserts legal sequences |
| 5 | **Inert attribute flags.** Measured, not recalled: `IsEnableBlur` and `IsCloseWhenTapOutside` each appear in exactly one file — their own declaration — and are read **zero** times, while `IsOverlay` is read seven times across two files. Two of three declared popup options did nothing and warned about nothing. | **A flag nothing reads does not ship.** Every `ScreenOptions` member has a test that fails if its behaviour is removed (§5.3). | one test per flag, in stage 6 |
| 6 | **Namespace/folder mismatch.** `GameFoundation.Scripts.UIModule.UITK.Presenter` lives in `Scripts/UIModuleUITK/Presenter/`, against the repo convention that namespace mirrors folder. | **Namespaces mirror folders from the first file.** `Cuvara.UIToolkit.Flow` lives in `Runtime/Flow/`. Trivial to hold from the start, annoying to fix later. | code review |

Rules 2 and 3 are the two that decide whether this design is actually more convenient than what
it replaces, rather than differently shaped. Rule 2 is what deletes the boilerplate; rule 3 is
what makes the tests writable.

### 14.2 New disagreements, about this package

1. **`check_standalone.py` bans the names this layer wants.** `IScreenManager`, `IScreenPresenter`,
   `IScreenView`, `BaseScreenPresenter`, `ScreenStatus` are all on `BANNED_SYMBOLS` with word
   boundaries. The package must therefore call its own things `IScreenNavigator`,
   `IUIToolkitScreenPresenter`, `IUIToolkitScreenView`, `ScreenLifecycleState`. **I think this is
   correct and should stay** — the gate is doing exactly its job, and the awkwardness is a
   feature, since a type called `IScreenManager` in this package would be genuinely confusable
   with the frozen one. But it must be decided **before** the first type is named, because
   renaming a public API later is a breaking change. §17 records the full list.

2. **`gf-safe-area` is a surviving host leak the gate does not catch.**
   `Runtime/Utilities/SafeAreaElement.cs:67` declares `public const string UssClassName =
   "gf-safe-area"` — `gf` for GameFoundation. `BANNED_SUBSTRINGS` catches `gdk`,
   `gamefoundation`, `gamedevelopmentkit`, but not the two-letter initialism. It is a **public
   USS class name consumers write stylesheets against**, so renaming it later breaks their
   styles. Rename to `cuvara-safe-area` now, in stage 0, while there are no consumers — and
   consider whether the gate should assert a `cuvara-` prefix on every exported USS class name
   rather than enumerate forbidden ones, which is the version of the check that would have caught
   both this and `gdk-grid-row`.

3. **The contract's "one navigation abstraction" clause is being satisfied by a project decision
   that is not written in the contract.** §1.1 explains why I read the clause as permitting this;
   that reading depends entirely on GameFoundation's flow never being wired. **That fact lives in
   nobody's file.** If it is not written into `docs/UI-ARCHITECTURE.md`, a future contributor will
   quite reasonably call `RegisterScreenManager()`, and the count becomes two with no gate to
   catch it. This is my strongest disagreement with the current state: the decision is load-bearing
   and undocumented.

4. **`ScreenOptions.Retain` is a hazard I am shipping on purpose, and I am uneasy about it.**
   Retention reintroduces exactly the stale-data and rebind problems §6 argues against. I include
   it because "rebuild a 2000-element world map on every open" is a real objection I cannot answer
   otherwise. But it is the flag most likely to be reached for casually and then to cause a bug
   that looks like a data problem. Mitigation: no `Retain` without a comment saying why, enforced
   in review, and a `ScreenLifecycleRecorder` assertion in that screen's test that `OnBindAsync`
   re-runs on every push.

5. **Serialising all navigation through `IsBusy` will be felt.** Two rapid taps on two different
   buttons will queue, not interleave — which is correct, and will occasionally look like input
   lag. The alternative, concurrent stack mutation, produces orderings nobody can reason about.
   I am confident in the choice and expect to have to defend it to whoever reports the lag.

---

## 15. Open questions, and what would settle each

| Question | What would settle it |
|---|---|
| Does per-screen `CreateScope` cost enough to matter, given `VCONTAINER_ECS_INTEGRATION` makes every `Dispatch()` resolve `ContainerLocal<IEnumerable<ComponentSystemBase>>` and sort world helpers? | Profiler capture of 50 `CreateScope`+`Dispose` cycles in a build with Entities present. If non-trivial, the navigator avoids `RegisterEntryPoint` in screen scopes and calls presenter hooks directly. **This is the single measurement most likely to change the design.** |
| Does §9.2's interface reflection survive IL2CPP with Minimal stripping? | An **Android** build — per `CLAUDE.md` a StandaloneWindows64 build is Mono2x with stripping disabled and validates neither. Verify with `strings` on the managed assembly using the UTF-8 heap for type names (not `strings -el`, which is for string literals) and a control search. |
| Does `NavigationCancelEvent` reach the panel root with nothing focused? | PlayMode test: open a screen with no focusable element, send a synthetic cancel, assert the source's `SeenCount`. This decides whether §5.4's focus-on-activate mitigation is required or merely prudent. |
| Does the fixed `BackNavigationSource` actually let Android exit at the root? | Device test with `RootBackPolicy.NotHandled` and an empty stack. Cannot be settled in the Editor. |
| Does `display:none` on `root-ui-closed` actually suppress descendant layout in 6000.3.9f1? | `UIElementsUpdate` profiler marker with three screens suspended vs popped. §7's entire cost argument rests on it and it is ten minutes of work. |
| Is `CloneTree` over a cached asset actually cheap enough to make destroy-by-default free? | Time `CloneTree` on the real inventory UXML at realistic size. §6's whole trade depends on this number and I do not have it. |
| Is porting `ViewCreatorWizard` (§13) worth it before the first three screens exist, or after? | Judgement, not evidence. My inclination: after two screens, when the boilerplate is known rather than guessed. |
| **Should a screen open from `IStartable` / `IAsyncStartable`?** | **SETTLED — no. Kept visible so it is not re-proposed.** Verified in VContainer source: `IInitializable.Initialize()` runs synchronously inside `Dispatch()` (`EntryPointDispatcher.cs:26-39`), but `IStartable.Start()` and `IAsyncStartable.StartAsync()` are queued onto `PlayerLoopHelper` at `PlayerLoopTiming.Startup` (`:58-64, 122-130`) — **one player-loop point later, and the caller cannot await it**. A screen opened that way has a one-frame gap nothing can close. Use `IInitializable`, or an explicit awaited call from the navigator. |

---

## 16. What I could NOT verify

1. **Nothing here was run.** No Unity Editor opened, no test executed, no build made. Every Unity
   API claim is from the installed `.xml` doc files; every VContainer claim is from package
   source. Behavioural claims — `display:none` layout suppression, `NavigationCancelEvent` focus
   routing, per-scope dispatch cost, `CloneTree` cost — are **reasoned from signatures and doc
   comments, not measured**.
2. **The `BackNavigationSource` consumption defect is confirmed by reading, not by test.** The
   control flow in `OnNavigationCancel` is unambiguous (early-return on no subscriber, then
   unconditional `StopPropagation`), and the re-brief independently reports it. But no test
   demonstrates the Android symptom, and none can in the Editor.

   **How much that reading is worth, now measurable.** v1 reported two defects the same way —
   read off control flow with line numbers, neither reproduced, and v1 said explicitly that a
   fix should be dropped if its defect failed to reproduce. **Both reproduced.** Tests were
   written and run against the frozen fork: 29/29 with the fixes, 27/29 with them reverted, and
   the two failures were the two predicted ones — `CleanUpAllScreen` released nothing
   (`IAssetsManager.Unload` never called, asset resident for the process lifetime) and
   `GetScreen` after a failed load rethrew from the cached faulted task instead of retrying.
   Fixed as `a3b4e257`.

   That does not make item 2 verified — it is still a reading — but it is the same method with a
   two-for-two record on this codebase, so **plan for the fix rather than treating it as
   speculative.** Two nuances from that exercise, both worth importing as method:
   - v1 **understated** the `CleanUpAllScreen` defect. It said the loop "never calls
     `DestroyView`"; in fact `BaseScreenPresenterCore.Dispose()` has an **empty body**, so the
     loop did nothing whatsoever before clearing the dictionary — and the `ScreenStatus != Opened`
     skip made it worse rather than safer, since a closed screen was still cached, still holding
     its Addressables handle, and about to be dropped unreferenced. When reading control flow,
     check what the called method actually *does*, not only that it is called.
   - One of the three tests written for those fixes **passes against the old code too**, because
     the old path called that empty `Dispose()` and never reached the signal. It guards the
     hazard the *fix* introduces, not the original bug. **A test that has never been seen to fail
     is not coverage** — every test proposed in §11 and §12 should be run against the un-fixed
     code once, and any that stays green should be labelled as a guard rather than counted.
3. **`ScreenOptions` semantics for modal-over-modal** are designed, not validated. Whether
   `DimsBelow` composes sensibly three deep is the kind of thing that only shows up in use.
4. **That six other projects consume the `com.gdk.core` fork.** Carried from v1; still not on this
   machine; now largely irrelevant since the plan touches GameFoundation nowhere.
5. **The size of the `ViewCreatorWizard` port.** "~1000 lines, half of it uGUI" is from the file
   listing and a read of the template regions, not from attempting the port.
6. **That `Runtime/Ecs/EcsViewModelBridge.cs` is in its intended state.** v1 flagged an
   unexplained working-tree modification; it is now committed as `468d520`
   ("catch a late-registered ECS sink up to the current state") and §9.1 is written against the
   committed behaviour. I did not re-read the whole file after the commit.

---

## 17. Naming constraints imposed by the standalone gate — NEW

`.github/scripts/check_standalone.py` scans `Runtime/` and `Tests/` for `.cs`, `.asmdef`, `.uxml`,
`.uss` and fails on any hit. **These are the names this layer would naturally want and cannot
have.** Deciding them now is cheap; renaming a public API later is not.

`BANNED_SYMBOLS` (word-boundary regex): `IScreenViewBase`, `ISurfaceScreenView`,
`IScreenViewBackend`, `ScreenPresenterViewType`, `BaseScreenPresenterCore`, `BaseScreenPresenter`,
`IScreenManager`, `IScreenPresenter`, `IScreenView`, `IUIView`, `SignalBus`, `IAssetsManager`,
`ILoggerManager`, `RootUICanvas`, `ScreenStatus`.

`BANNED_NAMESPACES`: `GameFoundation`, `UniT.Logging`, `UniT.ResourceManagement`.

`BANNED_SUBSTRINGS` (case-insensitive, matches string literals too): `gdk`, `gamefoundation`,
`gamedevelopmentkit`.

| Wanted | Banned? | Use instead |
|---|---|---|
| `IScreenManager` | **yes** | `IScreenNavigator` |
| `IScreenPresenter` | **yes** | `IUIToolkitScreenPresenter` |
| `IScreenView` | **yes** | `IUIToolkitScreenView` (or reuse the existing `IUIToolkitView`) |
| `BaseScreenPresenter<T>` | **yes** | `BaseUIToolkitScreenPresenter<T>` |
| `ScreenStatus` | **yes** | `ScreenLifecycleState` |
| `ScreenNavigator`, `ScreenSubscriptions`, `ScreenOptions`, `ScreenLifecycleState`, `IScreenAssetCache`, `RootBackPolicy` | no | as written |
| USS class names | must avoid `gdk` | prefix everything `cuvara-` — and fix `gf-safe-area` (§14.2) |

Word boundaries matter: `\bIScreenPresenter\b` does **not** match inside
`IUIToolkitScreenPresenter`, so the `UIToolkit` infix is what makes these names legal as well as
unambiguous. Verify any new public type name against the gate **before** it ships, by running
`python3 .github/scripts/check_standalone.py` — it takes under a second.
