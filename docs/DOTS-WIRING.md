# com.cuvara.dots wiring

How the client wires the `com.cuvara.dots` package (ECS views, netcode adapter, prediction
driver) into its VContainer scopes. Wired 2026-09-04; the package was installed but orphaned —
present in `manifest.json` and `testables`, referenced by nothing — until this change.

## The shape

Two halves, split by lifetime, mirroring how networking is already wired:

| Half | Where | Owns |
|---|---|---|
| Root | `GameLifetimeScope` → `builder.RegisterDots(viewRoot: transform)` | MessagePipe brokers for the 5 package messages, `IViewAssetProvider`, `EntityViewRegistry` + `ChunkViewProvisioner` + `DotsViewBootstrap` install (via `RegisterDotsViews`), `ISimulationModel`, the session's single `LocalMovePredictor` |
| Scene | `MainSceneScope` build-callback-injects a `DotsWorldBridge` found in the scene | `DotsSimulationBootstrap`, the code-built `ViewConfigCatalog`, `TypeArchetypeResolver`, `DotsEntityView`, `DotsNetcodeBootstrap` + `DotsPredictionBootstrap`, the per-frame `WorldViewBinder.Tick`, input send + `RecordInput` |

All of it lives in `Assets/Scripts/DI/Dots/` (assembly `NDC.Scripts.DI`). The bridge hangs off
the **same** `NetworkClient` that `RegisterNetworking()` registers and `NetworkBootstrap`
prefers — there is one connection, one merged `WorldState`, one predictor.

### Registration order inside `RegisterDots` is load-bearing

`RegisterDotsViews` calls the package's `RegisterDotsMessaging`, whose MessagePipe adapters
resolve `IPublisher<T>` at container build. MessagePipe's `RegisterMessagePipe()` plus one
`RegisterMessageBroker<T>` per message type (`ViewSpawned`, `ViewDespawned`, `ChunkWarmed`,
`ChunkReleased`, `ChunkCascadeReleased`) must therefore already be on the builder — they are the
first thing `RegisterDots` does. This is also the project's first `RegisterMessagePipe` call:
future consumers add brokers to it, never a second `RegisterMessagePipe`.

### View provider: primitive fallback, not GameFoundation — deliberately

The package ships `RegisterGameFoundationViewProvisioning()`, which resolves GameFoundation's
`IAssetsManager` + `IObjectPoolManager`. **This project's container registers neither** —
`GameLifetimeScope` never calls `RegisterGameFoundation()`, and standing up the whole
GameFoundation service stack (audio, pooling, asset management) to feed the view layer is a
project decision, not DOTS wiring. Until then, `PrimitiveViewAssetProvider` (adapted from the
package's NetworkedPrediction sample) pools capsules/spheres for real, so the recycle path is
exercised. The swap instructions live on that class; the test
`ViewAssetProvider_IsThePrimitiveFallback` is the assertion to flip when it happens.

Similarly the `ViewConfigCatalog` is built in code (no authored art exists); when `ViewConfig`
assets exist, list them in a `ViewArchetypeLibrary` asset and give `DotsWorldBridge` a
serialized reference instead.

## Defines: they do not flow from package asmdefs

`versionDefines` are per-asmdef. `NDC.Scripts.DI` (and `NDC.Tests.Editor`) declare their own:

| Define | Set by |
|---|---|
| `CUVARA_DOTS` | `com.cuvara.dots` (any) |
| `CUVARA_DOTS_VCONTAINER` | `jp.hadashikick.vcontainer` (any) |
| `CUVARA_DOTS_MESSAGEPIPE` | `com.cysharp.messagepipe` (any) |
| `CUVARA_NETCODE` | `com.cuvara.netcode` ≥ 0.19.0 |
| `CUVARA_SHARED_GAMELOGIC` | `com.rpgmmo.shared-gamelogic` (any) |

All five activate in today's manifest/lock. The guards exist so removing an optional package
degrades to absent code, not compile errors — the same contract the package holds itself to.

## Traps this wiring respects (from the package docs)

- **`WorldViewBinder` uses the no-predictor overload with `DotsEntityView`.** The predictor
  overload hands `SetState` the *predicted* position, and the adapter stores what it receives as
  the authoritative `ReconciliationAnchor` — prediction would reconcile against its own output.
  ECS-side prediction is `LocalPredictionSystem`'s job.
- **Never feed one entity both `binder.Tick` and `SetStateAtTick`** — the ticked path buffers
  and re-interpolates the binder's already-interpolated output: double `TargetDelay`, remote
  entities smoothly twice as far behind.
- **One input owner.** `DotsWorldBridge.driveInput` samples, sends, and records the same tick
  stream. `NetworkBootstrap`'s `SendSyntheticInput` must be OFF in any scene hosting the bridge,
  or the server integrates input the predictor never saw.
- **Wire hp lands on `NetworkEntityState`, not `Health`** (`writeHealth` stays false): `Health`
  means "destroy at zero", and the server owns entity lifetime.
- **Teardown order** (bridge `OnDestroy`): prediction → netcode adapter → destroy the mirrored
  `NetworkEntity` entities (the default world outlives the scene) → `catalog.Dispose()` (blob) →
  destroy the code-built ScriptableObjects. The view bootstrap is root-scoped and is *not*
  uninstalled by the bridge.
- **Input backend**: this project runs Input System (new) only (`activeInputHandler: 1`), under
  which legacy `UnityEngine.Input` throws; the bridge reads `Keyboard.current` under
  `ENABLE_INPUT_SYSTEM` and falls back per define, same as the netcode DOTS sample.
- **ECS never touches UI Toolkit** — no `com.cuvara.uitoolkit` bridge here. The HUD side of
  that bridge exists now and lives in `Assets/Scripts/UI/Hud/` — see `docs/HUD-BRIDGE.md`; the
  contract itself is `docs/UI-ARCHITECTURE.md`.

## Tests

`Assets/Tests/Editor` (`NDC.Tests.Editor`, EditMode) — the project's first Assets-side test
assembly. `DotsRegistrationTests` builds a real container through `RegisterDots` against a
throwaway `World` and asserts the resolutions above; `DotsBootstrapLifecycleTests` runs the
bridge's install/uninstall sequence and asserts the group tree, the singletons, idempotence,
and that uninstall + `catalog.Dispose()` leaves no singleton and no live blob.

Run in the Editor via Test Runner, or batch mode EditMode as CI does for the packages.
