# Notification Popup

The smallest complete screen the package can express, with no host framework involved.

```csharp
var factory = new UIToolkitViewFactory(myLoader);            // your IVisualTreeAssetLoader
var popup   = await factory.CreateAsync<NotificationPopupView>("NotificationPopup");

popup.SetContent("Are you sure?", "Do you really want to quit?", NotificationType.Option);
popup.Confirmed += Application.Quit;
popup.Cancelled += () => popup.DestroySelf();

popup.ViewSurface.SetParent(rootUIDocument.Layers.Overlay);
await popup.Open();
```

## What to notice

**There is no presenter and no model class.** The view raises `Confirmed` and `Cancelled` as
plain C# events. Whether you drive it from an MVP presenter, a state machine, or four lines
in a method is your architecture — the package has no opinion, and holds no reference to
whatever you choose.

**`SetContent` picks the button row with `display`, not `visibility`.** A `visibility: hidden`
element still occupies its space, so the panel would keep a gap the exact size of the row
that is not showing.

**The view is created invisible.** `BaseUIToolkitView`'s constructor sets opacity to 0, so
`Open()` is what reveals it — otherwise parenting into a visible layer flashes one
un-transitioned frame. `Open()` and `Close()` also move `pickingMode`, so a hidden popup
does not swallow clicks aimed at the screen behind it.

**Files:** `NotificationPopupView.cs`, `NotificationPopup.uxml`, `NotificationPopup.uss`.
Copy them into your project and change them; a sample is meant to be edited, not referenced.
