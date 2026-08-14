# Cuvara DOTS

Shared DOTS/ECS building blocks for Cuvara projects — reusable components, systems, jobs and authoring helpers built on Unity Entities, Burst, Collections and Mathematics.

**Status: early.** Ships the hybrid entity↔GameObject view layer and chunk-aware view
provisioning. Not yet compiled against a Unity Editor — see `CHANGELOG.md`.

## Layout

| Path | Assembly | Optional? | Purpose |
|---|---|---|---|
| `Runtime/` | `Cuvara.DOTS.Runtime` | no | View link components, registry, spawn/despawn/sync systems, provisioning interfaces |
| `Runtime.GameFoundation/` | `Cuvara.DOTS.GameFoundation` | yes — UniT + UniTask defines | `IViewAssetProvider` over `IAssetsManager` + `IObjectPoolManager` |
| `Runtime.GameLogic/` | `Cuvara.DOTS.GameLogic` | yes — `CUVARA_SHARED_GAMELOGIC` | `ISimulationModel` over `Shared.GameLogic`, plus all `Vec2`↔`float2` conversion |
| `Runtime.DI/` | `Cuvara.DOTS.DI` | yes — `CUVARA_DOTS_VCONTAINER` | `RegisterDotsViews()`, `RegisterSimulationModel()`, MessagePipe binding |
| `Editor/` | `Cuvara.DOTS.Editor` | no | Editor-only tooling and inspectors |
| `Tests/Runtime/` | `Cuvara.DOTS.Tests.Runtime` | no | Play-mode tests |
| `Tests/Editor/` | `Cuvara.DOTS.Tests.Editor` | no | Edit-mode tests |
| `Tests/Editor.GameLogic/` | `Cuvara.DOTS.Tests.GameLogic` | yes — `CUVARA_SHARED_GAMELOGIC` | Constants parity + golden vectors through the seam |

The two optional assemblies are gated by asmdef `versionDefines` + `defineConstraints`, the same
way `com.gdk.core` gates `GDK_VCONTAINER`. With VContainer absent, `Cuvara.DOTS.DI` is not
compiled and the core still works — you construct `EntityViewRegistry` yourself and call
`DotsViewBootstrap.Install(world, registry)`. With GameFoundation/UniT absent, you implement
`IViewAssetProvider` over whatever pool you do have. **The core assembly references neither**, and
installs against its four pinned Unity dependencies alone.

## View configuration

Author a `ViewConfig` per kind of view and list them in a `ViewArchetypeLibrary` under the names the
server uses. At session start, build the catalog and publish it:

```csharp
var catalog = new ViewConfigCatalog();
catalog.Build(library);
catalog.Install(world);              // publishes ViewConfigTableReference

// Warm what the catalog needs, using its own pool sizes:
foreach (var (key, size) in catalog.PoolSizesByKey())
    await provisioner.PrewarmChunkAsync("chunk-12-4", new[] { key }, countPerKey: size);

// Spawn by archetype name — resolve once, carry the index:
entityManager.AddComponentData(entity, new EntityViewRequest { ViewKey = "goblin" });
entityManager.AddComponentData(entity, new ViewConfigRef { Index = catalog.IndexOf("goblin") });
```

The bare-key path still works exactly as before: an entity with only `EntityViewRequest` and no
`ViewConfigRef` behaves as it always did. `catalog.Dispose()` releases the blob.

## System groups

Every package system is `[DisableAutoCreation]` and created by `DotsViewBootstrap.Install(world, registry)`.
Groups are `public` and are the ordering contract; the systems inside them are `internal` and will change.

```
InitializationSystemGroup                     [Unity]
├── NetcodeSystemGroup                        (empty — v0.3 snapshot apply)
└── ProvisioningSystemGroup                   UpdateAfter(NetcodeSystemGroup); empty
SimulationSystemGroup                         [Unity]
├── GameplaySystemGroup                       UpdateBefore(TransformSystemGroup)
│   ├── MovementSystemGroup                   (empty)
│   ├── LifecycleSystemGroup                  UpdateAfter(MovementSystemGroup); empty
│   └── DotsEndSimulationCommandBufferSystem  OrderLast
└── TransformSystemGroup                      [Unity]
PresentationSystemGroup                       [Unity]
└── ViewSystemGroup
    ├── ViewLifecycleGroup                    structural: views appear/disappear
    │   ├── EntityViewDespawnSystem           first — freed instances reusable this frame
    │   └── EntityViewSpawnSystem             UpdateAfter(EntityViewDespawnSystem)
    └── ViewTransformSyncGroup                UpdateAfter(ViewLifecycleGroup)
        └── EntityViewTransformSyncSystem
```

Order your own systems against the groups: `[UpdateAfter(typeof(ViewSystemGroup))]`.

## Usage

```csharp
// DI (VContainer + GameFoundation present), after RegisterGameFoundation:
builder.RegisterGameFoundationViewProvisioning();
builder.RegisterDotsViews(viewRoot);

// Warm everything a chunk needs, then drop it when the chunk unloads:
await provisioner.PrewarmChunkAsync("chunk-12-4", new[] { "goblin", "torch" }, countPerKey: 8);

// Views still standing on the chunk's expiring keys are despawned first, then the assets go.
// The entities survive without views; a ChunkCascadeReleased message reports how many.
var result = provisioner.ReleaseChunk("chunk-12-4");   // keys another chunk still lists survive
Debug.Log($"released {result.KeysReleased} keys, cascaded {result.ViewsDespawned} views");

// Simulation seam — identical call sites with or without com.rpgmmo.shared-gamelogic:
builder.RegisterSimulationModel();
if (model.IsAuthoritative)          // false => no shared logic; do NOT predict
    model.TryMove(in entity, input, dt, in bounds, out var predicted);

// Give an entity a view:
entityManager.AddComponentData(entity, new EntityViewRequest { ViewKey = "goblin" });
```

## Installation

### Git URL

Add to your project's `Packages/manifest.json`:

```json
"com.cuvara.dots": "https://github.com/Cuvara/com.cuvara.dots.git#v0.6.2"
```

Or via **Window > Package Manager > + > Add package from git URL**:

```
https://github.com/Cuvara/com.cuvara.dots.git#v0.6.2
```

### Embedded

Clone into your project's `Packages/com.cuvara.dots/` folder for local development.

### Running this package's tests in your project

A git-URL install lands in `Library/PackageCache`, and **Unity does not compile a package's test
assemblies unless the project asks for them**. Nothing warns you: the tests do not fail, they are
simply absent — no `Cuvara.DOTS.Tests.*` assembly in `Library/ScriptAssemblies`, and the Test Runner
filtered to `Cuvara.DOTS` reports *no tests found*, which reads exactly like a package with no tests.

Add the package to `testables` in your project's `Packages/manifest.json`, as a sibling of
`dependencies`:

```json
{
  "dependencies": { "com.cuvara.dots": "https://github.com/Cuvara/com.cuvara.dots.git#v0.6.2" },
  "testables": [ "com.cuvara.dots" ]
}
```

The `testables` entry this package declares in its own `package.json` does **not** substitute for
that: the consuming project's manifest is what makes the Test Runner build the assemblies.

Editing the manifest is not always enough on its own — an Editor that has already resolved the
package keeps its resolution cached, so the assemblies stay missing until the Editor is restarted.
Verify by checking that `Library/ScriptAssemblies/Cuvara.DOTS.Tests.Editor.dll` exists, not by
trusting the manifest edit.

## Requirements

- Unity 6000.3 or newer

Resolved automatically via `package.json`:

| Package | Version |
|---|---|
| `com.unity.entities` | 1.4.8 |
| `com.unity.burst` | 1.8.30 |
| `com.unity.collections` | 2.6.8 |
| `com.unity.mathematics` | 1.3.2 |

## Conventions

- `com.unity.jobs` is deprecated — it is merged into `com.unity.collections`.
- Use `IJobEntity` or `SystemAPI.Query` instead of the obsolete `Entities.ForEach`.
- Prefer unmanaged `ISystem` over managed `SystemBase`.

## License

MIT — see [LICENSE](LICENSE).
