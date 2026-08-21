# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

IndieRPGMMOAdventure — Unity 6 (6000.3.9f1) game project targeting Android, WebGL, Windows, and Linux. Early-stage, built on a template with DI and async patterns. .NET Standard 2.1.

### Scripting backend is per-target — do not assume IL2CPP everywhere

Measured 2026-08-12, not inferred from a template default:

| Target | Backend | Managed stripping |
|---|---|---|
| Android | IL2CPP | Minimal |
| WebGL | IL2CPP | Minimal |
| Standalone (Windows/Linux) | **Mono2x** | **Disabled** |

This matters for anything with AOT or reflection constraints. A StandaloneWindows64
build — the default and quickest one to produce — exercises **neither** IL2CPP nor the
stripper, so it cannot validate `link.xml` preservation or AOT behaviour, and a green
result there is misleading rather than merely incomplete. Test those on Android or
WebGL, and note that stripping is set to Minimal, the least aggressive level, so
raising it to High is the honest setting for a real stripping test.

## Build & CI

### Local Build
Unity Editor opens the project directly. No CLI build tool beyond Unity's batch mode.

### Running several clients against one backend

One client proves nothing about multiplayer. Three processes on one map, each its own
Nakama user, is the smallest arrangement in which "A sees B" is a real observation.

Build a Windows player (Mono2x, stripping disabled — the quickest target):

```bash
"/mnt/c/Program Files/Unity/Hub/Editor/6000.3.9f1/Editor/Unity.exe" \
  -quit -batchmode -nographics \
  -projectPath 'E:\SecretProject\IndieRPGMMOAdventure' \
  -buildTarget Win64 \
  -executeMethod PlayerBuilder.Build \
  -buildOutput 'E:\SecretProject\IndieRPGMMOAdventure\Builds\MultiClient' \
  -bootScene 'Assets/Samples/Cuvara Netcode/0.16.3/DOTS Sample/Scenes/DOTSSample.unity' \
  -logFile 'E:\SecretProject\IndieRPGMMOAdventure\Builds\multiclient-build.log'
```

`-buildOutput` rather than `BUILD_OUTPUT_DIR`: a variable exported in a WSL shell is
not in the environment of a Windows `Unity.exe`. Paths handed to `Unity.exe` must be
Windows paths for the same reason. The player lands in
`Builds/MultiClient/StandaloneWindows64/IndieRPGMMOAdventure.exe`. (`BuildConfig/*.json`
lists only `MainScene`; that path is the CI toolkit's, not this one.)

**`-bootScene` is what makes this a netcode-sample player, and omitting it gives you the
wrong game.** `PlayerBuilder` builds every enabled scene in `EditorBuildSettings` and the
player boots index 0 — which is `Assets/Scenes/MainScene.unity`, because that is what a
release build must boot. `-bootScene` moves the named scene to index 0 for this build
only; the enabled set, and therefore what ships inside the player, is unchanged, and
nothing is committed. Without the flag the three windows come up in MainScene, never
authenticate, and the harness looks broken for a reason that has nothing to do with
networking.

The path must match the enabled scene path exactly — an unmatched value is a hard build
error, not a silent fall-back to index 0. Note the version number in it: the sample lives
under the vendored netcode version (`0.16.3` today) and moves every time that package is
re-vendored, so check the directory before copying the command.

Then launch the instances:

```bash
Tools/run-clients.sh --exe Builds/MultiClient/StandaloneWindows64/IndieRPGMMOAdventure.exe \
  --count 3 --gateway-host <host> --gateway-port <port> \
  --nakama-host <host> --nakama-port <port> --nakama-key <key> \
  --map map_01 --status-url http://<gs-host>:<gs-port>/status --tile
```

No address is baked in. The game server is an Agones pod whose port is assigned at
scheduling time, so every address is a parameter — see `BackendCommandLine` for the
full flag set and the `CUVARA_*` environment fallbacks.

**`--nakama-key` is not optional any more.** It defaults to `defaultkey`, which is
what every backend used until the keys were rotated on 2026-08-20 — each cluster now
has its own. Read the one for the backend you are pointing at:

```bash
kubectl --context k3d-rpg-dev get secret nakama -n rpg-k8s-data \
  -o jsonpath='{.data.NAKAMA_SERVER_KEY}' | base64 -d; echo   # k3d-rpg-stg for staging
```

The verification harness in `rpg-mmo-server` does this for you — `checks_client.sh`
exports `CUVARA_NAKAMA_SERVER_KEY` from the cluster before it launches a player — so
this applies to running clients **by hand**.

Getting it wrong is loud, which is worth knowing before you go hunting. Measured
against staging on 2026-08-20 with the flag omitted:

```
[DOTSNet] Authenticating device=mc-...-1
[DOTSNet] FATAL: Cysharp.Threading.Tasks.UnityWebRequestException: HTTP/1.1 401 Unauthorized
```

The player launches and its window opens, so it *looks* like the silent-failure
shape described below — but the log names the 401 outright and no `IN WORLD` line
follows. **Read the log before suspecting area-of-interest**: a client missing from
the world because it never authenticated and one that authenticated and sees nobody
are different faults, and only the second one is about AOI.

**Kill the players before rebuilding.** A running player holds
`lib_burst_generated.dll` open and the build fails on it: `Tools/run-clients.sh --kill`.

#### Telling a real pass from three isolated clients

Three clients that each see only themselves is a failure that looks like success — the
windows are up, the logs say "IN WORLD", and nothing is wrong on the surface. Check all
of these, not the first one:

| Where | Expect | What the wrong value means |
|---|---|---|
| each client's window | three capsules, not one | remote entities never arrived |
| each client's log, `[DOTSNet] Auth OK, user_id=` | three **different** user ids | shared identity; the logins evicted each other |
| game server `/status` | `players_online: 3` | fewer means a client never joined, or was evicted |
| game server `/metrics` | `gameserver_players_online{map_id="map_01"} 3` | same, and it is the counter the load tests read |
| Redis `KEYS 'session:*'` | three entries | the gateway holds one session per user |
| Redis `KEYS 'servers:*'` | **one** entry for the map | one entry per game server, never per player; three means the clients landed on three servers and could never see each other |
| Redis `KEYS 'player:location:*'` | three entries | per-user location records |

The area-of-interest radius is 50 units. Clients that wander further apart than that are
mutually invisible and the server is *correct* to omit them, so judge visibility in the
first seconds after all three have joined, before they drift.

### CI/CD (GitHub Actions)
- `.github/workflows/unity-build.yml` — thin dispatcher delegating to `unity-build-workflows` submodule pipeline
- Platforms: Android (AAB/ARM64), WebGL (Brotli), Linux64, LinuxServer, Windows64
- Environments: production, staging, development
- Build configs live in `BuildConfig/` — `base.json` merged with environment overlays (`development.json`, `staging.json`, `production.json`)
- Build scripts: `Assets/BuildScripts/Editor/PlayerBuilder.cs`, `Assets/BuildScripts/Editor/AddressableBuilder.cs`
- Build gates: max 500MB, fail at 25% size increase, no missing references/scripts

### Verifying a player build actually contains your change

`strings` on a Unity player's managed assembly **will not find your string literals**.
.NET stores them as UTF-16, and plain `strings` scans for ASCII only — so a correct,
freshly built binary looks stale and you go hunting for a build problem that does not
exist. Use the UTF-16 mode, and always with a control:

```bash
D=<build>/<Product>_Data/Managed
strings -el "$D/Assembly-CSharp.dll" | grep -c "MY_NEW_MARKER"   # the change
strings -el "$D/Assembly-CSharp.dll" | grep -c "SOMETHING_OLD"   # the control
```

The control matters more than the target: it distinguishes "my change is missing" from
"my search is broken". Without it, a zero is unreadable.

**String literals and type names live in different encodings**, and searching for the
wrong one produces a confident false negative:

| Looking for | Where it lives | Command |
|---|---|---|
| a string literal (`"MY_MARKER"`) | UTF-16 user-string heap | `strings -el` |
| a type or member name (`MyNewClass`) | UTF-8 `#Strings` metadata heap | plain `strings` |

Searching a type name with `strings -el` returns zero from a binary that definitely
contains it — which reads exactly like a stale build.

Two related traps when driving builds from the Unity MCP tools:

- **The exe timestamp is not evidence.** An incremental build may leave the launcher
  `.exe` untouched and rewrite only `<Product>_Data/Managed/*.dll`. Check the managed
  assembly's mtime, not the exe's.
- **`BuildPipeline.BuildPlayer` blocks the Editor main thread**, so the MCP channel dies
  mid-build and its retries **re-invoke the build**. Guard anything expensive with a
  sentinel file, or you get several concurrent builds and no indication of it. Read
  progress from `Editor.log` instead; the log stays available while MCP does not.

### Addressables
Enabled with default profile. Build addressables step is optional in CI workflow.

## Architecture

### Git Submodules (3)
- `Packages/com.gdk.core` — GameFoundation (NightHowlGames) — core game systems framework
- `Packages/com.gdk.3rd` — ThirdPartyServices (NightHowlGames) — analytics/ads integrations
- `unity-build-workflows` — CI/CD toolkit (Cuvara)

Run `git submodule update --init --recursive` after clone.

### Assembly Structure
- `NDC.Scripts` (`Assets/Scripts/`) — main game code, root namespace `Scripts`
- `NDC.Scripts.DI` (`Assets/Scripts/DI/`) — DI configuration, references NDC.Scripts + VContainer
- `BuildScript.Editor` / `BuildScript.Runtime` (`Assets/BuildScripts/`) — build automation

### Dependency Injection (VContainer)
- `GameLifetimeScope` — root lifetime scope (project-wide registrations)
- `LoadingSceneScope` — loading scene container
- `MainSceneScope` — main gameplay container
- Scene scopes are child containers of GameLifetimeScope
- `DIExtensions.GetCurrentContainer()` — service locator fallback for non-injected contexts

### Key Libraries
| Library | Purpose |
|---------|---------|
| VContainer | DI container |
| UniTask | Async/await (replaces coroutines) |
| MessagePipe | Pub/sub event messaging (VContainer-integrated) |
| R3 | Reactive extensions |
| Addressables | Asset loading/management |
| URP 17.x | Rendering pipeline |
| Entities 1.4.8 | DOTS ECS framework |
| Entities.Graphics 1.4.21 | ECS rendering bridge |
| Unity.Physics 1.4.7 | DOTS physics |
| Burst 1.8.30 | High-performance compiler |
| Collections 2.6.8 | Native containers (includes Jobs) |
| Mathematics 1.3.2 | SIMD math library |

### DOTS/ECS Notes
- `com.unity.jobs` is deprecated — merged into `com.unity.collections`
- Use `IJobEntity` or `SystemAPI.Query` instead of obsolete `Entities.ForEach`
- DOTS systems use `ISystem` (unmanaged) preferred over `SystemBase` (managed)

### Unity MCP (IvanMurzak/Unity-MCP)
- Editor package: `com.ivanmurzak.unity.mcp` (via OpenUPM)
- MCP server: `aigamedeveloper/mcp-server` Docker image (port 8080)
- Client config: `.claude/mcp.json` → stdio transport via Docker
- In Unity Editor: `Window > AI Game Developer` to auto-generate skills

### OpenUPM Scoped Registries
Packages from `package.openupm.com` scoped to: `com.cysharp.*`, `com.frostbun.*`, `com.google.*`, `jp.hadashikick.*`

## Conventions

- Namespace mirrors folder path under Assets (e.g., `Scripts.DI`, `Scripts.Extensions`)
- One MonoBehaviour/class per file
- DI preferred over singletons — use VContainer's `LifetimeScope.Configure()` for registration
- Use UniTask for async operations, not coroutines
- **All code, comments, docs, changelogs, and commit messages in English**
- **Always update CHANGELOG when making changes**
- **Always write documentation for new features**
- **Separate code into distinct `.asmdef` assemblies when possible** — prefer modular assemblies over monolithic ones
