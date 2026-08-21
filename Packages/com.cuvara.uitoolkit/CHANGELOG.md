# Changelog

All notable changes to this package are documented here.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

## [0.2.0] - 2026-08-21

### Added

- **Screen flow system** (`Runtime/Flow/`). A stack-based screen navigator with full lifecycle:
  `PushAsync`, `PopAsync`, `ReplaceAsync`, `PopToRootAsync`, `PopAllAsync`. Screens are UXML
  documents managed by presenters with lifecycle hooks: `OnBindAsync`, `OnActivate`,
  `OnDeactivate`, `OnSuspend`, `OnResume`, `OnBackRequested`. Model-parameterized variants
  (`PushAsync<TPresenter, TModel>(model)`) pass data to a screen at open time.
  - `ScreenNavigator` — the stack, per-scene scoped via VContainer.
  - `BaseUIToolkitScreenPresenter<TView>` / `<TView, TModel>` — presenter bases.
  - `BaseUIToolkitPopupPresenter<TView>` / `<TView, TModel>` — popup convenience with `Close()`.
  - `ScreenOptions` — `None` (full screen), `Modal` (overlay layer), `DimsBelow` (dims without
    suspending the screen below).
  - `ScreenSubscriptions` — scoped cleanup for button clicks and events.
  - `ScreenRegistry` — maps presenter type to view type, asset key, and options.
  - `ScreenLifecycleState` — 9 states from `Registered` to `Disposed`.
  - `IScreenScopeFactory` / `VContainerScreenScopeFactory` — one VContainer child scope per screen.
  - `ScreenFlowRegistration` — `RegisterUIToolkit()`, `RegisterScreenFlow()`,
    `RegisterScreen<T,V>()`, `RegisterPopup<T,V>()` extension methods for VContainer.
- **Back navigation** (`Runtime/Input/BackNavigationSource`). Wires Escape, gamepad B, and
  Android back to the navigator. One-line setup: `source.BackHandler = navigator.HandleBack`.
  `RootBackPolicy` controls what happens at the bottom of the stack: `NotHandled` (platform
  default), `Consume` (swallow), or `Raise` (event).
- **Assembly definitions for all samples.** Samples could not compile when imported via Package
  Manager because they had no `.asmdef`. Each sample now ships its own.
- **Loading Flow sample** (`Samples~/LoadingFlow`). Two-scene flow demonstrating every package
  feature: LoadingScene (MonoBehaviour-driven progress bar with tips and spinner) transitions to
  MainScene (ScreenNavigator with Push, Pop, Replace, PopToRoot, Modal+DimsBelow overlay, model
  parameters, lifecycle hooks, OnBackRequested override, BackNavigationSource,
  UIToolkitListAdapter collection adapter, and per-scene navigator scoping).
- **A DOTS/ECS presentation adapter** (`Runtime/Ecs/`), optional behind `com.unity.entities`
  and the `CUVARA_UITOOLKIT_ENTITIES` versionDefine.
  - `IViewModelSink<TViewModel>` — the contract a host's Presenter implements.
  - `EcsViewModelBridge<TComponent, TViewModel>` — managed `SystemBase` in
    `PresentationSystemGroup` that converts component data to a plain ViewModel.
  - `EcsSinkRegistration` — binds a sink for a screen's lifetime and unbinds on `Dispose`.
  - `Samples~/EcsHud` — the five layers end to end.
- **CI improvements**: samples compile job, samples gate (`check_samples.py`), real Unity test
  job, install probes, `check_standalone.py` wired into CI.
- **A test that `[UpdateInGroup]` inherits onto a host's bridge subclass.**

### Fixed

- A sink registered mid-session received nothing until the simulation next wrote the component.
  The next pass after a registration now runs unfiltered exactly once.
- CI failed on its own dependency check after the `com.gdk.core` dependency was removed.
- `check_standalone.py` was never wired into CI.

## [0.1.0] - 2026-08-21

First release. The code was developed inside `com.gdk.core` on the `feat/uitk-migration`
branch and extracted here — **not unchanged**: every file referenced the host framework,
and severing those references is most of what this release is. See "Changed on extraction"
below for what that cost.

### Added

- **A standalone UI Toolkit screen layer.** Screens are UXML documents parented into a
  `UIDocument`'s visual tree. `BaseUIToolkitView`, `UIToolkitViewFactory`,
  `VisualElementViewLayer`.
- **Its own contracts**, in `Runtime/Core/`: `IUIToolkitView` (the view lifecycle),
  `IViewLayer` / `IViewSurface` (where a view lives and how it moves),
  `IVisualTreeAssetLoader` (one method — the host supplies the asset pipeline), and
  `IPresenterInstantiator` (the collection adapters' presenter factory).
- **`RootUIDocument`** plus the default three-layer `RootUIDocument.uxml`, and a `Layers`
  value carrying the Screen / Hidden / Overlay layers as one thing.
- **Collection adapters** — list, grid and multi-template — with `IUIToolkitItemView`,
  `BaseUIToolkitItemView` and `BaseUIToolkitItemPresenter`.
- **`SafeAreaElement` / `SafeAreaCalculator`** — notch handling. Insets are applied as
  layout, either as padding or as absolute edges. Note that
  `PanelSettings.SetScreenToPanelSpaceFunction` is deliberately NOT used: it is present in
  6000.3.9f1, but it transforms *pointer* coordinates, so driving a safe area through it
  would move where clicks land without moving any layout.
- **`PanelScaleRatio`** — the `CanvasScaler`-equivalent aspect-ratio rule, applied to
  `PanelSettings`. It clones the settings asset by default, because `PanelSettings` is a
  shared project asset and writing to it at runtime is a source-control diff rather than a
  runtime tweak.
- **`BackNavigationSource`** — raises a C# event on `NavigationCancelEvent`, covering
  Escape, gamepad B and the Android back button.
- **A VContainer registration**, in its own assembly. See **Dependencies** below — it began
  as an optional, gated assembly and is not one any more.
- **A `Notification Popup` sample** and 113 PlayMode tests.

### Dependencies

- **VContainer is required, not optional.** `jp.hadashikick.vcontainer` is a real dependency
  and the registration assembly is no longer gated behind a versionDefine
  plus a matching `defineConstraints`. The project standardises on VContainer for all
  dependency injection, so a host without a container is not a supported configuration — and
  the gate was an assembly-level branch that nothing exercised. `Cuvara.UIToolkit.VContainer`
  stays a separate assembly for direction rather than for gating: it may reference the view
  and manager types, and they may not reference it, which is what keeps a container reference
  out of the view layer.
- `com.cysharp.unitask` and `com.unity.modules.uielements` are the other two. All three
  resolve from a registry, so the package installs from its own declarations — the OpenUPM
  scoped registry for `com.cysharp` and `jp.hadashikick` is the consuming project's to add,
  because a UPM package cannot declare a scoped registry of its own.

### Changed on extraction

Every one of these was a reference to `com.gdk.core` that had to be severed, not a
refactor for its own sake:

- `ISurfaceScreenView` / `IScreenViewBase` → `IUIToolkitView`. The host contract required a
  `RectTransform` and an `IsReadyToUse` flag; a `VisualElement` has no `Transform`, and
  `CloneTree` is synchronous so there is no "not ready yet" window to flag.
- `IViewLayer` / `IViewSurface` are now DEFINED here. The host deleted its copies and
  consumes these, so there is one definition rather than two.
- `IAssetsManager` → `IVisualTreeAssetLoader`. The host's loader comes from an OpenUPM
  scoped registry, and a UPM package cannot declare a scoped registry of its own — so that
  dependency could never have resolved for a consumer installing from a git URL.
- The collection adapters' `IDependencyContainer`, resolved through a static service
  locator, → `IPresenterInstantiator` passed in. That locator was both a dependency and the
  reason those adapters could not be exercised without a live scene.
- `SignalBus` → plain C# events. `ILoggerManager` → `UnityEngine.Debug`.
- Namespaces `GameFoundation.Scripts.UIModule.UITK.*` → `Cuvara.UIToolkit.*`; assemblies
  `GameFoundation.UIModule.UITK` → `Cuvara.UIToolkit`.

### Deliberately not here

`UIToolkitScreenViewBackend`, `BaseUIToolkitScreenPresenter`,
`BaseUIToolkitPopupPresenter`, the notification popup *presenter*, and the back-navigation
*policy* all stayed in `com.gdk.core`. Each one exists to bind this package to that
framework — it implements `IScreenViewBackend`, or takes a `SignalBus`, or decides what
Back closes. Moving them here would have re-created the dependency this package exists to
remove. A CI gate (`.github/scripts/check_standalone.py`) fails the build if any of those
host symbols reappears under `Runtime/` or `Tests/`.
