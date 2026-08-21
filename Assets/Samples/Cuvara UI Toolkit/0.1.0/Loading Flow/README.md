# Loading Flow Sample

Two-scene flow demonstrating the screen navigator across scene boundaries:

1. **LoadingScene** — progress bar animates, then loads MainScene
2. **MainScene** — three buttons, each opening a different popup

## Setup

1. Import this sample from the Package Manager
2. Add both scenes to Build Settings:
   - `Assets/Samples/Cuvara UI Toolkit/0.1.0/Loading Flow/LoadingScene.unity` (index 0)
   - `Assets/Samples/Cuvara UI Toolkit/0.1.0/Loading Flow/MainScene.unity` (index 1)
3. Open LoadingScene, press Play

## What it shows

- `ResourceAssetLoader` — `IVisualTreeAssetLoader` backed by `Resources.Load`
- Per-scene `LifetimeScope` with independent navigator + screen registrations
- `BaseUIToolkitScreenPresenter` — full-screen loading with async progress
- `BaseUIToolkitPopupPresenter` — modal popups (Info, Confirm, Settings)
- `BackNavigationSource` wired to navigator (Escape / Android back closes popups)
- `ScreenOptions.Modal | ScreenOptions.DimsBelow` — overlay that dims but does not suspend

## Screens

| Key | Type | Description |
|-----|------|-------------|
| LoadingScreen | Screen | Progress bar with 6 animated steps |
| MainScreen | Screen | Three buttons: Info, Confirm, Settings |
| InfoPopup | Popup | Single "Got it" close button |
| ConfirmPopup | Popup | Cancel / OK, logs on confirm |
| SettingsPopup | Popup | Music + SFX sliders, fullscreen toggle |
