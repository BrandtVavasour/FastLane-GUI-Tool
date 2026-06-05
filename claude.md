# LaunchFast — Project Guide for Claude

> Read this first. It is the single source of truth for what LaunchFast is, why it
> exists, how it's built, and where we are. Any new session should be able to pick up
> from here. Keep it and `PROGRESS.md` up to date as work proceeds.

## What this is

LaunchFast is a **native macOS GUI for setting up, managing, and running fastlane
deployments** for Flutter apps. It exists to remove the command-line friction of
fastlane (`bundle exec fastlane …`, sourcing `deploy-env.sh`, remembering lane names,
match, env vars) and make building/deploying iOS + Android apps a point-and-click flow.

The owner maintains Flutter apps (Vending Machine Tracker, Hits & Blows) that deploy via
mature fastlane setups, and wants one Mac app to open those projects and drive them.

## Original goal (verbatim intent)

A Flutter… (now **.NET/Avalonia**, owner's choice) macOS app that:
- Opens a root project containing one or more fastlanes (or none — and can create lanes
  from the GUI for Android and iOS).
- Supports the organisation we already use: match files, env files, per-project secrets.
- Has a **release-to-prod checklist**.
- Helps **set up** fastlane in projects AND **manage/deploy** them.
- Has a **projects / recents grid** on load (Android-Studio style).
- Uses the project's **iOS/Android app icon** as the card icon.
- Shows the **current store version** for each lane's destination.

## Tech stack & standards ("Simon's standards" / Papyrine conventions)

- **.NET 10 / C# 14**, **Avalonia 11** MVVM (`CommunityToolkit.Mvvm`), macOS-first
  (Avalonia keeps Win/Linux open but they are not targeted).
- **`.slnx`** solution (`LaunchFast.slnx`), **Central Package Management**
  (`src/Directory.Packages.props`, transitive pinning).
- `src/Directory.Build.props`: `Nullable enable`, `ImplicitUsings enable`,
  **`TreatWarningsAsErrors=true`**, `EnforceCodeStyleInBuild=true`, `LangVersion 14`,
  pinned `Version`. **Polyfill** referenced (`PrivateAssets=all`).
- **Verify + NUnit 4** for tests; snapshot `.verified.*` files are source of truth.
  Prefer `sealed`, file-scoped namespaces, records for data, pure/testable units.
- `global.json` pins the SDK (10.0.200, latestFeature roll-forward).
- A **separate `IntegrationTests/`** solution for tests that need real fastlane / Keychain.
- Lowercase docs (`readme.md`, `license.txt`, `claude.md`), `.github/workflows` CI.

## Architecture

```
src/
  LaunchFast.Core         → pure domain, NO Avalonia dependency (so it's all unit-tested)
    Models/   Platform, Lane, Project, EnvStatus, (StoreStatus…)
    Parsing/  FastfileParser        — static lane extraction, skips private_lane
    Scanning/ ProjectScanner, ProjectStore (JSON recents+workspaces)
    Icons/    IconExtractor         — largest iOS/Android launcher icon
    Env/      EnvFileReader, ISecretStore, EnvResolver, KeychainSecretStore,
              SecretEnvFilter        — only genuine secrets gate a run
    Running/  Preflight, IPtyProcess/IPtyFactory (seam), LaneRunner,
              ProcessPtyFactory      — real pipe-based run backend
    Stores/   (Phase 8/9) LaneDestination, AppStoreConnectClient, PlayStoreClient,
              StoreStatusProvider
  LaunchFast.App          → Avalonia UI (MVVM)
    ViewModels/  Launcher, ProjectCard, ProjectDetail, Lane, Run, SecretsDialog, Shell
    Views/       LauncherView, ProjectDetailView, SecretsDialog, MainWindow
    Converters/  PathToBitmapConverter
    Services/    AppServices (composition root)
  LaunchFast.Core.Tests   → Verify + NUnit unit/snapshot tests (+ fixtures/)
  LaunchFast.App.Tests    → Avalonia.Headless VM + view tests
IntegrationTests/         → (Phase 10) separate .slnx; real fastlane/Keychain/PTY smoke
```

**Boundary rule:** `LaunchFast.Core` must never reference Avalonia. UI consumes Core via
interfaces wired in `Services/AppServices.cs`.

## Key decisions / gotchas

- **Run backend is pipe-based**, not a true PTY. The plan pinned `Pty.Net 0.1.39-pre`
  which does not exist on NuGet; `ProcessPtyFactory` (System.Diagnostics.Process with
  redirected pipes, env via `psi.Environment`, kill-tree, `CLICOLOR_FORCE`) sits behind
  the `IPtyFactory` seam. Trade-off: reduced terminal colour and no full interactive
  prompt (2FA) support. Acceptable because the owner's lanes authenticate via ASC API
  key + `MATCH_PASSWORD` env + Play service account and never prompt. A true `forkpty`
  PTY can replace `ProcessPtyFactory` later with zero churn elsewhere.
- **Secrets** are stored in the **macOS Keychain** (`KeychainSecretStore`, via the
  `security` CLI with `ArgumentList` so special chars are safe). Never written to disk.
  `SecretEnvFilter.IsSecret` decides which referenced `ENV[...]` vars are real secrets
  (allow-list + PASSWORD/TOKEN/SECRET/KEY suffix), so control vars like `CI`,
  `FASTLANE_ENV`, `FLUTTER_LOCALE` never block a run.
- **Lane discovery** is a static text parse of the `Fastfile` (no Ruby needed); private
  lanes are excluded.
- **Secret safety:** `.gitignore` blocks `*.p8`, `.env*`, `deploy-env.sh`,
  service-account JSON, etc. Never commit real credentials. Test fixtures use fake values.

## Build / run / test

```bash
dotnet build LaunchFast.slnx                 # 0 warnings (TreatWarningsAsErrors)
dotnet test  LaunchFast.slnx                 # Core + App unit/headless tests
dotnet run --project src/LaunchFast.App      # launch the GUI (manual verification)
# Phase 10:
dotnet test IntegrationTests/IntegrationTests.slnx   # real fastlane/Keychain smoke
```

## Sub-project roadmap (this repo builds #1 first)

1. **Launcher + detect + run existing lanes** ← current build (see PROGRESS.md).
2. Lane scaffolding for projects with no fastlane (generate Fastfiles/Matchfile/env).
3. Deeper match / code-signing management.
4. Release-to-prod checklist.
5. Richer multi-project organisation.

Source spec & plan (in the sibling VendingMachine repo's docs):
`docs/superpowers/specs/2026-06-05-fastlane-gui-runner-mvp-design.md` and
`docs/superpowers/plans/2026-06-05-launchfast-runner-mvp.md`.

## Current status

**Sub-project #1 is COMPLETE and reviewed (SHIP-READY) as of 2026-06-06.** All planned scope
is delivered: launcher grid + icons, lane detection, run with live output + **preflight** + stop
+ one-run gating, env files + Keychain secrets + secret-only gating, per-lane store version
(iOS + Android) with graceful unavailable, separate IntegrationTests solution + CI + docs.
**85 unit tests + 3 real integration tests** pass; build is 0/0 (Debug + Release, both solutions).
See **`PROGRESS.md`** for the phase-by-phase log. Next up is sub-project #2 (lane scaffolding).

Still needs the owner's Mac for manual verification: the visible window appearance and an actual
`fastlane` run; and real ASC `.p8` + Play service-account JSON for live store-version data.

### Known limitations / conventions to keep in mind
- **Preflight** (Gemfile/bundler) now runs before a lane launches; failures show in the output
  panel and block the run.
- **`LaneDestination.For` hardcodes lane names** → store destination: iOS `beta`→TestFlight,
  `release`→App Store; Android `internal`/`beta`/`production`→matching Play tracks. Projects
  using other release-lane names get `Destination.None` (no store line — graceful but silent).
- **UI pixel snapshots** aren't tested (the build sandbox has no headless Skia render backend);
  views are covered by construct-without-throw headless tests + the integration suite. The
  capture harness (flip `UseHeadlessDrawing`, `CaptureRenderedFrame`) is noted for a real Mac.
