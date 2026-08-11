# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

IndieRPGMMOAdventure — Unity 6 (6000.3.9f1) game project targeting Android, WebGL, Windows, and Linux. Early-stage, built on a template with DI and async patterns. Uses IL2CPP scripting backend with .NET Standard 2.1.

## Build & CI

### Local Build
Unity Editor opens the project directly. No CLI build tool beyond Unity's batch mode.

### CI/CD (GitHub Actions)
- `.github/workflows/unity-build.yml` — thin dispatcher delegating to `unity-build-workflows` submodule pipeline
- Platforms: Android (AAB/ARM64), WebGL (Brotli), Linux64, LinuxServer, Windows64
- Environments: production, staging, development
- Build configs live in `BuildConfig/` — `base.json` merged with environment overlays (`development.json`, `staging.json`, `production.json`)
- Build scripts: `Assets/BuildScripts/Editor/PlayerBuilder.cs`, `Assets/BuildScripts/Editor/AddressableBuilder.cs`
- Build gates: max 500MB, fail at 25% size increase, no missing references/scripts

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
