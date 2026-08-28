# ECS HUD

A health bar driven from a DOTS/ECS component, through the package's presentation bridge,
without ECS ever touching UI Toolkit.

Requires `com.unity.entities`. Without it, `Cuvara.UIToolkit.Ecs` is not compiled and this
sample will not build.

## The layering, which is the whole point

```
ECS world  ->  bridge (adapter)  ->  ViewModel  ->  Presenter  ->  View  ->  UXML
```

| Layer | File | May know |
|---|---|---|
| `PlayerVitals` | `EcsHudSample.cs` | nothing about UI |
| `VitalsBridge` | `EcsHudSample.cs` | the component and the ViewModel. **Not** `VisualElement` |
| `VitalsViewModel` | `EcsHudSample.cs` | plain values only |
| `VitalsPresenter` | `EcsHudSample.cs` | `IVitalsView`. **Not** `UIDocument`, `VisualElement`, UXML or USS |
| `VitalsView` | `EcsHudSample.cs` | UI Toolkit — the only layer that does |
| `EcsHud.uxml` / `.uss` | | structure and presentation, no logic |

Two separate reasons enforce that shape, and it is worth keeping them apart because they
constrain different things:

- **The architecture contract** says ECS must never manipulate UI Toolkit. That constrains
  *what the adapter may talk to*: a ViewModel, never a view.
- **A type-system fact** says `VisualElement` is plain managed C#, not a `UnityEngine.Object`,
  so it cannot be touched from `ISystem`, `IJobEntity`, Burst, or any worker thread — no
  attribute, no unsafe cast and no `NativeContainer` changes that. This constrains *where the
  adapter runs*: `SystemBase`, main thread, `PresentationSystemGroup`, never `[BurstCompile]`.

Satisfying one does not satisfy the other.

## A pure-ECS scene still needs one GameObject

`UIDocument` is a MonoBehaviour and there is no ECS equivalent, so even a scene whose
simulation is entirely unmanaged needs a single GameObject to host the panel. That is
`EcsHudBootstrap` here. It is not a compromise in the sample — it is how UI Toolkit works.

## Setting it up

1. Add a GameObject with a `UIDocument` and a `PanelSettings`.
2. Add `EcsHudBootstrap` and assign `EcsHud.uxml` to its `hudAsset` field.
3. Create an entity with a `PlayerVitals` component — a baker, a subscene, or
   `EntityManager.CreateEntity` from anywhere.
4. Write to that component. The bar updates. Stop writing and nothing is pushed at all.

That last sentence is the design, not a side effect: the bridge is disabled while no sink is
registered, and its query carries a change-version filter, so an idle simulation costs
nothing. Pushing every frame is what the architecture contract's performance section forbids.

## In a real project, use a scope

The bootstrap wires everything longhand in `Start()` so the dependency order is readable
without knowing a container. Production code should use a VContainer child scope per screen
and let `scope.Dispose()` unhook the sink registration, the presenter and the view together —
see the remarks on `EcsSinkRegistration`. A sink left registered keeps the Presenter alive,
which keeps the View alive, which keeps the visual tree alive. That leak is silent.

## Copy it, do not reference it

`Samples~` is invisible to Unity until imported through the Package Manager, and a sample is
meant to be edited. Copy it into your project and change it.
