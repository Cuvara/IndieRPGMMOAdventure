# Cuvara UI Toolkit

A **standalone UI Toolkit screen layer** for Unity 6. It gives you view lifecycle, parenting
into a `UIDocument`'s visual tree, collection adapters, safe-area and panel-scale handling,
and a back-navigation event source — and it depends on no UI framework of its own.

It owns its contracts. A host binds it to whatever screen flow it already has.

## It does not depend on GameFoundation

This is the design constraint, not an accident of packaging.

The package was extracted from a GameFoundation branch, where every one of its files
referenced `com.gdk.core` — `IScreenView`, `IScreenManager`, `BaseScreenPresenter`,
`SignalBus`, `IAssetsManager`, `ILoggerManager`. **All of those references were severed.**
The dependency now runs one way: `com.gdk.core` references this package, through an adapter
that lives on the GameFoundation side.

| Lives here | Lives in the host |
|---|---|
| `IViewLayer`, `IViewSurface`, `IUIToolkitView` — the contracts | the screen-flow implementation that binds to them |
| `BaseUIToolkitView`, `UIToolkitViewFactory`, `VisualElementViewLayer` | presenter bases, if the host has presenters |
| `RootUIDocument` | which screens exist and when they open |
| collection adapters, safe area, panel scale | the back-navigation *policy* — what Escape closes |
| a back-navigation **event source** | |

The back-navigation split is the sharpest example. This package registers for
`NavigationCancelEvent` on the panel and raises a plain C# event. It does not decide what to
close and it does not open a quit dialog, because "what does Back mean" is an application
question. A host that wants the GameFoundation behaviour writes ten lines against the event.

**Why it matters practically:** a package that reaches into its host cannot be installed,
compiled or tested on its own. This one can — see [Documentation~/CI.md](Documentation~/CI.md),
where CI bootstraps a throwaway project from this package's own declarations and runs the
suite. That is only possible because nothing here needs a private submodule to resolve.

## Install

```jsonc
{
  "scopedRegistries": [
    { "name": "OpenUPM", "url": "https://package.openupm.com", "scopes": ["com.cysharp", "jp.hadashikick"] }
  ],
  "dependencies": {
    "com.cuvara.uitoolkit": "https://github.com/Cuvara/UIToolkit.git#v0.1.0"
  },
  "testables": ["com.cuvara.uitoolkit"]
}
```

The OpenUPM scoped registry covers UniTask and VContainer. **A UPM package cannot declare a
scoped registry of its own**, so this belongs to the consuming project and there is no way to
ship it from here. That is also why `com.frostbun.*` is not a dependency: asset loading goes
through `IVisualTreeAssetLoader`, which you implement over Addressables, `Resources`, or
whatever you already use.

**VContainer is required, not optional.** An earlier draft gated the registration assembly
behind a versionDefine so the package would install without it. That gating is
gone — this project standardises on VContainer for all dependency injection, so "no container"
is not a supported configuration and the branch existed without anything exercising it.

### A screen's lifetime is a child scope

This is the whole lifecycle story, and it is worth stating because getting it wrong is the
usual source of UI leaks.

```csharp
// open
var scope = container.CreateScope(b =>
{
    b.Register<InventoryView>(Lifetime.Scoped).As<IInventoryView>();
    b.RegisterEntryPoint<InventoryPresenter>();   // IStartable + IDisposable
});

// close
scope.Dispose();
```

One `Dispose()` unsubscribes the presenter's handlers, releases screen-scoped services and
tears down the view — together, structurally, rather than each being something a person has to
remember. The presenter stays a plain C# class with no `UIDocument` and no `VisualElement`, so
it is testable against mocked view and service interfaces with no scene involved.

## Using it

```csharp
// 1. A layer is somewhere a view can be parented. RootUIDocument gives you three.
var layers = rootUIDocument.Layers;         // Screen, Hidden, Overlay

// 2. A view is a UXML document plus a lifecycle.
public sealed class MyScreenView : BaseUIToolkitView
{
    private readonly Button closeButton;

    public MyScreenView(VisualTreeAsset asset) : base(asset)
    {
        this.closeButton = this.Root.Q<Button>("close");
    }
}

// 3. The factory loads the UXML through YOUR loader and constructs the view.
var view = await factory.CreateAsync<MyScreenView>("MyScreen");
view.ViewSurface.SetParent(layers.Screen);
await view.Open();
```

`IVisualTreeAssetLoader` is the one thing you must supply:

```csharp
public sealed class AddressablesVisualTreeAssetLoader : IVisualTreeAssetLoader
{
    public UniTask<VisualTreeAsset> LoadAsync(string key) => /* your loader */;
}
```

## What is here

| Path | Contents |
|---|---|
| `Runtime/Core/` | `IViewLayer`, `IViewSurface`, `IUIToolkitView`, `IVisualTreeAssetLoader`, `IPresenterInstantiator` |
| `Runtime/View/` | `BaseUIToolkitView`, `UIToolkitViewFactory`, `VisualElementViewLayer` |
| `Runtime/ViewModel/` | `BindableViewModel` — the notifying base for hybrid data-binding ViewModels |
| `Runtime/Managers/` | `RootUIDocument` and the default three-layer `RootUIDocument.uxml` |
| `Runtime/Collections/` | list, grid and multi-template adapters + item view/presenter bases |
| `Runtime/Utilities/` | `SafeAreaElement`, `SafeAreaCalculator`, `PanelScaleRatio`, `Require<T>` |
| `Runtime/Input/` | back-navigation event source |
| `Runtime/Ecs/` | DOTS/ECS presentation adapter — optional, needs `com.unity.entities` |
| `Editor/Codegen/` | UXML → typed view codegen (menu + auto-regen); `Core/` is Unity-free |
| `Tools~/UxmlCodegenCli/` | plain-`dotnet` CI drift check over the committed generated bindings |
| `Samples~/NotificationPopup/` | the smallest complete screen, host-free |
| `Samples~/EcsHud/` | a HUD driven from ECS, through the adapter — and the reference hybrid data-binding screen |
| `Tests/` | PlayMode tests (113) — they need a live panel, which EditMode has not got |

## Typed queries and UXML codegen

`root.Require<Label>("popup-title")` is `Q<T>` that throws a precise
`InvalidOperationException` instead of returning null — the element name, the expected
type and the root searched under, in the message.

On top of it sits a codegen: enroll a UXML once via
**Assets/Cuvara/Generate UXML Bindings** and a `partial` class with one typed property per
named element plus an `AssignQueries(root)` appears in `Generated/` beside it, regenerated
automatically on every save (opt-in — only enrolled files are ever touched) and
byte-checked against its UXML in CI by `Tools~/UxmlCodegenCli`. Your half of the partial
picks the base type and calls `AssignQueries` in the constructor. The full workflow — a
`ConfirmPopup` end to end, the naming/namespace conventions, the failure rules —
is in [Documentation~/UXML-CODEGEN.md](Documentation~/UXML-CODEGEN.md).

## Hybrid data binding

For data-heavy screens, Unity 6's runtime data binding is allowed — as a **View-internal
implementation detail** behind the existing `IView` interfaces, never above them. The
View assigns `Root.dataSource` and wires elements with C# `SetBinding` (`nameof` paths,
`BindingMode.ToTarget`); the Presenter writes plain properties on a
`BindableViewModel` (`Runtime/ViewModel/`) and never learns binding exists. Commands,
clicks and navigation stay on `ScreenSubscriptions`; and **a binding source must
notify** — a non-notifying source is version-polled by the binding system on every UI
update, which is the per-frame work this package forbids. The full convention, the
EcsHud walkthrough, the testing story and a per-screen decision table:
[Documentation~/HYBRID-DATA-BINDING.md](Documentation~/HYBRID-DATA-BINDING.md).

## DOTS / ECS

Optional. Install `com.unity.entities` and `Runtime/Ecs/` compiles; leave it out and that
assembly is skipped and nothing else changes.

**ECS does not touch UI Toolkit here, and the package will not let it.** The path is:

```
ECS world  ->  adapter  ->  ViewModel  ->  Presenter  ->  View  ->  UI Toolkit
```

`Runtime/Ecs/` is the **adapter half only**. It reads an unmanaged component, converts it to
a plain ViewModel, and pushes that to an `IViewModelSink<T>` the host implements — usually a
Presenter. It holds no view reference, names no Presenter type, and the assembly does not
reference UIElements at all. There is a test asserting that last point, because it is the
kind of boundary that erodes one convenient edit at a time.

```csharp
// 1. What the simulation writes. Unmanaged, Burst-friendly, knows nothing about UI.
public struct PlayerVitals : IComponentData { public int Health; public int MaxHealth; }

// 2. What crosses the boundary. Plain values — no VisualElement, no UIDocument.
public readonly struct VitalsViewModel { public readonly string Caption; /* ... */ }

// 3. The adapter. Convert() is usually the whole of a host bridge.
[UpdateInGroup(typeof(PresentationSystemGroup))]
public partial class VitalsBridge : EcsViewModelBridge<PlayerVitals, VitalsViewModel>
{
    protected override VitalsViewModel Convert(in PlayerVitals c) => new($"{c.Health}/{c.MaxHealth}");
}

// 4. Your Presenter is the sink. It knows an IView, never a VisualElement.
public sealed class VitalsPresenter : IViewModelSink<VitalsViewModel>
{
    public void Push(in VitalsViewModel vm) => this.view.Render(vm.Caption);
}

// 5. Bind for the screen's lifetime; dispose with its scope.
var registration = EcsSinkRegistration.Bind(bridge, presenter);
```

### Two rules, constraining different things

Keep them apart — satisfying one does not satisfy the other.

**Architecture.** ECS must never manipulate UI Toolkit. That constrains *what the adapter may
talk to*: a ViewModel, never a view.

**Threading.** `VisualElement` is plain managed C#, **not** a `UnityEngine.Object`. It cannot
be touched from `ISystem`, `IJobEntity`, Burst, or any worker thread — there is no attribute,
no unsafe cast and no `NativeContainer` that makes it work. This is a type-system fact, not a
performance guideline, and any design that wants to write the visual tree from a job is
impossible rather than merely slow. It constrains *where the adapter runs*: `SystemBase`,
main thread, `PresentationSystemGroup`, never `[BurstCompile]`.

### It stays quiet when nothing changes

Two mechanisms, both cheap:

- `Enabled` is false while no sink is registered, so a world with no screen open does not
  even evaluate the query.
- The query carries `SetChangedVersionFilter`, so chunks nothing has written since the last
  run are skipped.

That filter is chunk-granular and conservative — it reports a chunk changed if anything wrote
the component, including writing an identical value. Override `HasChanged` for value-level
deduplication when a sink is expensive enough to be worth it.

Pushing every frame would defeat all of this, and it is the specific thing this design exists
to prevent: rebuilding a subtree at 60fps on the main thread is how UI Toolkit gets a
reputation for being slow when the fault is the caller's.

### Entity-to-sink mapping is by value

An `IComponentData` is unmanaged and cannot hold a `VisualElement`, a Presenter, or anything
else managed. Route by a value key — an entity index/version pair, or a stable game id — and
keep the key-to-sink map on the managed side. Reaching for a managed component to dodge that
is the wrong answer.

## Known limits of UI Toolkit itself

Verified against the installed 6000.3.9f1 assembly, not assumed — worth knowing before you
plan a screen around something that does not exist:

- **No per-element material or shader.** `VisualElement.materialOverride` is absent. An
  effect that needs a custom material on a UI element has no UI Toolkit equivalent.
- **`VisualElement` is plain managed C#**, not a `UnityEngine.Object`. It cannot be a
  Timeline or Animator binding target, and it cannot be touched from Burst or from a job.
  Animate with USS transitions or `schedule`.
- Present and usable in this version: `SetBinding`/`dataSource`, `PanelInputConfiguration`
  and `panelInputRedirection`, and world-space panels via `renderMode`/`worldSpaceSize`.
