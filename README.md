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
    { "name": "OpenUPM", "url": "https://package.openupm.com", "scopes": ["com.cysharp"] }
  ],
  "dependencies": {
    "com.cuvara.uitoolkit": "https://github.com/Cuvara/UIToolkit.git#v0.1.0"
  },
  "testables": ["com.cuvara.uitoolkit"]
}
```

The OpenUPM scoped registry is for UniTask. **A UPM package cannot declare a scoped registry
of its own**, so this belongs to the consuming project and there is no way to ship it from
here. That is also why `com.frostbun.*` is not a dependency: asset loading goes through
`IVisualTreeAssetLoader`, which you implement over Addressables, `Resources`, or whatever you
already use.

VContainer is optional. Present, `GDK_VCONTAINER` is defined and the registration extension
compiles; absent, that one file compiles out.

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
| `Runtime/Core/` | `IViewLayer`, `IViewSurface`, `IUIToolkitView`, `IVisualTreeAssetLoader` |
| `Runtime/View/` | `BaseUIToolkitView`, `UIToolkitViewFactory`, `VisualElementViewLayer` |
| `Runtime/Managers/` | `RootUIDocument` |
| `Runtime/Collections/` | list, grid and multi-template adapters + item view/presenter bases |
| `Runtime/Utilities/` | `SafeAreaElement`, `SafeAreaCalculator`, `PanelScaleRatio` |
| `Runtime/Input/` | back-navigation event source |
| `Samples~/NotificationPopup/` | the smallest complete screen, host-free |
| `Tests/` | EditMode and PlayMode tests |

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
