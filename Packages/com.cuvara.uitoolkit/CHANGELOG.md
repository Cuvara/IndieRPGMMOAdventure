# Changelog

All notable changes to this package are documented here.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Added

- **A DOTS/ECS presentation adapter** (`Runtime/Ecs/`), optional behind
  `com.unity.entities` and the `CUVARA_UITOOLKIT_ENTITIES` versionDefine.
  - `IViewModelSink<TViewModel>` — the contract a host's Presenter implements. This is the
    package's entire coupling to MVP: it knows a sink, not a Presenter, not a View.
  - `EcsViewModelBridge<TComponent, TViewModel>` — a managed `SystemBase` in
    `PresentationSystemGroup` that converts component data to a plain ViewModel and pushes it.
  - `EcsSinkRegistration` — binds a sink for a screen's lifetime and unbinds on `Dispose`.
  - `Samples~/EcsHud` — the five layers end to end.

  **ECS does not touch UI Toolkit here.** The path is
  `ECS -> adapter -> ViewModel -> Presenter -> View -> UI Toolkit`, per the project's UI
  architecture contract, and `Runtime/Ecs/` is the adapter arrow only. The assembly does not
  reference UIElements at all and a test asserts that, because this is the kind of boundary
  that erodes one convenient edit at a time.

  Two separate rules produce that shape and it is worth keeping them apart. The *architecture*
  rule constrains what the adapter may talk to — a ViewModel, never a view. A *type-system*
  fact constrains where it runs: `VisualElement` is plain managed C#, not a
  `UnityEngine.Object`, so it cannot be touched from `ISystem`, `IJobEntity`, Burst or any
  worker thread at all. Hence `SystemBase`, main thread, no `[BurstCompile]`. Satisfying one
  rule does not satisfy the other.

  It stays quiet when nothing changes: `Enabled` is false while no sink is registered, and the
  query carries `SetChangedVersionFilter`. Pushing every frame is what the contract's
  performance section forbids.

### Fixed

- A sink registered mid-session received nothing until the simulation next wrote the
  component. Both quiet-keeping mechanisms were working correctly and combining into a bug: the
  change filter skipped the untouched chunk, so a screen opened while the simulation was idle
  stayed blank. The next pass after a registration now runs unfiltered exactly once. Found by a
  test, not in review.

### Fixed

- **CI failed on its own dependency check.** The step required
  `x-manualDependencies["com.gdk.core"]` to be present — correct while the package still
  depended on GameFoundation, wrong from the moment it became standalone and that field was
  removed. It now asserts only what still holds: no unresolvable package may appear in
  `dependencies`.
- **`check_standalone.py` was never wired into CI.** The gate that keeps this package
  standalone existed in `.github/scripts/` and nothing ran it, which is the shape of defect
  it exists to catch — present, plausible, and inert.

### Added

- **A real Unity test job.** The package bootstraps a throwaway project from its own
  `package.json` — the only thing hand-supplied is the OpenUPM scoped registry, which a UPM
  package is not permitted to declare for itself — and runs EditMode and PlayMode. This is
  only possible because the package is standalone; a package reaching into a private
  submodule cannot be tested by its own CI.
  The job asserts on the result XML rather than the exit code: the runner sets
  `USE_EXIT_CODE=false`, so Unity exits 0 on "No tests were executed" and the published
  check goes neutral rather than red. A run that executed nothing fails here.
  `activeInputHandler: 1` is pinned in the bootstrap — the strictest setting, where the
  legacy `UnityEngine.Input` API throws rather than returning false.
- **Install probes.** `documented` gates: a consumer following the README must be able to
  compile the package. `bare` is informational and expected to fail, recording what a
  consumer sees with no scoped registry rather than leaving it to be guessed at.

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
