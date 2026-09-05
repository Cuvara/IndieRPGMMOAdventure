# Changelog

All notable changes to this package are documented here.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [0.6.0] - 2026-09-06

### Added

- **Screen Creator Wizard** (`Editor/ScreenCreator/`). An Editor window
  (`Assets/Cuvara/Create Screen`) that scaffolds a new screen, popup or collection item in
  one click: the UXML (with `SafeAreaElement` for screens/popups, not items), a USS skeleton,
  the C# file (view interface + view + presenter with `OnBindAsync(subs, ct)`, no `Dispose`
  override, `Require<T>` queries), and a test skeleton with the `subs.LiveCount == 0`
  assertion. Replaces the frozen GameFoundation wizard: no `SignalBus`, no `ILoggerManager`,
  no `[Preserve]`, no `ISurfaceScreenView`, no `BindData` — every line emitted matches the
  package's own v0.5.0 API. Templates are pure string constants, testable with NUnit alone.
  21 EditMode tests cover: no banned host-framework tokens, correct base classes, correct
  `OnBindAsync` signature, `ScreenSubscriptions` parameter, `CancellationToken` parameter,
  `Require<T>` instead of `Q<T>`, UXML well-formed XML, SafeArea on screens not items,
  element names match view queries, placeholder substitution, kebab-case conversion.

## [0.5.0] - 2026-09-05

### Added

- **`ScreenOptions.CloseOnTapOutside`** — a modal with this flag gets a full-screen scrim
  element behind it; a `PointerDownEvent` on the scrim pops the modal. When combined with
  `DimsBelow`, the scrim is semi-transparent (`rgba(0,0,0,0.5)`); otherwise fully
  transparent. The scrim is removed on close or dispose. One test per behaviour.
- **`ScreenOptions.Retain`** — opt-in instance retention. A retained screen's scope survives
  pop and the instance is reused on the next push. `OnBindAsync` re-runs with a fresh
  `ScreenSubscriptions` on every push, so double-registration is structurally impossible.
  `ScreenNavigator.Dispose()` releases all retained entries. Four tests cover retain, reuse,
  re-bind, and dispose.
- **`subs.OnFirstGeometry(element, callback)`** on `ScreenSubscriptions` — defers a callback
  to the first `GeometryChangedEvent` from an element, then unregisters itself. For the
  measure-after-resume pattern: a screen suspended under `display:none` has no resolved
  layout, and this is the convenience that waits for the first real layout pass.
- **Focus-on-activate** — the navigator focuses the top screen's root element on activate
  (push and resume), so `NavigationCancelEvent` (Escape, gamepad B, Android back) always
  reaches the root even with no focused element. Without this, Back silently stops working
  after a push if nothing in the screen is focusable.
- **`BackNavigationSource.SeenCount`** — counts every Back press that reached the source
  while enabled, whether handled or not. `SeenCount - HandledCount` is "how many presses
  reached the platform's own Back".
- **`VisualTreeAsset` caching in `UIToolkitViewFactory`** — the factory caches loaded assets
  by key so destroy-on-close does not re-hit the loader on every push of the same screen.
  `ClearCache()` is called on navigator dispose (scene teardown).
- **`EcsAutoSinkBinder`** (`Runtime/Ecs/`) — hooks into `IScreenNavigator.ScreenActivated`/
  `ScreenDeactivated` events and auto-binds every `IViewModelSink<T>` interface on a
  presenter to its matching `EcsViewModelBridge` in the world. The author writes zero
  registration code — just implement the interface. Binds on activate, unbinds on
  deactivate, so a suspended screen stops receiving pushes.
- **TestSupport doubles** (`Runtime/TestSupport/`) — shipped for consumers:
  `FakeVisualTreeAssetLoader` (dictionary-backed, `FailFor`, `DelayFrames`, `LoadCount`),
  `FakeScopeFactory` (`Created`/`Disposed` counters),
  `FakeViewLayer` / `RecordingViewSurface` (assert parenting without a panel),
  `SpyViewModelSink<T>` (records pushes),
  `FakeBackSource` (simulates Back, reports consumed/unhandled),
  `ScreenSubscriptionsAssertions.AssertAllReleased()` extension.

## [0.4.0] - 2026-09-03

### Added

- **Hybrid data-binding convention** — Unity 6 runtime data binding
  (`DataBinding`/`INotifyBindablePropertyChanged`/`[CreateProperty]`) is now allowed, as a
  **View-internal implementation detail behind the existing `IView` interfaces**, for
  data-heavy screens. The MVP core is untouched; commands, clicks and navigation stay on
  `ScreenSubscriptions`; every binding is `BindingMode.ToTarget` with a `nameof` path
  (stringly UXML `<Bindings>` discouraged). Convention, walkthrough, testing story and a
  per-screen decision table: `Documentation~/HYBRID-DATA-BINDING.md`.
- **`BindableViewModel`** (`Runtime/ViewModel/`, namespace `Cuvara.UIToolkit.ViewModel`).
  The notifying base a binding source must derive from: `Set<T>(ref field, value)` guards
  with `EqualityComparer<T>.Default`, raises `propertyChanged` with the
  `[CallerMemberName]` property name only on real change, and returns whether anything
  changed. Notifying is mandatory because a non-notifying `DataBinding` source is
  version-polled by the binding system on every UI update — per-frame work the package's
  "update on data change, not per frame" contract forbids. Stays plain C#: testable with
  NUnit alone, no panel.
- **EcsHud sample retrofitted as the reference hybrid screen.** The imperative
  `Render(caption, fraction)` path is gone: the sink writes properties on a
  `[CreateProperty]`-annotated `VitalsHudViewModel`, and `VitalsView.Bind` assigns
  `Root.dataSource` and wires the label and bar once via `SetBinding` (the
  fraction→`StyleLength` conversion is a converter on the binding, so UI Toolkit types
  never leak above the View). The `adapter → ViewModel → View` layering and the ECS rule
  are unchanged — nothing in `Runtime/Ecs/` moved. The sample's UXML (renamed
  `VitalsView.uxml` so the generated class matches the view) is now **enrolled in the
  UXML codegen**: `Generated/VitalsView.uxml.g.cs` is the other half of the partial View
  and is drift-checked by CI alongside the test fixture.
- **Tests**: `Tests/Runtime/ViewModel/BindableViewModelTests.cs` (plain C# — raise with
  correct name via `[CallerMemberName]`, silence on equal values including null→null,
  same-reference and equal-but-distinct strings, return-value semantics, value and
  reference types; verified under plain `dotnet` as well) and
  `BindableViewModelBindingTests.cs` (`[UnityTest]` on a live `UIDocument` — a `Set()`
  reaches a bound `Label` and a converter-driven `style.width` through the real binding
  system, with no `Render` call).

### Changed

- **`UXML-CODEGEN.md` documents a batchmode limitation** found while verifying 0.3.0 on
  a real Editor (6000.3.9f1): a `-batchmode -quit` session that starts with compile
  errors exits before the asset import step (exit code still 0), so the auto-regen
  postprocessor never runs in it — after an element rename breaks consuming code,
  batchmode cannot regenerate its way out. The doc lists the recovery paths (git
  checkout of the `.g.cs`, the menu item in an interactive Editor, or
  `Tools~/UxmlCodegenCli`). Docs only; no code change.

## [0.3.0] - 2026-09-03

### Added

- **`Require<T>` query extension** (`Runtime/Utilities/VisualElementQueryExtensions.cs`).
  `root.Require<Label>("popup-title")` wraps `Q<T>` and throws `InvalidOperationException`
  naming the missing element, the expected type and the root searched under — a UXML edit
  that breaks a binding now fails at bind time with a message, not as a
  `NullReferenceException` later.
- **UXML → typed view codegen** (`Editor/Codegen/`, new `Cuvara.UIToolkit.Editor`
  assembly). Parses a `.uxml` as plain XML and generates
  `<uxml-dir>/Generated/<Name>.uxml.g.cs`: a `partial`, base-less class with one typed
  property per named element (PascalCase from kebab-case; unknown/custom tags fall back to
  `VisualElement` with a comment naming the tag) and an `AssignQueries(VisualElement root)`
  resolving each through `Require<T>`. Three layers:
  - **pure core** in `Editor/Codegen/Core/` — string in, string out, deliberately
    Unity-free so it compiles outside Unity;
  - **enrollment menu** — `Assets/Cuvara/Generate UXML Bindings` on selected `.uxml`
    assets does the first generation;
  - **auto-regen postprocessor** — regenerates on `.uxml` import ONLY when the generated
    file already exists (opt-in gate), and skips writing (and refreshing) when the fresh
    content is byte-identical (loop guard).
  Duplicate names, PascalCase collisions (`popup-title` vs `popupTitle`) and a property
  colliding with the class name fail generation with a message listing the offenders.
  Output is deterministic — document order, no timestamps, UTF-8 no BOM, `\n`.
  Namespace convention (`.uxml-namespace` override → nearest asmdef `rootNamespace` →
  `UxmlBindings`) and the full workflow: `Documentation~/UXML-CODEGEN.md`.
- **CI drift check** (`Tools~/UxmlCodegenCli/` — Unity ignores `~` folders). A plain
  `dotnet run` console project compiling the same pure-core sources; scans roots for
  enrolled UXML, regenerates in memory, byte-compares with the committed file and exits
  non-zero listing drifted files. Wired into the consuming repo's
  `.github/workflows/uxml-codegen-drift.yml`.
- **Tests**: `Tests/Editor/UxmlBindingGeneratorTests.cs` (string fixtures — properties and
  types, unknown-tag fallback, template skipping, duplicate/collision errors,
  kebab→Pascal edge cases, determinism, document order; also runnable under plain
  `dotnet`), `Tests/Runtime/RequireQueryTests.cs`, and an enrolled
  `Tests/Runtime/ConfirmPopup.uxml` whose committed generated class is exercised by
  `GeneratedConfirmPopupTests` and drift-checked by CI.

### Fixed

- **CI test project installs the Input System package it pins.** Both Unity jobs wrote
  `activeInputHandler: 1` ("Input System package only") into `ProjectSettings.asset` without
  adding `com.unity.inputsystem` to the manifest. That combination is an inconsistent project:
  UI Toolkit's `InputForUI` finds no Input System provider, falls back to its legacy
  `InputManagerProvider`, and that provider reads `UnityEngine.Input.mousePosition` — which
  throws under `activeInputHandler: 1`. The exception arrives as an unhandled log message
  during the runtime panel's update loop, and the test framework attributes it to whichever
  test happens to be running. Three `ScreenFlowRegistrationTests` cases failed this way,
  none of them touching input.

### Changed

- **The samples job compiles every sample `package.json` declares** instead of a hardcoded
  list of two. The list had already gone stale: `ScreenFlow` — the sample with the scene, and
  the largest of the four — was added while the job kept compiling `NotificationPopup` and
  `EcsHud` and reporting success over a sample it had never seen. `LoadingFlow` was in the
  same position.
- **The test job installs `com.unity.entities`**, so `Cuvara.UIToolkit.Ecs.Tests` compiles and
  runs in CI. Without it the `CUVARA_UITOOLKIT_ENTITIES` constraint is unmet, the assembly is
  gated out, and CI silently reported ~20 fewer tests than a local run — invisible, because
  the count is never compared against anything.

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
