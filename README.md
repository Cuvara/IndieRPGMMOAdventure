# NDC Unity Template

Unity project template maintained by CuongND, built and released via the
`unity-build-workflows` CI toolkit.

---

## CI / Build System

This project uses the
[unity-build-workflows](unity-build-workflows/README.md) toolkit (included as a
git submodule) for all CI builds. Unity operations run inside pinned Docker
containers on GitHub Actions — no local Unity installation is required for CI.

Builds run through the toolkit's **numbered entry workflows** (toolkit v3).
Three of them, three questions:

| Workflow | Question it answers | Builds a player? |
|---|---|---|
| `01-ci.yml` | Is this code safe to merge? | No — validate, licence, tests only |
| `10-build-development.yml` | Give me something to test with | Yes — APK / unsigned artifacts |
| `11-build-release.yml` | Give me something we could ship | Yes — signed AAB, immutable Release Set |

`01-ci.yml` runs automatically on push and pull request against `develop`,
`staging` and `release-*`. The two build workflows are `workflow_dispatch`
only: pushing no longer spends six Unity builds on a merge check.

Promotion is a separate layer. `20-release-android.yml`,
`22-release-webgl.yml`, `23-release-windows.yml` and `24-release-linux.yml`
publish the exact bytes a `Build / Release` run produced — they do not rebuild.
Each takes a `source-run-id`, which `Build / Release`'s final report prints as
a ready-made command. iOS promotion exists in the toolkit
(`pipeline-ios-release.yml`) but is not installed here: this project ships no
iOS build and `BuildConfig/` carries no `iOS` block.

All entry workflows call the same `unity-pipeline.yml` engine at `@v3` and
contain no build logic of their own.

See [unity-build-workflows/docs/CONSUMER\_SETUP.md](unity-build-workflows/docs/CONSUMER_SETUP.md)
for the entry-workflow contract and
[unity-build-workflows/docs/RELEASE\_FLOW.md](unity-build-workflows/docs/RELEASE_FLOW.md)
for the release/promotion layer.

### Triggering builds manually

```bash
# Development build (APK), all platforms the environment selects
gh workflow run 10-build-development.yml \
  --repo Cuvara/IndieRPGMMOAdventure \
  --ref develop \
  -f platform=All

# Development build, one platform
gh workflow run 10-build-development.yml \
  --repo Cuvara/IndieRPGMMOAdventure \
  --ref develop \
  -f platform=Android

# Release build (signed AAB, immutable artifacts)
gh workflow run 11-build-release.yml \
  --repo Cuvara/IndieRPGMMOAdventure \
  --ref main \
  -f platform=Android

# Promote a finished release build to the store lane
gh workflow run 20-release-android.yml \
  --repo Cuvara/IndieRPGMMOAdventure \
  --ref main \
  -f source-run-id=<RUN_ID>
```

Platform choices: **All**, **Desktop**, **Android**, **WebGL**, **Windows**,
**Linux**, **Linux Server**. `All` resolves to the environment's
`*_BUILD_PLATFORMS` repository variable.

**iOS** requires a registered self-hosted macOS runner with the
`macos-unity-xcode` label — it is **blocked** until one is provisioned (see
[SELF\_HOSTED\_MACOS\_RUNNER.md](unity-build-workflows/docs/SELF_HOSTED_MACOS_RUNNER.md),
[EXPLICIT\_PLATFORM\_FLOW.md § iOS](unity-build-workflows/docs/EXPLICIT_PLATFORM_FLOW.md#6-ios-build--special-requirements)
and
[GITHUB\_ACTIONS\_BUILD\_RUNBOOK.md § 10](unity-build-workflows/docs/GITHUB_ACTIONS_BUILD_RUNBOOK.md#10-iosmacos-runner-limitations)).

### Key dispatch inputs

Shared by `10-build-development.yml` and `11-build-release.yml`:

| Input | Default | Description |
|---|---|---|
| `platform` | `All` | `All`, `Desktop`, `Android`, `WebGL`, `Windows`, `Linux`, `Linux Server` |
| `environment` | `development` (dev) / `production` (release) | Build environment passed to Unity |
| `run-tests` | `true` | Run Unity tests as the gate before building |
| `test-mode` | `All` | `EditMode`, `PlayMode`, `All` |
| `build-addressables` | `false` | Build the Addressables catalog first |
| `unity-version` | *(blank)* | Override `ProjectSettings/ProjectVersion.txt` |
| `clean-build` | `auto` | `auto`, `true`, `false` |
| `define-symbols` | *(blank)* | Extra scripting define symbols |
| `runner-type` | `auto` | `auto`, `github-hosted`, `self-hosted` |
| `build-engine` | `auto` | `auto`, `docker`, `local` |

`build-type` is its own axis and is fixed per workflow (`development` vs
`release`), so artifact names cannot be confused: `development-android-apk`
versus `release-android-aab`.

Full input reference: [EXPLICIT\_PLATFORM\_FLOW.md § 2](unity-build-workflows/docs/EXPLICIT_PLATFORM_FLOW.md#2-workflow-dispatch-inputs).

### Unity version

Current version: **6000.3.9f1** — defined in
`ProjectSettings/ProjectVersion.txt` (single source of truth).

To upgrade Unity, follow the checklist in
[unity-build-workflows/docs/UNITY\_VERSION\_UPGRADE.md](unity-build-workflows/docs/UNITY_VERSION_UPGRADE.md).

---

## Required Secrets

Configure in `Settings → Secrets and variables → Actions`:

| Secret | Required | Purpose |
|---|---|---|
| `UNITY_LICENSE` | Yes | Raw `.ulf` file contents |
| `UNITY_EMAIL` | Yes | Unity account email |
| `UNITY_PASSWORD` | Yes | Unity account password |
| `ANDROID_KEYSTORE_BASE64` | Optional | Android signing |
| `ANDROID_KEYSTORE_PASS` | Optional | Android signing |
| `ANDROID_KEY_ALIAS` | Optional | Android signing |
| `ANDROID_KEY_PASS` | Optional | Android signing |
| `DISCORD_WEBHOOK_URL` | Optional | Discord build notifications |
| `R2_ACCESS_KEY_ID` | Optional | Cloudflare R2 build delivery (`BUILD_DELIVERY=r2`) |
| `R2_SECRET_ACCESS_KEY` | Optional | Cloudflare R2 build delivery (`BUILD_DELIVERY=r2`) |

All three Unity license secrets (`UNITY_LICENSE`, `UNITY_EMAIL`,
`UNITY_PASSWORD`) must be set together. See
[unity-build-workflows/docs/UNITY\_PERSONAL\_DOCKER\_LICENSE.md](unity-build-workflows/docs/UNITY_PERSONAL_DOCKER_LICENSE.md)
for setup instructions.

Verify secrets are present:
```bash
gh secret list --repo Cuvara/IndieRPGMMOAdventure \
  | grep -E 'UNITY_LICENSE|UNITY_EMAIL|UNITY_PASSWORD'
```

---

## Downloading Build Artifacts

Retention tiers by purpose: release 90 days, staging 14, development 7, logs 7.
Do not set `ARTIFACT_RETENTION_DAYS` — a release artifact that expires can never
be promoted again.

A finished build can also be published to a plain download URL that works
without a GitHub account, via the `BUILD_DELIVERY` repository variable
(`none` | `r2` | `local`). See
[unity-build-workflows/docs/BUILD\_DELIVERY.md](unity-build-workflows/docs/BUILD_DELIVERY.md).

```bash
# List recent builds
gh run list --repo Cuvara/IndieRPGMMOAdventure \
  --workflow 10-build-development.yml --limit 10

# Download artifacts from a specific run
gh run download <RUN_ID> --repo Cuvara/IndieRPGMMOAdventure
```

---

## Documentation

| Document | Description |
|---|---|
| [unity-build-workflows/docs/CONSUMER\_SETUP.md](unity-build-workflows/docs/CONSUMER_SETUP.md) | Entry-workflow contract — which numbered workflow answers which question, repository variables |
| [unity-build-workflows/docs/RELEASE\_FLOW.md](unity-build-workflows/docs/RELEASE_FLOW.md) | Release Set and promotion — `source-run-id`, store lanes, what is rebuilt (nothing) |
| [unity-build-workflows/docs/BUILD\_DELIVERY.md](unity-build-workflows/docs/BUILD_DELIVERY.md) | Publishing a finished build to a download URL (`BUILD_DELIVERY`: `none` / `r2` / `local`) |
| [unity-build-workflows/docs/EXPLICIT\_PLATFORM\_FLOW.md](unity-build-workflows/docs/EXPLICIT_PLATFORM_FLOW.md) | Explicit-platform-jobs flow: job graph, dispatch inputs, activation, platform selection, iOS requirements |
| [unity-build-workflows/docs/UNITY\_PERSONAL\_DOCKER\_LICENSE.md](unity-build-workflows/docs/UNITY_PERSONAL_DOCKER_LICENSE.md) | Unity Personal/free Docker licensing — `personal-combined` strategy, secret setup, troubleshooting |
| [unity-build-workflows/docs/UNITY\_VERSION\_UPGRADE.md](unity-build-workflows/docs/UNITY_VERSION_UPGRADE.md) | Step-by-step Unity version upgrade checklist |
| [unity-build-workflows/docs/GITHUB\_ACTIONS\_BUILD\_RUNBOOK.md](unity-build-workflows/docs/GITHUB_ACTIONS_BUILD_RUNBOOK.md) | Operational runbook — triggering builds, reading logs, artifacts, common errors |
| [unity-build-workflows/docs/SELF\_HOSTED\_MACOS\_RUNNER.md](unity-build-workflows/docs/SELF_HOSTED_MACOS_RUNNER.md) | Provisioning a `macos-unity-xcode` self-hosted runner for iOS builds (Xcode, Unity iOS module, activation) |
| [unity-build-workflows/README.md](unity-build-workflows/README.md) | CI toolkit — architecture, workflows, image variants |
