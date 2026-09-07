# Cuvara DOTS improvement plan

Date: 2026-09-07. Status: proposed; implementation has not started in this task.

## 1. Objective and baseline

Make the existing Cuvara DOTS feature set correct, explicit to integrate, measurable and suitable for a real multiplayer gameplay slice. Complete the lifecycle contracts before adding new systems or claiming performance capacity.

Baseline inspected: `com.cuvara.dots` 0.27.1, resolved cache fingerprint `dfcfddc1867dfb8b8a6af8313eb0d332343fb3fc`. Source evidence below is relative to this package root unless explicitly marked as client code. README/ROADMAP contain stale status statements, so source takes precedence.

The workspace is changing concurrently: the client manifest now points Netcode at `feat/reconnect-policy`, and its changelog records newer auth cancellation work. Earlier workspace-audit auth findings must not be treated as unchanged. Capture actual package commits and rerun compatibility checks before implementation. Do not replace or revert that work.

This plan covers DOTS and its integration boundaries. The previous backend security, reward durability and map-transfer plan remains a separate workstream. No live Unity tests or performance profiles were run for this document. Confirmed source behavior is distinguished from risks needing reproduction.

## 2. Ownership rules

| Owner | Responsibilities |
|---|---|
| Cuvara DOTS | ECS components/groups, entity/view lifetime, pooling/provider contracts, chunk provisioning, presentation data, optional physics helpers, ECS adapters |
| Cuvara Netcode | Transport, protocol/codec, merged world state, reconnect, prediction/interpolation algorithms, network timing |
| Shared.GameLogic | Deterministic rules shared with server; DOTS calls these rather than duplicating them |
| Cuvara UIToolkit | UI views/bindings, UI collection lifecycle and reusable presentation controls |
| Game client | Input mapping, scene/session orchestration, art/Addressables loading, gameplay-specific animation and UI, project budgets |
| Backend/Nakama | Authoritative gameplay state and grants; Nakama built-in auth/session, wallet/ledger, storage and leaderboard APIs remain preferred |

- Do not duplicate Nakama economy/auth/storage inside DOTS.
- Keep DOTS -> Netcode dependency one-way. Do not add Unity.Entities requirements to Netcode core.
- Keep optional dependencies gated in separate assemblies. Core must still work without DI, Netcode, GameFoundation or Unity.Physics.
- Package source fixes belong in the package repository, then a release and manifest/lock adoption. Never deliver a fix by editing Library/PackageCache.
- Prefer one asset/pool owner per prefab key. A host adapter may use an existing pool instead of activating a second independent pool.

## 3. Priority definitions

- P1: correctness, lifecycle, ownership and advertised feature gaps; required before depending on the affected feature.
- P2: integration robustness, profiling and measured performance work; required before setting release budgets/capacity.
- P3: conditional expansion; implement only when a concrete gameplay workflow needs it.

All tests below are planned acceptance criteria, not completed results. Numeric scenarios are proposed workloads, not claims about supported capacity.

## 4. Foundation backlog

### D01 - P1: publish an accurate feature and support matrix

Evidence: README/ROADMAP still describe older scope; CHANGELOG mentions a CollisionEventSystem absent from the inspected source. MinimapBuffer references a MinimapDataSystem that is absent.

- Classify each feature as implemented, integrated in this client, sample-only, data contract only, or planned.
- For every module record dependencies, installation API, update group, singleton requirements, ownership and teardown procedure.
- Correct stale install URLs/versions, performance claims and references to missing types.
- Record tested package combinations and platforms; separate source presence from runtime acceptance.

Done when each supported feature maps to real code plus an integration example/test, with no unsupported feature described as complete.

### D02 - P1: correct pooling identity, ownership and disposal

Evidence: `Runtime/Provisioning/PooledViewAssetProvider.cs:160-182` ignores whether removing from `_active` succeeded and routes return by a substring in GameObject.name. A duplicate return can enqueue the same instance twice. Renaming can lose its pool identity. At :206-222 Dispose clears active tracking without reclaiming active objects outside the pool root, and destroys the supplied root regardless of who created it.

- Track instance -> key and acquired/pooled state explicitly; names are diagnostic only.
- Define duplicate return, foreign instance return, external destruction and prefab re-registration policies.
- Preserve caller-owned root objects. Destroy only provider-owned roots and instances according to an explicit ownership contract.
- Decide how outstanding acquired instances are reclaimed at Dispose; no silent tracking loss.
- Make disposal idempotent and reject use after disposal with clear errors.
- Honor cancellation before PrewarmAsync/AcquireAsync perform work. Prewarm currently accepts a token but executes its instantiate loop synchronously without checking it.
- Reset reusable transform/animation/particle/subscription state through a narrow host-facing lifecycle contract where needed.
- Distinguish inactive pool cap from active-view admission budget; the former does not bound total instance count.

Acceptance: duplicate release never allows two entities to acquire the same object; rename does not change ownership; caller root survives Dispose; acquired and pooled counts reconcile after teardown; canceled work creates no unexpected instances. Cover destroyed pooled/active instances and replace-key behavior.

### D03 - P1: make chunk provisioning cancellation and ownership deterministic

Evidence: ChunkViewProvisioner has shared-key reference counts and asynchronous prewarm; cascade sink is optional even though its documentation warns that omission is unsafe for streaming.

- Specify legal state transitions and failure/retry behavior for chunk warm/release.
- Test release while warming, cancellation after partial completion, warm failure, immediate re-warm with the same ID, and two chunks sharing keys.
- Prevent obsolete async completion from marking a released/replaced chunk warm.
- Define thread affinity: Unity object work remains on the main thread, even if loading is asynchronous.
- Ensure streaming configuration installs a cascade sink or explicitly rejects unsafe release.
- Separate chunk-owned assets from session/global assets; destroy views before dropping the last relevant asset reference.
- Reconcile counters and events after every failure path; no hidden retained dictionaries or negative references.

Acceptance: surviving chunks retain shared assets; canceled/released chunks leave no leaked references or resurrected views; repeated warm/release returns to the baseline counts.

### D04 - P1: explicit bootstrap and teardown for each optional module

Evidence: DotsViewBootstrap installs core view/overlay systems. CameraFollowSystem and PhysicsMovementBridge use DisableAutoCreation but need separate installation. Existing per-session client wiring is in `Assets/Scripts/DI/Dots/DotsWorldBridge.cs`.

- Provide documented install/uninstall entry points for camera, physics and any completed minimap/events module.
- Validate required singleton/config/provider/dependency presence before use.
- Test install twice, uninstall twice, replacing a registry, scene reload and World destruction.
- Record root scope versus scene/session scope ownership. No references to an old World/session may survive into the next one.
- Complete dependent jobs before disposing their native containers; distinguish temporary disable from permanent World disposal.
- Verify recursive system-group ordering after manual installation, including subgroups, rather than relying only on attributes.
- Test two Worlds simultaneously; one World's uninstall must not remove the other's views/services.

Acceptance: deterministic order, no duplicate singleton/system registration, no duplicate views and no native-container leak after repeated lifecycle transitions.

### D05 - P1: validate configuration and archetype stability

Evidence: ViewConfig carries fixed-size keys and sorting data; ViewConfigCatalog rebuilds indexed data; EntityArchetypePreset/ArchetypeFactory construct selected component sets.

- Validate empty/duplicate/overlong keys, missing prefabs, invalid numeric values and unknown entity-type mappings before entering gameplay.
- Specify whether a catalog is immutable per session or versioned for rebuild. Old indices must never silently resolve to a different view.
- Define prefab replacement semantics for active and pooled instances.
- Test preset component combinations, initialization values, batch creation and absent optional modules.
- Keep runtime validation available where configs arrive dynamically; do not depend solely on the Editor inspector.

Acceptance: invalid configurations produce actionable errors, correct configs produce consistent entities, and catalog changes cannot silently swap visuals.

## 5. Complete existing feature contracts

### D06 - P1: lifecycle event production and semantics

Evidence: `Runtime.Netcode/EntityLifecycleEvents.cs` declares NetworkEntitySpawned/Despawned; source search found no publisher. Existing ViewSpawned/Despawned are a different lifecycle.

- Define network entity present/absent separately from a visual instance acquired/released. AOI exit is not death; view culling is not entity removal.
- Publish network lifecycle notifications at the committed ECS application boundary, with enough identity to handle despawn after entity removal.
- Define ordering, late subscriptions, error isolation, teardown behavior and whether events are ephemeral.
- Do not publish duplicate spawn on repeated full snapshots or duplicate despawn during teardown.
- Add an actual consumer sample and optional MessagePipe wiring. Core must not require MessagePipe.

Acceptance: scripted full/delta snapshot, AOI exit/reentry and reconnect sequences yield the documented exact event sequence.

### D07 - P1 if physics is enabled: finish physics integration

Evidence: PhysicsBodyFactory and SpatialQuery exist, but collision/trigger messages are only structs in the inspected package; no collector system was found.

- Add a correctly ordered Unity.Physics collision/trigger collector if these events are supported.
- Use stable entity identity including generation/version; Entity.Index alone can identify a different entity after reuse.
- Specify enter/stay/exit semantics, aggregation of multiple contacts, pair ordering and missing/destroyed entities.
- Own and dispose collider BlobAssetReferences explicitly; test shared collider ownership and teardown.
- Validate shape dimensions, mass, filters and dynamic/static/kinematic behavior.
- Ensure physics-driven movement and direct Transform movement do not both integrate the same entity. Specify interaction with prediction and fixed-step timing.
- Reuse Unity.Physics queries; avoid building a second physics engine or claiming server-authoritative collision from a client-only helper.

Acceptance: expected contacts/triggers arrive in order, reused entity indices do not misroute events, motion integrates once, collider memory returns to baseline.

### D08 - P1 if exposed: finish minimap, overlay and 2D sorting

Evidence: MinimapEntry/MinimapBuffer exist without their producer. ViewOverlaySystem produces anchor data. ViewSortingKey is populated but sorting is not applied to SpriteRenderer.

- Add minimap producer with position, stable identity and presentation category; host owns map projection and rendering.
- Specify whether minimap data covers visible entities only or another authoritative visibility set. Do not reveal entities omitted by server AOI.
- Define native buffer allocation/disposal and clear behavior when the last matching entity disappears; consumers must not see stale entries.
- For overlays define world-to-screen conversion owner, behind-camera handling, distance filtering, refresh cadence and UI instance recycling.
- Either implement and test SpriteRenderer sorting in a dedicated optional path or clearly mark sorting fields unsupported until a 2D consumer exists.
- Keep world simulation independent of whether an overlay/minimap is enabled.

Acceptance: spawn, movement, despawn, empty-world and scene reload leave the UI consistent with the current entity set; no stale markers or retained native buffers.

### D09 - P1 if camera module is used: camera behavior and lifecycle

- Define no-target/multiple-target behavior and target switching rather than allowing GetSingleton failures.
- Support a supplied camera/config so split-screen or non-main cameras do not require a package fork.
- Verify follow order after interpolation/prediction and reset smoothing state after teleport/reconnect.
- Test zero delta time, extreme distance, MaxSpeed clamp, zero SmoothTime and frame-rate variation. Compare behavior with a proven Unity damping primitive before maintaining custom math.
- Verify finite values and predictable behavior when the target is destroyed.

Acceptance: camera target changes are safe, speed limiting matches its documented meaning, paused frames do not introduce jumps and teleport policy is explicit.

## 6. Network and performance robustness

### D10 - P1 correctness, P2 tuning: snapshot ingestion and transform ownership

Evidence: DotsEntityView owns an unbounded ConcurrentQueue; NetworkViewCommandSystem drains it fully. Current host binder polls merged world state, so this is a capacity risk, not proof of a current backlog. DOTS supports both unticked binder output and ticked ECS interpolation.

- Instrument commands pending, oldest command age and drain time before changing the queue.
- Specify producer/consumer thread affinity; ConcurrentQueue does not make accompanying HashSet/Dictionary access thread-safe.
- Give each session/generation its own ingestion ownership so late data cannot respawn old entities.
- If bounding work, preserve structural ordering and enough timestamped samples for interpolation. Movement/state coalescing must not discard lifecycle or one-shot action semantics.
- Enforce one interpolation path per entity and one writer for local predicted transforms.
- Reconcile using authoritative position, matching ack tick and server speed; never feed a predicted/rendered position back as server truth.
- Keep replicated HP separate from local HealthDeath removal unless explicitly opted in.
- Test duplicate/reordered snapshots, reconnect reset, toggling prediction and AOI reentry. Algorithms stay in Netcode/Shared.GameLogic.

Acceptance: no duplicate interpolation delay, no competing Transform writers, bounded recovery after a burst, and no stale-session entities.

### D11 - P2: instrument and optimize hybrid presentation

Evidence: EntityViewTransformSyncSystem schedules then immediately completes the gather and applies transforms serially; ViewOverlaySystem adds a separate completion.

- Add profiler markers for snapshot apply, spawn/despawn, gather, job wait, Transform apply, overlays, minimap, camera and pool miss/prewarm.
- Record managed GC/frame, native allocations, active/pooled views by key, spawned/despawned per frame, queue high-water mark and deferred-spawn age.
- Profile steady movement separately from AOI churn and chunk loading. Include Animator/render/GPU cost in the host; ECS-only time is not total frame time.
- Evaluate batching structural changes, growable scratch buffers and selective Transform writes only after a repeatable baseline identifies them as material.
- Preserve synchronization correctness when moving/removing Complete calls. Jobs touching the same data must still finish before the main-thread consumer.
- Evaluate culling/LOD/update-frequency tiers only if visible count or animation cost warrants them. Visual culling must not stop authoritative state processing.

Acceptance: improvements lower measured p95/p99 frame cost or allocations in the target workload without incorrect visuals, lost updates or increased latency.

### D12 - P2: integrate actual assets and ownership in the host

Evidence: client DotsRegistration currently chooses PrimitiveViewAssetProvider and builds view config in code.

- Select the production provider: existing GameFoundation pool or package pooled provider with a host asset loader. Avoid two owners for the same instances.
- Define Addressables handle leases and scope lifetime; load once where appropriate, release only after all dependent views/pool instances are done.
- Replace primitive-only acceptance with at least one representative animated prefab and one effect prefab.
- Author ViewConfig/library assets and validate missing/mismatched keys in the build workflow.
- Test the real MainScene path, scene transitions, reconnect and teardown; sample success alone is insufficient.

Acceptance: representative assets survive churn without reset artifacts or growing reference counts, and the real boot scene uses the intended integration.

## 7. Tests, platforms and release process

### D13 - P2: build a focused verification matrix

- Core-only package: no optional integration dependency required to compile or exercise views/simulation.
- Netcode + Shared.GameLogic: snapshot apply, local prediction, interpolation and lifecycle tests.
- Unity.Physics enabled/absent: correct optional compilation plus runtime movement/contact tests when enabled.
- VContainer/MessagePipe/GameFoundation present/absent: test actual registrations and lifetime disposal, not just compilation.
- EditMode: pure contracts, key validation, ownership, queue/event ordering and presets.
- PlayMode: pooling activation, World/group ordering, scene transitions, camera, native lifetime and physics.
- Android IL2CPP: real boot flow, reflection/stripping where relevant and profiler run. Desktop Mono is a separate target, not a substitute.
- WebGL only if retained as a release target: coordinate the missing browser realtime transport with Netcode/backend; no DOTS-only test can close that gap.
- CI must expose absent/skipped test assemblies and preserve result artifacts. Test-count floors supplement, not replace, scenario assertions.

Acceptance: each supported module/platform combination has explicit results; unsupported combinations are clearly documented.

### D14 - P2: reliable package delivery

- Locate/inspect the actual package source checkout and its current branch before edits; respect its local instructions and dirty state.
- Keep core fixes, optional modules and host adoption in separate reviewable changes.
- Pin compatibility evidence to exact commits; use stable release tags for accepted consumer builds after any feature-branch testing.
- Include .meta files, asmdef gates, migration notes, changelog, supported dependencies and refreshed sample compile checks.
- Validate manifest and lock resolution together. Roll back by restoring a known package set and compatible host changes, not by editing cache files.
- Reproduce upgrade in a clean consumer project and in this project; preserve old public contracts or document intentional breaks.

Acceptance: clean install, upgrade and rollback produce the recorded package combination and runnable boot flow.

## 8. Conditional expansion after the foundation

These are design candidates, not requirements to finish every release.

| ID | Candidate | Trigger and boundary | Acceptance |
|---|---|---|---|
| E01 | View culling/LOD | Profiling shows too many costly views; use a presentation visibility policy distinct from server AOI | Hidden views reduce cost; reentry restores correct pose/state; simulation continues |
| E02 | Spawn/prewarm budget per frame | Real AOI/chunk bursts exceed frame budget | Priority entities appear promptly; backlog drains within agreed latency; no starvation |
| E03 | Animation bridge | First gameplay prefab needs consistent movement/action/death states | Pooled objects reset; no repeated attack/death trigger on snapshot replay; adapters remain optional |
| E04 | VFX/SFX event bridge | First gameplay slice needs one-shot effects | Event identity/replay policy prevents duplicates; cancellation, target despawn and audio/pool cleanup work |
| E05 | Authoring tools | Designers manage enough presets/assets that manual validation becomes error-prone | Inspector/build validation identifies missing assets and bad component combinations before runtime |
| E06 | Chunk streaming controller | Project needs automatic spatial load/unload | Host chooses regions and hysteresis; DOTS provisioner owns asset references, not map gameplay rules |
| E07 | 2D tile/grid/LOS | A specific 2D workflow requires it | Reuse suitable existing Unity/domain facilities; do not create speculative navigation or physics systems |

Skills, quests, inventory rules and enemy AI remain game/backend code until actual reuse justifies extraction. Account, economy, social and leaderboard features prefer Nakama's built-in APIs. No new ECS framework, networking algorithm or custom economy service is part of this plan.

## 9. Execution order and review units

| Phase | Work | Dependency | Initial effort estimate |
|---|---|---|---|
| A | D01 baseline/support matrix and reproductions for D02 | None | 0.5-1 day |
| B | D02 pool fixes, D03 chunk lifetime, D04 bootstrap, D05 config | A | 3-6 days |
| C | D06 events; D07 physics; D08 UI data; D09 camera | B; implement advertised/required modules first | 4-8 days |
| D | D10 network integration and D12 real assets | B; coordinate current Netcode branch | 2-4 days |
| E | D11 profiling/targeted optimization, D13 matrix, D14 release | Accepted modules from C/D | 3-6 days |
| F | E01-E07 as selected by gameplay evidence | Stable baseline and a real consumer | Estimate each separately |

Planning envelope for A-E: about 13-25 engineering days for one engineer, not a delivery commitment. Device access, CI setup, external package changes and failure investigation may alter this. D13 tests accompany each earlier phase; E consolidates the platform/performance matrix rather than postponing all testing until the end. No assumption of parallel agents or extra staff.

Suggested PR boundaries: pool identity/disposal; chunk cancellation; bootstrap/config; network lifecycle events; physics completion; overlay/minimap; camera; network ingestion; host provider adoption; measured performance changes; documentation/release matrix. Keep each behavior fix paired with its regression test.

## 10. Workload and acceptance record

Record device/OS, Unity/package commits, backend image, FPS target, quality settings and build backend for each run. Proposed starting workloads:

1. Idle/steady movement with 50, 200 and 500 representative visible entities, scaling only while stable.
2. AOI entry/exit burst and repeated chunk warm/release with shared asset keys.
3. Pool exhaustion, canceled asset load, failed load and external instance destruction.
4. Three distinct clients observing each other, moving and reconnecting to a controlled backend.
5. Scene/session cycling and a sustained run long enough to expose retained instances/native buffers; start with 100 lifecycle cycles and a 30-minute soak.
6. Representative Android build with actual prefabs, overlays and animation; pure-ECS benchmark kept as a separate diagnostic.

Capture frame CPU/GPU p50/p95/p99, job wait, GC allocation, native/managed memory, active/pooled views, deferred spawn latency, queue age and reconciliation/visual correctness. A 60 FPS target allows 16.67 ms for the entire frame; 30 FPS allows 33.33 ms. Allocate a DOTS share after measuring the rest of the game, not by consuming the entire budget.

Mandatory correctness gates: no duplicated pooled lease, no destruction of host-owned roots, no stale-session writes, no duplicate one-shot effects, no native leaks, correct event/Transform ordering and repeatable teardown. Performance gates must state the exact device/workload. Do not adopt unsupported 100M-entity or fixed-speedup claims from sample descriptions.

## 11. Decisions to settle before implementation

- Minimum Android device, target FPS and representative art complexity.
- Expected visible players/enemies and burst size, separately from total map population.
- Which optional features are release requirements: physics contacts, minimap, 2D sorting, camera.
- Production asset/pool provider and ownership boundaries between root and scene scopes.
- Chosen remote interpolation path and compatible Netcode release after current branch work.
- Which gameplay event IDs/ordering the server can supply for one-shot animation/effects.
- Whether WebGL remains a supported gameplay target.

Progress can begin with core lifecycle fixes and regression tests before these product choices are settled. Do not silently invent their answers when setting release budgets.

## 12. Definition of complete

The package is ready for the selected scope when advertised features have working producers/consumers, all installed modules have explicit ownership/teardown, regression and target-platform tests pass, representative gameplay meets its agreed budgets, and a clean consumer can install the released version from its docs. Future expansion follows a demonstrated gameplay need, not an arbitrary feature count.
