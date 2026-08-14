# Roadmap

What is in `com.cuvara.dots` today, what is being built, and what is planned. **Written from the
tree, not from the original plan** — an item is Done only if the code exists here.

**Version labels:** shipped work carries the real version it shipped in, matching `package.json`
and `CHANGELOG.md`. Unshipped work carries **no version label**, only an order, because a milestone
number assigned in advance is a guess that goes stale the moment the order changes.

## Scope

**Hybrid** building blocks: simulation runs in ECS, visuals are GameObject/MonoBehaviour. The
package does not render entities. Consumers wire it up through **VContainer**.

Two rules constrain everything below.

- **Standalone install.** The package resolves and compiles against its four pinned dependencies
  alone — `com.unity.entities`, `com.unity.burst`, `com.unity.collections`, `com.unity.mathematics`.
  Anything needing more lives in a separate assembly gated by `versionDefines` +
  `defineConstraints`, and is absent rather than broken when its dependency is.
  Verified by `Samples~/HybridViews`, which runs on the four alone.
- **Dependency direction.** `com.cuvara.dots` may depend on `com.cuvara.netcode`. The reverse is
  forbidden, in every release. Netcode's `IEntityView` stays three methods.

## Done

| Feature | Shipped in | Contents |
|---|---|---|
| Entity↔view link and transform sync | 0.2.0, reworked 0.4.0 | `EntityViewRequest` → `EntityViewLink` (+ cleanup component), managed `EntityViewRegistry` side table, spawn/despawn systems, per-frame `LocalToWorld` → `Transform` sync. Systems are `internal`; the group tree is the ordering contract. |
| Chunk-aware view provisioning | 0.2.0, 0.5.0, 0.6.0 | `IViewAssetProvider` seam, `ChunkViewProvisioner` refcounting keys per chunk with prewarm/release. Release **cascades** through the ordinary despawn path (0.6.0) rather than stranding live views. |
| Package-owned system group tree | 0.4.0, nested 0.6.1 | `NetcodeSystemGroup` → `ProvisioningSystemGroup`; `GameplaySystemGroup` (`MovementSystemGroup`, `LifecycleSystemGroup`, `DotsEndSimulationCommandBufferSystem`); `ViewSystemGroup` → `ViewLifecycleGroup` + `ViewTransformSyncGroup`. Groups outside the view branch are still empty. |
| Optional `Shared.GameLogic` seam | 0.3.0 | `ISimulationModel` + `SimEntity`/`SimBounds`/`SimConstants`/`SimMoveResult` always compile; `Cuvara.DOTS.GameLogic` implements over the shared library, `PassiveSimulationModel` covers its absence. One `#if` in the whole package. Guarded by constants-parity and golden-vector tests. |
| Messaging without a MessagePipe dependency | 0.5.0 | `IDotsPublisher<T>`/`IDotsSubscriber<T>` + `ViewSpawned`, `ViewDespawned`, `ChunkWarmed`, `ChunkReleased`, `ChunkCascadeReleased`. MessagePipe adapters live in `Cuvara.DOTS.DI` behind a version gate; publishing is a no-op when absent. |
| GameFoundation asset provider | 0.2.0 | `IViewAssetProvider` over UniT's `IAssetsManager` + `IObjectPoolManager`. No loader, cache or pool of its own. |
| Hybrid Views sample + scene | 0.6.2 | Bootstrap, self-contained primitive provider, orbiting entities, despawn/recycle, narrated chunk warm/release. Ready-to-play scene, pinned `.meta` GUIDs, troubleshooting notes. |

**Ordering decision, not an accident:** the `ISimulationModel` seam (0.3.0) was pulled ahead of the
remaining v0.2.0-era items on purpose, to settle the `Shared.GameLogic` question early.

## In progress

Nothing. The last shipped version, 0.6.2, closed the asmdef fixes that made the package compile for
the first time.

## Planned, in order

1. **ViewConfig + data setup** — **no code in the tree.** ScriptableObject authoring (asset key,
   pool size, scale, offsets) converted to `IComponentData`, a blob table for many-per-archetype,
   named archetype definitions. Runtime authoring, not subscene baking: consumers spawn from server
   snapshots at runtime. This is the last piece of the original hybrid core, and until it lands a
   consumer configures views by hardcoding keys.
2. **Simulation components and systems** — **no code in the tree.** `Lifetime`, `Health`,
   `MoveToward`, `SpinSpeed`, `MoveData` with their `ISystem` counterparts, decoupled from demo
   singletons and from a hardcoded command-buffer system. `MovementSystemGroup` and
   `LifecycleSystemGroup` exist and are empty, waiting for exactly these.
3. **Netcode `IEntityView` adapter** — separate assembly gated on `com.cuvara.netcode`, arrow
   pointing one way only. Plus an ECS → MonoBehaviour event queue for one-shot request entities,
   held until a second consumer exists to shape its API.
4. **2D** — tile data in ECS (chunked grid in a blob asset, plus lookup / neighbourhood /
   line-of-sight queries) and an ECS sort key drained to `SpriteRenderer.sortingOrder` in the same
   main-thread pass as transform sync. Sprite view pooling needs nothing new: a prefab with a
   `SpriteRenderer` already flows through provisioning. Rendering stays on
   `Tilemap`/`TilemapRenderer` GameObjects.

## Known debts

- **The test assemblies are not compiled in the consuming project, so no test in this package has
  ever run.** Established, not suspected: a full EditMode run in the consumer passed 139/139, but
  filtering to `Cuvara.DOTS` returned *no tests found*, and `Library/ScriptAssemblies` holds no
  `Cuvara.DOTS.Tests.*` assembly — those 139 belong to other packages. "Tested" here does not mean
  "written but unrun"; it means the code is not built at all.
  - **Cause:** a git-URL install lives in `Library/PackageCache`, and Unity only builds a package's
    test assemblies when the consuming project lists it in `testables` in `Packages/manifest.json`.
    The `testables` entry in this package's own `package.json` does not substitute for that.
    Documented in the README install section, because every consumer hits it and none will guess it:
    a package whose tests silently do not exist in the consumer is the same class of failure as a
    missing `.meta`.
  - Editing the manifest is necessary but not always sufficient — an Editor that already resolved
    the package caches that resolution, so the assemblies stay absent until it restarts. Verify by
    the presence of `Library/ScriptAssemblies/Cuvara.DOTS.Tests.Editor.dll`, not by the manifest.
- **`Cuvara.DOTS.Tests.GameLogic` has a second gate** beyond `UNITY_INCLUDE_TESTS`: it is also
  constrained on `CUVARA_SHARED_GAMELOGIC`, defined only when `com.rpgmmo.shared-gamelogic` is
  installed, and it references that package's `Shared.GameLogic` assembly. Correct by design — the
  tests cannot run without the library they compare against — but it means this file can contribute
  zero cases while every other gate is green, and that is not a failure to chase.
- **The MessagePipe and GameFoundation adapters are unexercised by any test.** No test assembly
  references `Cuvara.DOTS.DI` or `Cuvara.DOTS.GameFoundation`, so both are compile-checked only.
- **`Cuvara.DOTS.Editor` contains only `PackageMarkerEditor.cs`** — the assembly exists to hold
  editor tooling that has not been written.
- **`.meta` files are load-bearing.** A git-URL install lands in `Library/PackageCache`, which Unity
  treats as immutable and will not generate metas into; a new file without one is silently ignored
  rather than erroring.

## Out of scope

- **Entity rendering wrappers** over Entities.Graphics. Visuals are GameObjects; the package never
  creates a rendered entity.
- **A new asset loader, cache, or GameObject pool.** GameFoundation owns these, and a second pool
  would contend with the first over the same prefabs.
- **Wrappers over `SystemAPI` singleton access.** Unity's API is already the abstraction.
- **Scene bootstrap** — cameras, lights, ground planes. That belongs in a sample.
- **2D collision.** There is no DOTS 2D physics; 3D `Unity.Physics` on a plane or `Physics2D` on the
  GameObject side are project decisions. The tile blob is a broadphase over static tiles, not a
  physics engine, and will not pretend otherwise.
- **Snapshot merge, interpolation, transport, codec, entity-handle interning.** `com.cuvara.netcode`
  owns them; a second copy of the merge rule is the divergence the shared-logic boundary prevents.

## Measurement caveat

Standalone Windows and Linux builds use Mono2x with managed stripping disabled, so a green result
there exercises neither IL2CPP nor the stripper and cannot validate AOT behaviour, `link.xml`
preservation, or Burst codegen. Any performance or AOT claim must be backed by an Android or WebGL
build, with stripping raised above the default Minimal.
