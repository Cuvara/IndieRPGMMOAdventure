# Changelog

All notable changes to the Cuvara DOTS package will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Documentation

- **Consumers must list this package in `testables` or its tests do not exist for them.** Written up
  in the README install section. A git-URL install lives in `Library/PackageCache` and Unity builds a
  package's test assemblies only when the project's `Packages/manifest.json` names it in `testables`;
  the entry this package declares in its own `package.json` does not substitute. The failure is
  silent — no `Cuvara.DOTS.Tests.*` in `Library/ScriptAssemblies`, and a Test Runner filtered to
  `Cuvara.DOTS` reporting *no tests found*, which is indistinguishable from a package that ships no
  tests. Editing the manifest is necessary but not sufficient: an Editor that already resolved the
  package caches that resolution until restarted, so verification is the presence of
  `Cuvara.DOTS.Tests.Editor.dll`, not the manifest edit.
- README install snippets updated from `#v0.1.0` to `#v0.6.2`; the old ones pinned a tag from before
  any runtime code existed.
- *Known debts* in `ROADMAP.md` upgraded from suspicion to fact: measured in the consuming project,
  a full EditMode run passes 139/139 while `Cuvara.DOTS` contributes **zero** — those tests belong to
  other packages. Also records that `Cuvara.DOTS.Tests.GameLogic` carries a second, deliberate gate
  (`CUVARA_SHARED_GAMELOGIC` plus a `Shared.GameLogic` assembly reference), so it can legitimately
  contribute no cases when the optional dependency is absent, and that no test references
  `Cuvara.DOTS.DI` or `Cuvara.DOTS.GameFoundation`.

### Changed

- **`ROADMAP.md` rewritten from the tree instead of from the plan.** It described the package as
  mid-v0.2.0 while `package.json` read 0.6.2, and omitted everything shipped since — cascade
  release, the messaging seam, the system group tree. Now organised Done / In progress / Planned,
  with shipped work carrying the real version it shipped in and unshipped work carrying only an
  order, since a milestone number assigned in advance goes stale the moment the order changes.
  Two items are stated as having **no code in the tree** rather than as in progress: ViewConfig +
  data setup, and the simulation components and systems. The `ISimulationModel` seam is recorded as
  a deliberate reorder — pulled ahead to settle the `Shared.GameLogic` question early — not as
  work that happened by accident. A *Known debts* section names the never-executed test suite, the
  MessagePipe and GameFoundation adapters that no test exercises, the empty
  `Cuvara.DOTS.Editor` assembly, and the load-bearing `.meta` files. Net 13 lines shorter.

### Verified

- **The package has now been compiled and run in a real Unity 6 Editor** (6000.3.9f1, URP project,
  embedded install). Every line below is an observed result, not an expectation.
  - `Cuvara.DOTS.Runtime`, `.DI`, `.GameFoundation`, `.GameLogic`, `.Editor` and the sample assembly
    compile with **zero errors** at 0.6.2. The 0.6.2 asmdef fix is sufficient on its own — no further
    references were needed.
  - `Samples~/HybridViews` imports, its scene opens with all three root objects, and the
    `HybridViewsSample` MonoBehaviour **binds** (pinned `.meta` GUIDs survive the import) with its
    serialized values intact: `_stepSeconds = 4`, `_warmCountPerKey = 4`, three view definitions.
  - The full sample timeline ran green in play mode. Observed: key de-duplication (`cube` listed
    twice → refcount 1); 10 entities spawned with 2 deferred on the cold key; shared `sphere`
    reaching refcount 2; **cascade release** reporting `Released=True, ViewsDespawned=2` while
    `sphere` survived at refcount 1; a second release returning `Released=False, WasTracked=False`
    (no-op, not a refusal); and a clean finish — `instantiated=12, acquires=10, recycles=10,
    key teardowns=3, live views=0, tracked chunks=0`. Every acquire was matched by a recycle.
  - Transform sync confirmed by sampling live view positions mid-run: cubes orbiting at r≈4, y=0 and
    spheres at r≈6.5, y=1.5, matching the `OrbitMotion` values the entities carry.

### Fixed

- The sample's closing log line claimed "acquires above instantiations is the pool doing its job",
  which the real run contradicts: prewarming instantiates 4 per key up front, so instantiations
  (12) exceed acquires (10) by design. It now states what the numbers actually mean.

### Notes

- **Running the sample from an unfocused Editor needs `Application.runInBackground`.** With the
  Editor in the background the player loop does not tick — the observed symptom is play mode active
  with `Time.time == 0` and `frameCount == 1`, so the sample logs step 1 and appears to hang. This is
  Editor behaviour, not a package or sample defect. Now written up as a troubleshooting section in
  the sample README, alongside the duplicate-sample case: two version folders under
  `Assets/Samples/Cuvara DOTS/` collide on the assembly name
  `Cuvara.DOTS.Samples.HybridViews` and block play mode until the stale one is deleted, because
  importing a sample does not remove the copy a previous version left behind.

## [0.6.2] - 2026-08-14

### Fixed

- **First compile: five errors, from two missing assembly references.**
  - `Unity.Transforms` added to `Cuvara.DOTS.Runtime`, `Cuvara.DOTS.Tests.Runtime`,
    `Cuvara.DOTS.Tests.Editor` and the sample assembly. `LocalToWorld` and `TransformSystemGroup`
    live in `Unity.Transforms`, which is a separate assembly from `Unity.Entities` — this was fallout
    from the `LocalTransform` → `LocalToWorld` change, where the reference was never added, and it
    had been asserted rather than checked.
  - `UniTask` added to `Cuvara.DOTS.DI`. MessagePipe's `ISubscriber<T>` surface exposes `UniTask` in
    its signatures, so a reference to it is required to compile against that interface at all.
- Audited every asmdef's `references` against the types its files actually use, rather than fixing
  only the assemblies that happened to error. Three of the four `Unity.Transforms` gaps were in
  assemblies the compiler had not reached yet.

### Notes

- The package produced **no** assemblies until its `.meta` files were committed: a git-URL install
  lands in `Library/PackageCache`, which Unity treats as immutable, so it would not generate them and
  silently ignored every source file. Nothing in 0.1.0–0.6.1 had ever been compiled. Add a `.meta`
  alongside every new file or the same silence returns.

## [0.6.1] - 2026-08-14

### Changed

- **The view group is nested rather than flat**, so a consumer can inject work between "views exist"
  and "views are positioned" without naming a package system:
  `ViewSystemGroup` → `ViewLifecycleGroup` (despawn, then spawn) and `ViewTransformSyncGroup`
  (`UpdateAfter(ViewLifecycleGroup)`).
- **Despawn runs before spawn again.** Recycling a dead entity's view first makes the freed pool
  instance available to the same frame's spawns, so a frame that destroys ten entities and creates
  ten more instantiates nothing; the reverse order grows the pool to the sum instead of the maximum.
- The sample ticks `ViewLifecycleGroup` rather than the whole `ViewSystemGroup` when it needs a
  despawn flushed early — the narrower group is exactly what it needs, and ticking the parent would
  run the transform sync twice in one frame.

## [0.6.0] - 2026-08-14

### Changed

- **Chunk release now cascades instead of refusing.** 0.5.0 refused a release while live views stood
  on the keys it would drop; the approved behaviour is to take those views down first. `ReleaseChunk`
  finds every entity whose `EntityViewLink` points at an instance of an expiring key, puts it through
  the **ordinary despawn path** — recycle the instance, drop the handle, clear the link — and only
  then lets the reference count reach zero and calls `IViewAssetProvider.Release`. That is the exact
  inversion of the original bug, where the asset died first and the links were left dead with nobody
  to clean them. Only keys the chunk is the *last* referencer of are cascaded: a key another chunk
  still lists is not being released, so its views are in no danger.
- `ChunkReleaseResult` now carries `ViewsDespawned` and `KeysReleased`; the refusal fields are gone.
  `ReleaseAll` returns total views cascaded. `ChunkReleased.LiveViewCount` became `ViewsDespawned`.
- `ILiveViewCounter` is replaced by `IViewCascadeSink` — the provisioner no longer needs to *count*
  live views, it needs to *end* them, and one seam is better than two overlapping ones.

### Added

- `ChunkCascadeReleased(ChunkId, KeyCount, ViewsDespawned)` — published whenever a release tears down
  views. Surviving without a view is the intended outcome of a streaming unload, but a consumer
  cannot infer it from anything else, so shipping it silently would make an entity that quietly
  stopped rendering indistinguishable from a bug. A log line is not enough: the code unloading a
  region is usually not the code owning the entities in it.
- `EntityViewCascade` — the `IViewCascadeSink` implementation, and the only type that knows both the
  registry and the world.
- `EntityViewRegistry.TryGetKey`, so the cascade can map a handle back to the key it came from.
- `EntityViewCascadeTests` (play mode, real `World`) and reworked `ChunkReleaseSafetyTests`.

### Accepted limitations

- **The cascade is synchronous in its managed half and deferred in its structural half.** Recycling
  the instance and dropping the handle happen inside the `ReleaseChunk` call; removing
  `EntityViewLink`/`EntityViewLinkCleanup` is recorded into `DotsEndSimulationCommandBufferSystem`,
  because an `EntityManager` structural change from arbitrary consumer code would invalidate any
  chunk iteration in flight. **The state a caller observes mid-cascade**, until that buffer plays back
  at the end of the next `GameplaySystemGroup`: GameObjects already gone and assets already released,
  while the entities still carry an `EntityViewLink` whose handle no longer resolves. Nothing
  misbehaves in that window — `ApplyTransform` ignores an unknown handle and `Despawn` on a dropped
  one is a no-op — but query for a live view via `EntityViewRegistry.Get`, not by the presence of the
  component. Pinned by a test.
- **No respawn loop**: the cascade removes the link and does not re-add an `EntityViewRequest`, and
  the spawn system acts only on requests, so a cascaded entity stays view-less until something
  deliberately requests a view again. Pinned by a test.
- A provisioner built without an `IViewCascadeSink` still releases unconditionally; the constructor
  documents it as unsafe for streaming and a test pins it, so it is a decision rather than an accident.
- **Nothing has been compiled.** The MessagePipe adapters remain entirely unexercised.

## [0.5.0] - 2026-08-14

### Fixed

- **Releasing a chunk no longer destroys live views.** `ChunkViewProvisioner` refcounts *chunks*,
  and a live view is held by an entity it has never heard of, so a release whose key count hit zero
  destroyed on-screen pooled instances while `EntityViewRegistry` kept their handles and the entities
  kept an `EntityViewLink` that could never resolve or respawn — views silently gone, no error, no
  recovery, on the ordinary streaming path. `ReleaseChunk` now **refuses**: it returns a
  `ChunkReleaseResult` carrying `LiveViewCount` and a `BlockingKey`, logs a warning, publishes
  `ChunkReleased` with `Released == false`, and changes nothing. Refusal was chosen over deferring
  (reports success for something that did not happen) and over cascading despawns (puts entity
  lifetime decisions in the asset layer, which cannot see entities) because it is the only one whose
  failure mode a consumer can see. Live views on a key another chunk also references do not block —
  that release would not tear the key down. Backed by `ChunkReleaseSafetyTests`.
- **A never-warmed view key no longer defers in silence.** `EntityViewSpawnSystem` counts deferrals
  per key and warns once after 300 frames, so a typo'd key is distinguishable from a slow load. The
  deferral policy itself is unchanged.
- **Views spawn already rotated.** `EntityViewRegistry.Spawn` took the entity's rotation from
  `LocalToWorld` instead of always using identity, which showed one frame of wrong facing.

### Added

- **MessagePipe messaging, without a core dependency.** `Cuvara.DOTS.Runtime` declares two
  one-method interfaces — `IDotsPublisher<T>` and `IDotsSubscriber<T>` — plus four message types
  (`ViewSpawned`, `ViewDespawned`, `ChunkWarmed`, `ChunkReleased`), and never names a MessagePipe
  type. The adapters binding them to MessagePipe's `IPublisher<T>`/`ISubscriber<T>` live in
  `Cuvara.DOTS.DI` behind a `versionDefines` gate on `com.cysharp.messagepipe`. **With MessagePipe
  absent, publishing is a no-op via `NullDotsPublisher<T>` — that is the documented behaviour, and no
  signal bus is written to fill the gap.** Nothing inside the package subscribes; the messages exist
  for consumers.
- `ILiveViewCounter`, implemented by `EntityViewRegistry`, so the provisioning layer can ask about
  live views without knowing what a view or an entity is.
- `ChunkViewProvisioner.IsChunkLoaded` — the "have the loads finished?" question callers actually
  want.

### Changed

- **Breaking: `Cuvara.DOTS.VContainer` renamed to `Cuvara.DOTS.DI`** (folder `Runtime.VContainer/` →
  `Runtime.DI/`), matching the namespace it already used.
- **Breaking: `IsChunkWarm` renamed to `IsChunkTracked`.** It returns true the instant
  `PrewarmChunkAsync` is called, before anything has loaded, which every caller would read as "loads
  finished". `IsChunkLoaded` is that question.
- **Breaking: `ReleaseChunk` returns `ChunkReleaseResult`, not `bool`**, and `ReleaseAll` returns the
  number of chunks it could not release. A bool collapsed "unknown chunk" and "refused" into the same
  value, which is how a streaming bug hides.
- `RegisterDotsViews()` now owns the `ChunkViewProvisioner` registration and wires the registry in as
  its `ILiveViewCounter`; `RegisterGameFoundationViewProvisioning()` no longer registers one, since
  registering it in both places would silently give whichever ran last.

### Accepted limitations

- **A provisioner built without an `ILiveViewCounter` still releases unconditionally.** The parameter
  is optional so the type stays usable without the view layer; the constructor documents it as unsafe
  for streaming, and a test pins the behaviour so it is a decision rather than an accident.
- **Nothing has been compiled.** The MessagePipe adapters are unexercised — no test assembly declares
  a MessagePipe dependency.

### Added

- **`Samples~/HybridViews` sample ("Hybrid Views"), declared in `package.json`.** A `MonoBehaviour`
  bootstrap that installs the view layer into the default world, a `PrimitiveViewAssetProvider`
  implementing `IViewAssetProvider` over `GameObject.CreatePrimitive` and a `Stack<GameObject>` pool
  — so the sample runs on a bare install with only the four pinned dependencies and proves the
  standalone claim — orbiting entities that make the transform sync visible, a mid-run despawn that
  exercises the recycle path, and a narrated chunk warm/release in which two chunks share a key: the
  shared key survives the first release and is torn down only on the second. A cold key is left
  deferred on purpose so the "invisible for a few frames" behaviour can be seen rather than read
  about. Sample `README.md` states which optional packages are *not* needed and why.
- **A ready-to-play scene for that sample**, `Samples~/HybridViews/Scenes/HybridViewsSample.unity`:
  camera aimed at the origin, directional light, and the bootstrap GameObject with its three view
  definitions already filled in — import, open, press Play. It carries **no render-pipeline-specific
  components** (no `UniversalAdditionalCameraData`, no volume, no URP asset reference) so it opens
  clean under URP, HDRP or the built-in pipeline. `.meta` files with fixed GUIDs ship alongside the
  sample's files, because a scene that references a script by GUID breaks if the GUID is regenerated
  at import time.
- **The hazard this sample surfaced** — releasing a chunk whose entities are still alive used to
  destroy those views through `IViewAssetProvider.Release` while the `EntityViewRegistry` kept their
  handles and the entities kept an `EntityViewLink` that could never resolve or respawn. Writing the
  sample is what made it visible; it is **fixed in this same version** (see *Fixed* above) and
  replaced by the cascade in 0.6.0. Kept here only as the origin of that fix.
- Sample updated for the 0.4.0 transform change: entities created from code now add `LocalToWorld`
  explicitly (`TransformSystemGroup` writes into it but does not add it — baking would, runtime
  creation does not), and `OrbitMotionSystem` names its group instead of relying on the default.

## [0.4.0] - 2026-08-14

### Added

- **The package's full system group tree**, all positions fixed now so a consumer's `[UpdateAfter]`
  resolves today and does not change meaning as systems land:
  `NetcodeSystemGroup` → `ProvisioningSystemGroup` in `InitializationSystemGroup`;
  `GameplaySystemGroup` (`UpdateBefore(TransformSystemGroup)`) containing `MovementSystemGroup`,
  `LifecycleSystemGroup` and `DotsEndSimulationCommandBufferSystem` (`OrderLast`);
  `ViewSystemGroup` in `PresentationSystemGroup`. Groups other than the view group are empty in this
  version.
- `DotsEndSimulationCommandBufferSystem` — the package's own ECB, played back at the end of
  `GameplaySystemGroup` and therefore *before* `TransformSystemGroup` and long before presentation.
  Unity's `EndSimulationEntityCommandBufferSystem` plays back after the transform systems, which
  would leave a window in which a view could be synced against an entity that died this frame.
- `DotsViewBootstrap.InstallSystems(World)`, called by `Install`.

### Changed

- **Breaking: the view systems moved into `ViewSystemGroup` and became `internal`.** The group tree
  is the ordering contract; a public system name is an accidental API promise. `InternalsVisibleTo`
  grants access to the two test assemblies only.
- **Breaking: transform sync reads `LocalToWorld`, not `LocalTransform`.** `LocalTransform` is
  relative to the parent, so a parented entity's view was placed at local coordinates — correct for
  root entities and wrong for everything else. `LocalToWorld` is what `TransformSystemGroup` computed
  earlier in the same frame and is right in both cases. Uniform scale is recovered as the length of
  the matrix's first basis vector; non-uniform and sheared transforms are not represented.
- **Breaking: every package system and group is `[DisableAutoCreation]`** and created explicitly by
  `DotsViewBootstrap`. Unity's default bootstrap creates every non-disabled system in *every* world,
  so a multi-world setup would have had two view groups driving one `EntityViewRegistry` and
  double-spawning every entity.
- Spawn now runs before despawn (was despawn-first in 0.2.0), matching the agreed tree.
- Group names dropped their `Cuvara` prefix — `Cuvara.DOTS.*` already scopes them. `Dots` is kept on
  the ECB system precisely so it cannot be mistaken for Unity's.

### Resolved

- **The open question about optional asmdef references is answered: Unity drops a name-based
  reference to an assembly that does not exist.** Verified locally rather than assumed — four
  installed `UniT.*.DI` asmdefs in the consuming project reference `"Zenject"`, `com.svermeulen.extenject`
  is not in the project, and it compiles. Those assemblies are not even excluded by `defineConstraints`,
  so this covers the stronger case. `Cuvara.DOTS.GameFoundation`'s references to `UniT.*` / `UniTask` /
  `VContainer` and `Cuvara.DOTS.VContainer`'s reference to `Cuvara.DOTS.GameLogic` are therefore safe
  when those packages are absent, and the GUID-reference fallback is not needed.

### Accepted limitations

- **`OrderFirst` is not used where a sibling's `UpdateAfter` already encodes the same order.**
  Entities sorts `OrderFirst` members into a separate batch and then drops ordering relations between
  that batch and normal members, with only a warning — so the explicit relation is the one that holds.
  Ordering is unchanged from the agreed tree; only the mechanism differs. `OrderLast` is kept on the
  ECB system, which has no sibling relation to conflict with.
- **No `FixedStepSimulationSystemGroup`.** The server is authoritative and the client integrates
  nothing; a fixed 60 Hz group would re-time server-paced data for no determinism gain. It changes
  when prediction lands, and the rate will derive from the server tick rate.
- **Nothing has been compiled.** The `DotsEndSimulationCommandBufferSystem` singleton boilerplate is
  the highest-risk item — it is unsafe code against an API whose exact shape varies by Entities version.

## [0.3.0] - 2026-08-14

### Added

- **Optional `Shared.GameLogic` seam.** `ISimulationModel` plus the value types it speaks in
  (`SimEntity`, `SimBounds`, `SimConstants`, `SimMoveResult`) live in `Cuvara.DOTS.Runtime` with no
  define guards at all. The package owns the abstraction; `Shared.GameLogic` is one implementation
  behind it and never the interface. Consumer code is byte-identical whether or not
  `com.rpgmmo.shared-gamelogic` is installed.
- **`Cuvara.DOTS.GameLogic`** — optional assembly holding `SharedGameLogicSimulation`, which
  delegates movement to `MovementSystem.TryMove` and combat to `CombatLogic.CalculateDamage` /
  `InRange`, and the `Vec2`↔`float2` / `SimBounds`→`MapBounds` conversions. All conversion lives
  here because it can only live here: `Shared.GameLogic.asmdef` is `noEngineReferences: true` and
  can never learn what a `float2` is. Gated by `versionDefines` + `defineConstraints` on
  `com.rpgmmo.shared-gamelogic`, so it is not compiled at all when the package is absent.
- **`PassiveSimulationModel`** — the absent-dependency path. It applies authoritative state and
  predicts nothing, reporting `IsAuthoritative == false` and `SimMoveResult.Unavailable`.
- `RegisterSimulationModel()` in `Cuvara.DOTS.VContainer`, holding the **single `#if`** that decides
  between the two implementations, driven by the asmdef `versionDefine` rather than a hand-set
  Player Settings define — the `GDK_VCONTAINER` pattern from `com.gdk.core`.
- Tests in a new `Cuvara.DOTS.Tests.GameLogic` assembly, itself constrained on
  `CUVARA_SHARED_GAMELOGIC` so it only compiles when the dependency is present: field-by-field
  parity of `SimConstants` against `GameConstants`, and the shared `movement.json` golden vectors
  replayed **through the seam** and compared bit-for-bit.

### Changed

- `package.json` bumped to 0.3.0. Pinned Unity dependencies unchanged — `com.rpgmmo.shared-gamelogic`
  is an optional integration, not a dependency.

### Accepted limitations

- **`PassiveSimulationModel` refuses rather than approximates.** It returns 0 damage, `false` for
  `InRange` and an unchanged position. It does not re-derive the server's movement rule: that rule
  is not "position += direction * speed * dt" — `MovementSystem.Integrate` splits the multiply into
  separate float locals to deny an FMA contraction, and `Vec2.SqrMagnitude` casts every intermediate
  because C# permits higher-precision evaluation and .NET's RyuJIT and Unity's Mono JIT choose
  differently. A re-implementation would be one ULP wrong and drift silently. Callers must check
  `IsAuthoritative`.
- **`SimConstants.Unavailable` is all zeros**, not plausible defaults. With the shared package absent
  there is no source of truth, and inventing one is the literal-copy trap in disguise.
- **`SimEntity` carries no identity.** `EntityState.Id` / `.Type` are `string` and therefore unusable
  in Burst or an `IComponentData`; identity stays ECS-side as a `FixedString64Bytes`. `SnapshotMerger`
  (netcode owns it) and `ValidationLogic` (managed delegate over string keys, server-shaped) are
  deliberately not exposed.
- **The DI assembly is still named `Cuvara.DOTS.VContainer`**, not `Cuvara.DOTS.DI`, since it already
  shipped in 0.2.0 — its root namespace is `Cuvara.DOTS.DI` and the registration lives there.
- **Nothing here has been compiled.** No Unity Editor was available.

## [0.2.0] - 2026-08-14

### Added

- **Chunk-aware view provisioning.** `IViewAssetProvider` is the one seam the view layer has onto
  asset loading and pooling; `ChunkViewProvisioner` warms and releases whole key sets on behalf of a
  spatial chunk or region, reference-counting keys so two chunks sharing a prefab cannot unload it
  from under each other. A key counts once per chunk regardless of how many times the chunk lists
  it, releasing an unknown or already-released chunk is a no-op, and re-warming an existing chunk
  diffs — a key in both the old and new set never transiently reaches zero.
- **`Cuvara.DOTS.GameFoundation`** — optional adapter implementing `IViewAssetProvider` over the
  GameFoundation / UniT `IAssetsManager` + `IObjectPoolManager` pair. No loader, cache or pool of its
  own: a second pool over the same prefabs would fight the first over recycling.
- **Hybrid entity↔GameObject views.** `EntityViewRequest` → `EntityViewLink` (+ the
  `EntityViewLinkCleanup` cleanup component), a managed `EntityViewRegistry` side-table reached from
  `ISystem` structs through the managed `EntityViewRegistryReference` singleton, and three systems in
  `PresentationSystemGroup`: spawn, despawn and per-frame `LocalTransform` → `Transform` sync.
- **`Cuvara.DOTS.VContainer`** — optional `RegisterDotsViews()` registration extension, mirroring
  `GameFoundationVContainer.RegisterGameFoundation`. The caller supplies the `IViewAssetProvider`.
- **Explicit system group hierarchy.** `CuvaraViewPresentationGroup` (in `PresentationSystemGroup`)
  contains `CuvaraViewLifecycleGroup` (despawn, then spawn) and `CuvaraViewTransformSyncGroup`
  (`UpdateAfter` the lifecycle group). No package system sits in a default group or relies on
  implicit creation order, and consumers order their own systems against the package groups rather
  than against individual systems.
- Tests: reference-count semantics of the chunk provisioner (edit mode), the entity→view
  spawn/despawn/sync lifecycle against an isolated `World` (play mode), and reflection assertions
  on the group attributes (edit mode) — a misplaced `[UpdateInGroup]` never fails a build and shows
  up only as views trailing the simulation by a frame.

### Changed

- `package.json` bumped to 0.2.0. Its four pinned Unity dependencies are unchanged — VContainer,
  UniTask and GameFoundation/UniT are **not** dependencies, only optional integrations.

### Removed

- The `PackageMarker` placeholder in `Runtime/` and both placeholder smoke tests, now that those
  assemblies hold real code. `Editor/PackageMarkerEditor.cs` stays: `Cuvara.DOTS.Editor` still has
  no real code, and an assembly definition over an empty folder produces no assembly.

### Accepted limitations

- **Warm counts only grow.** If chunk A warms 8 instances of a key and chunk B warms 2, releasing A
  leaves 8 warm. Shrinking would destroy pooled instances a live chunk may be about to spawn, which
  is the hitch prewarming exists to avoid. Memory returns when the count reaches zero.
- **The sync split is structural, not a measured win.** The collect half is an `IJobEntity` writing
  blittable samples; the apply half is a flat main-thread loop, because `Transform` cannot be touched
  off the main thread or Bursted. This has not been profiled — at low view counts the scheduling
  overhead may cost more than it saves. The `Complete()` before the drain is a sync point every frame.
- **Cold keys defer rather than load.** An entity whose view prefab has not been warmed stays
  invisible for a few frames instead of forcing a synchronous load. The hitch belongs in the chunk
  prewarm, where it is asynchronous and expected.
- **View handles are never reused.** A recycled id would let a stale `EntityViewLink` address someone
  else's view — a bug that reads as a rendering glitch. Wrap-around at `int.MaxValue` is not defended.
- **Nothing here has been compiled.** No Unity Editor was available; see the 0.2.0 notes in the
  commit message for what the first compile is most likely to catch.

## [0.1.0] - 2026-08-14

### Added

- Initial package scaffold. No runtime gameplay code — assemblies and metadata only.
- `package.json` declaring `com.cuvara.dots` for Unity 6000.3, MIT licensed, depending on
  `com.unity.entities` 1.4.8, `com.unity.burst` 1.8.30, `com.unity.collections` 2.6.8 and
  `com.unity.mathematics` 1.3.2 — the versions already used by the consuming Unity project.
- Four assembly definitions: `Cuvara.DOTS.Runtime`, editor-only `Cuvara.DOTS.Editor`, and the
  Unity Test Framework assemblies `Cuvara.DOTS.Tests.Runtime` (play mode) and
  `Cuvara.DOTS.Tests.Editor` (edit mode), the latter two gated on `UNITY_INCLUDE_TESTS`.
- Placeholder `PackageMarker` types in each assembly folder. Unity produces no assembly for an
  assembly definition whose folder holds no C# file, which would break every reference to it;
  the markers keep the graph resolvable until real code lands.
- Smoke tests in both test assemblies asserting the runtime assembly is referenceable.
- `README.md`, `CHANGELOG.md`, MIT `LICENSE` and a Unity-package `.gitignore`.
