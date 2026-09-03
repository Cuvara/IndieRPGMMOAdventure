# Hybrid data binding

The convention for using Unity 6's **runtime data binding** (`DataBinding`,
`INotifyBindablePropertyChanged`, `[CreateProperty]`) inside this package's MVP screens —
where it is allowed, where it is forbidden, and the one rule that is not negotiable.

The decision, in one sentence: **the MVP framework core stays untouched; runtime data
binding is allowed only as a View-internal implementation detail, behind the existing
`IView` interfaces, for data-heavy screens.** Nothing above a View — Presenter, sink,
Service, ECS bridge — ever sees a `DataBinding`, a `PropertyPath` or a binding mode.

## What binds, what stays manual

| Concern | Mechanism | Why |
|---|---|---|
| Values that **change during the screen's life** (health, counts, timers, names) | `Root.dataSource` + `SetBinding`, `BindingMode.ToTarget` | This is what binding is for: N mutable labels/bars without N imperative setters |
| **Static-once** data (a title set at bind, a fixed icon) | plain assignment in the View | A binding is per-frame-checkable machinery; a value that never changes needs none |
| **Commands** — clicks, submits, toggles-as-actions | `ScreenSubscriptions.Clicked` / `.On<TEvent>` | Commands are Presenter business, scoped to the screen's life and released by its scope. `ToSource`/`TwoWay` binding for behaviour hides the command path from the Presenter and from tests |
| **Navigation** | `ScreenNavigator` via the Presenter | Never a side effect of a binding |
| **Animation** | USS transitions / `schedule` in the View | The binding delivers the state; animating between states is the style system's job (see the sample's `.ecs-hud__fill` transition) |

Data flows one way: everything binds `BindingMode.ToTarget`. A screen that wants input
fields writing back into a model routes that through the Presenter like any other command
— `TwoWay` couplings are how a View silently becomes the owner of state.

## The mandatory-notify rule

**A ViewModel used as a binding source MUST implement
`INotifyBindablePropertyChanged` and MUST raise only on real change.** Both halves matter:

- A source that does **not** notify still "works" — because the binding system falls back
  to **version-polling the source on every UI update**. Every binding on it is
  re-evaluated per frame, forever, invisibly. That is precisely the per-frame work this
  package's contract ("update on data change, not per frame") forbids, and it is the
  failure mode that makes the rule mandatory rather than stylistic: the screen looks
  correct while quietly doing the work the whole architecture exists to avoid.
- A source that notifies on **every write**, changed or not, re-evaluates its bindings on
  every push — the ECS bridge's catch-up pass, a Presenter re-setting the same caption —
  which is the same waste arriving by invitation.

`BindableViewModel` (`Runtime/ViewModel/`) is both halves in one base class:

```csharp
public sealed class VitalsHudViewModel : BindableViewModel
{
    private string caption = string.Empty;
    private float  fraction;

    [CreateProperty]
    public string Caption
    {
        get => this.caption;
        set => this.Set(ref this.caption, value);   // raises only on real change
    }

    [CreateProperty]
    public float Fraction
    {
        get => this.fraction;
        set => this.Set(ref this.fraction, value);
    }
}
```

`Set` compares with `EqualityComparer<T>.Default`, assigns, raises `propertyChanged` with
the `[CallerMemberName]` property name, and returns whether anything changed. Mark every
bindable property `[CreateProperty]` so the binding system resolves it through a property
bag. The ViewModel stays plain C# — the UIElements types it touches are an interface and
an event-args struct; no `VisualElement`, no panel, no scene.

## The EcsHud walkthrough

`Samples~/EcsHud` is the reference hybrid screen. Full layering and rationale in its
README; the three hybrid-specific pieces:

**1. The ViewModel** — `VitalsHudViewModel` above. Plain, notifying, `[CreateProperty]`.

**2. The View** — binds once, in `Bind`, and has no `Render` method at all:

```csharp
public sealed partial class VitalsView : BaseUIToolkitView, IVitalsView
{
    public VitalsView(VisualTreeAsset visualTreeAsset) : base(visualTreeAsset)
    {
        this.StretchToParent();
        this.AssignQueries(this.Root);   // generated half: typed Require<T> queries
    }

    public void Bind(VitalsHudViewModel viewModel)
    {
        this.Root.dataSource = viewModel;

        this.HealthCaption.SetBinding(nameof(Label.text), new DataBinding
        {
            dataSourcePath = new PropertyPath(nameof(VitalsHudViewModel.Caption)),
            bindingMode    = BindingMode.ToTarget,
        });

        var fillBinding = new DataBinding
        {
            dataSourcePath = new PropertyPath(nameof(VitalsHudViewModel.Fraction)),
            bindingMode    = BindingMode.ToTarget,
        };
        fillBinding.sourceToUiConverters.AddConverter(
            (ref float fraction) => new StyleLength(Length.Percent(fraction * 100f)));
        this.HealthFill.SetBinding("style.width", fillBinding);
    }
}
```

The converter is the boundary discipline in miniature: the ViewModel exposes a plain
0..1 `float`; `StyleLength` — a UI Toolkit type — appears only inside the View.

**3. The sink** — the Presenter writes properties and knows nothing about binding:

```csharp
public sealed class VitalsPresenter : IViewModelSink<VitalsViewModel>
{
    public void Push(in VitalsViewModel boundary)
    {
        this.viewModel.Caption  = boundary.Caption;    // Set() guard: identical push
        this.viewModel.Fraction = boundary.Fraction;   // raises nothing
    }
}
```

The ECS rule holds untouched: ECS code never touches a `VisualElement`, and the bindable
ViewModel is plain C#, so the bridge → sink → ViewModel path involves no UI type. The
retrofit changed nothing in `Runtime/Ecs/` and nothing in the bridge — which is the
evidence that binding really is View-internal.

## `nameof` is mandatory; UXML `<Bindings>` is discouraged

Every `dataSourcePath` is `new PropertyPath(nameof(Vm.Property))`. The alternative —
authoring `<Bindings>` blocks in UXML with string `data-source-path` attributes — is
discouraged here for the same reason this package built `Require<T>` and the UXML codegen
(see `UXML-CODEGEN.md`): a stringly reference breaks silently. A renamed VM property
leaves a UXML-authored binding pointing at nothing, and the binding system's response is
to show stale data, not to throw. With `nameof`, the rename is a compile error; with the
generated `AssignQueries`, a renamed element is one too. Views should fail loudly at
compile or bind time, never dim quietly at runtime.

## The testing story

| Layer | How | Needs |
|---|---|---|
| ViewModel behaviour (notify on change, silence on equal, `Set` return) | plain NUnit — `Tests/Runtime/ViewModel/BindableViewModelTests.cs` | nothing: no panel, no element; also runs under plain `dotnet` |
| Presenter / sink | plain NUnit against a fake `IView` and a real ViewModel — assert VM state, not element state | nothing |
| The wiring (does a `Set` actually reach a bound element?) | `[UnityTest]` on a live `UIDocument` — `Tests/Runtime/ViewModel/BindableViewModelBindingTests.cs` | a PlayMode panel: the binding system only applies bindings inside a panel's update loop |

The split is the payoff of the convention: because binding is View-internal, everything
above the View tests exactly as it did before the retrofit — and a Presenter test now
asserts `viewModel.Caption`, which is one comparison, instead of walking a visual tree.

## MVP or binding? Per screen type

| Screen | Verdict | Because |
|---|---|---|
| HUD / vitals / resource bars | **Hybrid** | many values, high change rate, no commands — the reference case |
| Data-heavy status panels (character sheet, server status) | **Hybrid** | dozens of labels; imperative `Render` becomes a wall of setters |
| Dialogs / popups (confirm, notification) | **MVP only** | two or three static-once values and a button — binding machinery outweighs the screen |
| Forms and settings | **MVP, binding optional for display-only rows** | input is commands; route writes through the Presenter, never `TwoWay` |
| Navigation shells, loading screens | **MVP only** | lifecycle and flow, barely any mutable display state |
| Collection rows (`UIToolkitListAdapter` items) | **MVP only, today** | rows are recycled; a per-row `dataSource` swap per rebind is untested here — measure before adopting |

When in doubt, start MVP. A screen can adopt binding later without touching its
Presenter, its tests, or its callers — that is what "behind the existing `IView`
interfaces" buys.
