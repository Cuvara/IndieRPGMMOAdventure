# Changelog

All notable changes to this package are documented here.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

## [0.1.0] - 2026-08-21

First release. The code was developed inside `com.gdk.core` on the `feat/uitk-migration`
branch and extracted here unchanged; this entry describes what that work produced rather
than pretending the package has a history it does not have.

### Added

- **A UI Toolkit backend for GameFoundation's screen flow.** Screens are UXML documents
  parented into a `UIDocument`'s visual tree instead of prefabs instantiated under a
  `Canvas`. `BaseUIToolkitView`, `UIToolkitViewFactory`, `VisualElementViewLayer`.
- **Presenter bases** — `BaseUIToolkitScreenPresenter<TView>` / `<TView, TModel>` and
  `BaseUIToolkitPopupPresenter<TView>` / `<TView, TModel>`, mirroring the uGUI bases so a
  presenter changes backend by changing which base it derives from.
- **`RootUIDocument`** and `UIToolkitScreenViewBackend` — the runtime root and the backend
  `ScreenManager` selects.
- **`UIToolkitBackNavigation`** — Escape, gamepad B and the Android back button, driven
  from `NavigationCancelEvent` rather than from a per-frame `UnityEngine.Input` poll.
- **Collection adapters** — list, grid and multi-template, the UI Toolkit counterparts of
  the OSA adapters. The uGUI/OSA adapters are untouched.
- **`SafeAreaElement`, `SafeAreaCalculator`, `PanelScaleRatio`** — the UI Toolkit
  equivalents of the uGUI `SafeArea` and `ScaleScreenRatio` components.
- **`NotificationPopup` ported to UXML + USS**, alongside the uGUI popup, which stays.

### Notes

- **The uGUI path is permanent.** This backend sits behind the view-surface seam and is
  selected per presenter type; a project that registers nothing here is byte-for-byte
  unaffected. The prefab path was explicitly kept rather than deprecated.
- **`RegisterUIToolkitViewBackend` is opt-in and separate from `RegisterScreenManager`.**
  Folding it in would put a `UIDocument` in every GameFoundation consumer, including the
  ones with no UI Toolkit screen.
- **This package cannot be compiled by its own CI.** It requires `com.gdk.core`, a git
  submodule in a private NightHowlGames repository that Cuvara's Actions cannot fetch. The
  tests are real and run in the consuming project. See `Documentation~/CI.md`.

[Unreleased]: https://github.com/Cuvara/UIToolkit/compare/v0.1.0...HEAD
[0.1.0]: https://github.com/Cuvara/UIToolkit/releases/tag/v0.1.0
