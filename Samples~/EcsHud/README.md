# ECS HUD

A health bar driven from a DOTS/ECS component, through the package's presentation bridge,
without ECS ever touching UI Toolkit — and the package's **reference hybrid screen**: the
View renders through runtime data binding (`Root.dataSource` + `SetBinding`) instead of
imperative `Render` calls, behind the same `IView` interface MVP always had. The
convention this implements is `Documentation~/HYBRID-DATA-BINDING.md`.

Requires `com.unity.entities`. Without it, `Cuvara.UIToolkit.Ecs` is not compiled and this
sample will not build.

## The layering, which is the whole point

```
ECS world  ->  bridge (adapter)  ->  ViewModel  ->  Presenter  ->  View  ->  UXML
```

| Layer | File | May know |
|---|---|---|
| `PlayerVitals` | `EcsHudSample.cs` | nothing about UI |
| `VitalsBridge` | `EcsHudSample.cs` | the component and the boundary ViewModel. **Not** `VisualElement` |
| `VitalsViewModel` | `EcsHudSample.cs` | plain values only — the readonly struct that crosses from ECS |
| `VitalsHudViewModel` | `EcsHudSample.cs` | plain notifying properties (`BindableViewModel`). **Not** `VisualElement`, not `DataBinding` |
| `VitalsPresenter` | `EcsHudSample.cs` | `IVitalsView` and the bindable ViewModel. **Not** `UIDocument`, `VisualElement`, `DataBinding`, UXML or USS |
| `VitalsView` | `EcsHudSample.cs` + `Generated/VitalsView.uxml.g.cs` | UI Toolkit — the only layer that does. Owns the bindings |
| `VitalsView.uxml` / `EcsHud.uss` | | structure and presentation, no logic, no `<Bindings>` |

Two separate reasons enforce that shape, and it is worth keeping them apart because they
constrain different things:

- **The architecture contract** says ECS must never manipulate UI Toolkit. That constrains
  *what the adapter may talk to*: a ViewModel, never a view.
- **A type-system fact** says `VisualElement` is plain managed C#, not a `UnityEngine.Object`,
  so it cannot be touched from `ISystem`, `IJobEntity`, Burst, or any worker thread — no
  attribute, no unsafe cast and no `NativeContainer` changes that. This constrains *where the
  adapter runs*: `SystemBase`, main thread, `PresentationSystemGroup`, never `[BurstCompile]`.

Satisfying one does not satisfy the other. Note that the hybrid retrofit changed neither:
the bridge and the sink contract are exactly what they were when the View rendered
imperatively — data binding is invisible above the View.

## What "hybrid" looks like here

- The sink (`VitalsPresenter`) writes two plain properties on `VitalsHudViewModel`. Its
  `Set` guard means an identical push — the bridge's catch-up pass, say — raises nothing.
- `VitalsView.Bind` runs **once**: assigns `Root.dataSource`, then `SetBinding` per
  element, every path a `nameof`, every mode `BindingMode.ToTarget`. There is no `Render`
  method; after `Bind`, changes flow through the binding system on notification.
- The ViewModel **must notify** (that is what `BindableViewModel` is for): a data source
  that does not implement `INotifyBindablePropertyChanged` is version-polled by the
  binding system on every UI update — per-frame work the package contract forbids.
- The fraction→width conversion is a converter on the binding, in the View, so
  `StyleLength` never leaks above the View.
- Had this HUD buttons, their clicks would stay on `ScreenSubscriptions`. Binding carries
  values toward the UI; it never carries commands.

`VitalsView.uxml` is enrolled in the UXML codegen: `Generated/VitalsView.uxml.g.cs` is
the other half of the `partial` View — typed element properties resolved through
`Require<T>` — regenerated on save and drift-checked in CI. A renamed element is a
compile error, and a missing one throws with its name at construction.

## A pure-ECS scene still needs one GameObject

`UIDocument` is a MonoBehaviour and there is no ECS equivalent, so even a scene whose
simulation is entirely unmanaged needs a single GameObject to host the panel. That is
`EcsHudBootstrap` here. It is not a compromise in the sample — it is how UI Toolkit works.

## Setting it up

1. Add a GameObject with a `UIDocument` and a `PanelSettings`.
2. Add `EcsHudBootstrap` and assign `VitalsView.uxml` to its `hudAsset` field.
3. Create an entity with a `PlayerVitals` component — a baker, a subscene, or
   `EntityManager.CreateEntity` from anywhere.
4. Write to that component. The bar updates. Stop writing and nothing is pushed at all.

That last sentence is the design, not a side effect: the bridge is disabled while no sink is
registered, its query carries a change-version filter, and the bindable ViewModel raises
only on real change — three layers of "update on data change, not per frame", one per
mechanism that could otherwise do per-frame work.

## In a real project, use a scope

The bootstrap wires everything longhand in `Start()` so the dependency order is readable
without knowing a container. Production code should use a VContainer child scope per screen
and let `scope.Dispose()` unhook the sink registration, the presenter and the view together —
see the remarks on `EcsSinkRegistration`. A sink left registered keeps the Presenter alive,
which keeps the ViewModel alive, which — through the panel's `dataSource` — keeps the
visual tree alive. That leak is silent.

## Copy it, do not reference it

`Samples~` is invisible to Unity until imported through the Package Manager, and a sample is
meant to be edited. Copy it into your project and change it.
