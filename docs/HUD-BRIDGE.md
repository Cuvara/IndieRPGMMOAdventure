# DOTS → UI Toolkit HUD bridge

How ECS world data reaches the UI Toolkit HUD. Wired 2026-09-04, on top of the dots wiring
(`docs/DOTS-WIRING.md`). The two packages stay mutually unaware — `com.cuvara.dots` and
`com.cuvara.uitoolkit` never reference each other; the game composes them, which is why
everything here lives under `Assets/Scripts/UI/Hud/` and nothing was added to either package.

## The data path

```
netcode mirrors                Assets/Scripts/UI/Hud/Ecs/            Assets/Scripts/UI/Hud/
NetworkEntity      ─┐
NetworkEntityState ─┼─► HudStateSystem ─► HudState ─► HudBridgeSystem ─► HudSnapshot
LocalTransform     ─┘   (SimulationGroup)  (singleton)  (PresentationGroup)     │
                                                                     HudPresenter (sink)
                                                                                │
                                                              HudViewModel (BindableViewModel)
                                                                                │  runtime data binding
                                                                    HudView ─► HudView.uxml
```

Every arrow is one-way and each layer knows only the one below it. ECS code never touches a
`VisualElement` (the contract in `docs/UI-ARCHITECTURE.md`); the binding is a View-internal
detail per the uitoolkit package's `HYBRID-DATA-BINDING.md`, and the whole shape is the
package's `Samples~/EcsHud` productionized against the real netcode world.

## What is actually bridged

| Source (on every netcode mirror entity) | `HudState` field | `HudViewModel` property |
|---|---|---|
| `NetworkEntityState.Hp`/`MaxHp` of the `IsLocal` mirror | `Hp`, `MaxHp` | `HealthCaption` ("57/100"), `HealthFraction` (0..1) |
| `LocalTransform.Position` of the `IsLocal` mirror, quantized to 0.1 | `PosX`, `PosZ` | `PositionCaption` ("(12.3, 45.7)") |
| count of mirrors with kind `"player"` (or `IsLocal`) | `PlayersVisible` | `PlayersVisible` |
| count of all mirrors | `EntitiesVisible` | `EntitiesVisible` |
| presence of an `IsLocal` mirror | `HasLocalPlayer` | drives the "—" placeholders |

Nothing speculative: every field has a writer today (`NetworkViewCommandSystem` puts all three
components on every mirror it spawns). Connection state is deliberately **absent** — it lives on
the managed `NetworkClient`, not in ECS, so it belongs to a presenter subscribing to client
events, not to this bridge; adding it here would mean polling managed state per frame.

Hp reads `NetworkEntityState`, never `Cuvara.DOTS.Simulation.Health` — `Health` means "destroy
at zero" and `writeHealth` stays off (see DOTS-WIRING's trap list).

## Why a singleton `HudState`, not bridging `NetworkEntityState` directly

`EcsViewModelBridge` converts and pushes **every** entity its query matches, keeping one
`lastPushed` — it is shaped for "one component instance feeds one screen". Bridged directly,
every replicated entity's hp would be pushed in arbitrary order and the HUD would show whichever
came last. So a game-side aggregation system (`HudStateSystem`, `SimulationSystemGroup`) finds
the local player and counts the rest into one `HudState` singleton, and the bridge stays the
pure converter the package wants (`HudBridgeSystem` is a `Convert` override and nothing else).

**The change contract, end to end.** The bridge's chunk change filter reports any write, equal
or not — so `HudStateSystem` compares before it writes (`HudState : IEquatable`), and quantizes
position to the 0.1 the HUD can display. A frame in which nothing shown changed therefore costs
one query walk in the aggregator and *zero* work in the bridge, presenter, ViewModel, and
binding system. There is no per-frame UI work anywhere in this path.

## Lifecycle

`HudWorldBridge` (MonoBehaviour, `[RequireComponent(UIDocument)]`, sibling of `DotsWorldBridge`
in spirit) composes everything:

1. `Update` polls only until `World.DefaultGameObjectInjectionWorld` exists (same lazy pattern
   as `DotsWorldBridge`), then installs once and sets `enabled = false` — no per-frame cost after.
2. Install: `HudView` from the serialized `VisualTreeAsset` into the `UIDocument`;
   `HudPresenter(view, new HudViewModel())`; `HudEcsBootstrap.Install(world)` puts
   `HudStateSystem` into `SimulationSystemGroup` and `HudBridgeSystem` into
   `PresentationSystemGroup` (both `[DisableAutoCreation]` — explicit install, same world
   `DotsWorldBridge` uses, and tests install into a throwaway one); then
   `EcsSinkRegistration.Bind(bridge, presenter)` — registering is what enables the bridge and
   arms its one-shot catch-up push.
3. Teardown (`OnDestroy`), in reverse: **sink first** (a registered sink keeps
   Presenter → ViewModel → visual tree alive — the standard silent UI leak), then
   `HudEcsBootstrap.Uninstall` (destroys both systems; `HudStateSystem.OnDestroy` takes its
   singleton with it, so a reinstall catches up from fresh data, not stale), then the view.
   Unlike the dots view systems, these are destroyed on scene teardown: they exist only for
   the HUD and the default world outlives the scene.

Ordering guarantee: netcode drain and prediction run under `InitializationSystemGroup`, the
aggregator in `SimulationSystemGroup`, the bridge in `PresentationSystemGroup` — the HUD never
renders a frame behind the world, with no explicit ordering attributes against package systems.

## Host decision: UIDocument component, not a uitoolkit screen flow

The project registers no `ScreenManager`/screen flow anywhere (`GameLifetimeScope` wires
networking, Nakama, dots — no uitoolkit registration), so there is no screen host to enroll a
HUD presenter into. `HudWorldBridge` is therefore a lightweight `UIDocument` host, and it is
not VContainer-injected because it has no managed dependency to inject. When the screen flow
stands up, the Presenter + `EcsSinkRegistration` move into a screen's child scope (the
registration snippet is in `EcsSinkRegistration`'s remarks) and this component reduces to the
`UIDocument`. Nothing about the data path changes.

**Disk layout divergence, on purpose:** `docs/UI-ARCHITECTURE.md` sketches
`Assets/UI/Screens/<Name>/`. That tree does not exist yet, and the project's one UI assembly
(`NDC.Scripts.UI`) roots at `Assets/Scripts/UI/` — so the HUD lives at
`Assets/Scripts/UI/Hud/` (the per-screen file split is the contract's exactly: uxml, uss,
`IHudView`, `HudView`, `HudViewModel`). If/when screens move to `Assets/UI/`, the HUD moves
with them. A `.uxml-namespace` file pins the codegen namespace to `Scripts.UI.Hud` (folder
convention) rather than the asmdef root `Scripts.UI`.

## Assemblies and defines

| Assembly | Where | Gated by | References |
|---|---|---|---|
| `NDC.Scripts.UI` (existing) | `Assets/Scripts/UI/Hud/*` — ViewModel, view interface, View, UXML/USS, generated bindings | nothing — compiles with no ECS installed | `Cuvara.UIToolkit`, UniTask |
| `NDC.Scripts.UI.Hud.Ecs` (new) | `Assets/Scripts/UI/Hud/Ecs/` — state, systems, snapshot, presenter, bootstrap, host | `CUVARA_DOTS` + `CUVARA_NETCODE` (≥0.19.0) + `CUVARA_UITOOLKIT_ENTITIES` (com.unity.entities), each from its own `versionDefines` | both packages' assemblies + Entities |

Game assemblies reference both packages; neither package references the other — the one-way
arrows `docs/UI-ARCHITECTURE.md` and the vendor split require. `HudStateSystem` spells the
server kind `"player"` itself rather than referencing `DotsViewArchetypes.ServerKindPlayer`:
that constant lives in `NDC.Scripts.DI`, and the HUD bridge must not depend on the DI wiring
layer (the constant's own doc names the wire as the source of truth for the string).

## UXML codegen

`HudView.uxml` is enrolled: `Generated/HudView.uxml.g.cs` is committed (byte-exact to the
generator — verified against the codegen algorithm), the Editor regenerates it on every save of
the UXML, and the package's drift-check CLI covers `Assets/` in CI. A renamed element fails
compilation in `HudView.Bind`/`AssignQueries`, not silently at runtime.

## Tests

- `Assets/Tests/Editor` (EditMode): `HudViewModelTests` (notify-on-change, plain C#),
  `HudSnapshotTests` (the pure conversion: formatting, clamping, invariant culture,
  placeholders), `HudPresenterTests` (sink translation against a spy view; identical push is
  silent), `HudEcsLifecycleTests` (throwaway world: install/idempotence, aggregation from
  synthetic mirror entities, catch-up push, the quiet-frame-pushes-nothing guarantee, teardown
  leaves no system and no singleton).
- `Assets/Tests/Runtime` (new, PlayMode): `HudViewBindingTests` — the real `HudView` over the
  committed UXML on a live `UIDocument`, following the package's
  `BindableViewModelBindingTests`: property writes reach the bound elements through the binding
  system with no render call, converters included.

## Using it in a scene

Add a GameObject with `UIDocument` (any `PanelSettings`) + `HudWorldBridge`, assign
`HudView.uxml` to the `hudAsset` field. No DI registration is required. The netcode sample
scene draws its own IMGUI HUD; this component is for the game's own scenes as they grow.
