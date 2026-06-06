# LaunchFast — Sub-project 2: fastlane Setup Wizard

**Status:** Design approved (brainstorm), ready for implementation planning
**Date:** 2026-06-06

## Context

LaunchFast (sub-project #1 + the fastlane-feature expansion) runs and manages fastlane for
Flutter projects that **already have it configured**. A project with no `ios/fastlane` or
`android/fastlane` is currently a dead end — `ProjectScanner.TryScanRoot` returns `null`, so it
never even appears in the launcher. This sub-project adds a **guided wizard** that takes a project
from *no fastlane* to *ready-to-deploy*, and also adds a missing platform or an individual lane to a
project that already has fastlane.

## Goal

A multi-step wizard that:
- Generates a complete fastlane setup (Fastfile/Appfile/Matchfile/Gemfile/`.env.example`) for a
  Flutter project that has none, for iOS and/or Android.
- Adds a missing platform, or an individual lane, to a project that already has fastlane.
- Collects the project-specific variables along the way, stores secrets in the macOS Keychain,
  previews the exact file changes as a diff, writes on approval, and runs `bundle install` so the
  project is immediately runnable.

## Non-goals (out of scope for this sub-project)

- Running interactive credential setup inside the wizard — `fastlane match init`/first cert sync,
  first TestFlight upload, Play console enrolment. (You run these from the existing Lanes screen,
  where preflight + the run panel already live.)
- Non-Flutter project layouts (the wizard assumes a Flutter root: `pubspec.yaml` + `ios/`/`android/`).
- Editing existing lanes (the wizard *adds*; editing a lane is done by hand / future work).
- Windows/Linux.

## Decisions locked in brainstorming

1. **Template source = clone the owner's proven setup, parameterized** (Option A). The generated
   files mirror the VendingMachine-style patterns (dotenv env loading, `match` readonly,
   `flutter build ipa --dart-define …`, Android `gradle bundleRelease` + `supply` track promotions).
   Each lane is an individual template so add-mode can emit just one.
2. **Existing-project changes = generate → preview the exact diff → approve → write** (Option 2).
   Ruby-aware insertion produces a *new file string*; nothing is written until the user approves the
   diff. Scope: add a missing platform **and/or** add individual lanes not already present.
3. **After writing = files + `bundle install`** (Option 2). Generate files → store secrets in
   Keychain → `bundle install` per platform dir via the PTY backend with live output → re-scan.
   Interactive credential dances are left to the Lanes screen.
4. **Secrets:** non-secret config (bundle id, match repo URL, team id, package name, json_key path)
   → generated `Appfile`/`Matchfile`/`.env.example`. Secret **values** (`MATCH_PASSWORD`,
   `APPLE_ID`, `API_TOKEN`, keystore passwords) → macOS Keychain via the existing
   `KeychainSecretStore`; `.env.example` contains only placeholder names, never real values.

## Architecture

### Core (pure, unit-tested, no Avalonia dependency)

```
src/LaunchFast.Core/Scaffolding/
  LaneTemplate.cs          # one lane's template: name, platform, desc, render(answers)->string
  FastlaneScaffolder.cs    # renders the full file set (Fastfile/Appfile/Matchfile/Gemfile/.env.example)
  FastfileMerger.cs        # Ruby-aware: insert a lane into a platform block / add a platform block
  ProjectFacts.cs          # auto-detection: bundle id, package name, app name/version from the project
  ScaffoldPlan.cs          # the computed set of file writes + secret stores (what Review shows)
  WizardAnswers.cs         # the collected inputs (record)
```

- **`LaneTemplate`** — each lane (ios: `sync_certificates`, `beta`, `release`, `screenshots`;
  android: `build`, `internal`, `beta`, `production`) is a template that renders its Ruby body from
  `WizardAnswers`. Pure string rendering.
- **`FastlaneScaffolder.Render(WizardAnswers) -> ScaffoldPlan`** — assembles the chosen lanes into a
  Fastfile, plus Appfile/Matchfile/Gemfile/`.env.example`, returning a `ScaffoldPlan`: a list of
  `FileChange(path, NewContent, Kind: Create|InsertLane|AddPlatformBlock|AppendEnv)` + a list of
  `SecretToStore(key, value)` (values held in memory only, never serialized to the plan on disk).
- **`FastfileMerger`** — for an existing Fastfile: `InsertLane(existingText, laneRuby, platform)`
  finds `platform :ios do … end` and inserts before the matching `end`; `AddPlatformBlock(...)`
  appends a new `platform` block. Returns the new full text. Reuses the brace/`do…end` depth logic
  proven in `FastfileParser.ParseDetailed`. Total/robust.
- **`ProjectFacts.Read(Project)`** — auto-detect: iOS bundle id (from `ios/Runner.xcodeproj`
  pbxproj `PRODUCT_BUNDLE_IDENTIFIER` or `ios/Runner/Info.plist`), Android package
  (`android/app/build.gradle` `applicationId` / `AndroidManifest.xml`), app name + version
  (`pubspec.yaml`). Pre-fills wizard fields. Total/never-throws.
- **`ScaffoldPlan`** — the reviewable result: the file diffs + which secrets will be stored. The UI
  renders this; applying it writes the files + stores the secrets.

### Scanner change

- `Project` gains a `bool HasFastlane` property (computed: `IosFastlaneDir is not null ||
  AndroidFastlaneDir is not null`).
- `ProjectScanner.TryScanRoot(root)` now also returns a `Project` for a **Flutter root with no
  fastlane** — `pubspec.yaml` present + `ios/` and/or `android/` dirs present, but no fastlane dirs
  → a "setup candidate" `Project` (`IosFastlaneDir`/`AndroidFastlaneDir` null, so `HasFastlane=false`).
  It still returns `null` for a directory that is neither a fastlane project nor a Flutter project
  (no pubspec and no platform dirs). `ScanWorkspace` therefore includes setup candidates.
- The launcher renders a candidate (`!HasFastlane`) as the *"No fastlane · Set up →"* card; the
  normal 12-section shell is only opened for `HasFastlane` projects. (Audit existing `TryScanRoot`
  call sites — `LauncherViewModel`, tests — for the widened contract.)

### App (Avalonia)

```
src/LaunchFast.App/
  ViewModels/Wizard/
    SetupWizardViewModel.cs        # orchestrates steps, mode (Install|AddToExisting), Next/Back, Apply
    WizardPlatformsStepViewModel.cs
    WizardIosStepViewModel.cs
    WizardAndroidStepViewModel.cs
    WizardLanesStepViewModel.cs
    WizardReviewStepViewModel.cs   # renders the ScaffoldPlan diffs; Apply
  Views/Wizard/
    SetupWizardView.axaml          # step rail + content host (matches the macOS theme)
    (one view per step)
  Services/
    ProjectScaffoldService.cs      # applies a ScaffoldPlan: write files + KeychainSecretStore.Set
                                   # + run `bundle install` via the PTY backend + re-scan
```

- **`SetupWizardViewModel`** — holds `WizardAnswers`, the current step, the `mode`
  (`Install` for a fastlane-less project; `AddToExisting` pre-filled from what exists). Builds the
  `ScaffoldPlan` for the Review step (via `FastlaneScaffolder` + `FastfileMerger` in add-mode).
- **Entry points:** the launcher card for a setup-candidate shows *"No fastlane · Set up →"* →
  opens the wizard in Install mode. Inside an open project, a **"＋ Add lane / platform"** action on
  the **Fastfile** section toolbar opens the wizard in AddToExisting mode (offers only
  platforms/lanes not already present).
- **Wizard surface:** a dedicated full-content flow (own view with the step rail), shown by the shell
  in place of the launcher/project shell while active. Cancel returns to the launcher.

## Data flow

1. Launcher surfaces a setup-candidate → user clicks *Set up →* → `SetupWizardViewModel` (Install).
   (Or: open project → Fastfile → *Add lane/platform* → wizard (AddToExisting), pre-filled.)
2. `ProjectFacts.Read` pre-fills detectable fields. User steps through Platforms → iOS → Android →
   Lanes, entering variables; secret fields are flagged.
3. Review step: `FastlaneScaffolder.Render` (+ `FastfileMerger` for existing files) → `ScaffoldPlan`
   → per-file diffs shown.
4. Apply: `ProjectScaffoldService` writes each `FileChange`, calls `KeychainSecretStore.Set` for each
   `SecretToStore`, runs `bundle install` per platform dir (PTY backend, live output), then
   re-scans the project. On success the project now has fastlane → opens the normal shell.

## Error handling

- Auto-detection failures → fields left blank (user fills them); never blocks.
- Validation: required fields (bundle id, package name, match repo when match lanes chosen) gate
  Next; the Review/Apply is disabled until valid.
- Write failure (permissions) → surfaced per-file in the Review step; partial writes reported; never
  silently swallowed.
- `bundle install` failure → shown in the run panel; the files are already written (the project is
  set up, just not bundled) — the wizard reports this honestly and points to the Lanes screen.
- Add-mode merge: if the existing Fastfile can't be parsed (no matching `platform` block), fall back
  to **AddPlatformBlock** or show the generated lane for manual paste (diff shows it) rather than
  corrupt the file.
- Secret values never written to disk or logs; only `KeychainSecretStore.Set`.

## Testing strategy

- **`FastlaneScaffolder`** — Verify-snapshot the rendered Fastfile/Appfile/Matchfile/Gemfile/
  `.env.example` for iOS-only, Android-only, and both, against expected contents modelled on the
  VendingMachine files (committed `.verified.txt`). Confirm secrets are placeholders only.
- **`FastfileMerger`** — insert a lane into a fixture Fastfile (assert it lands inside the right
  `platform` block, before `end`); add a platform block to an iOS-only Fastfile; unparseable input →
  graceful fallback.
- **`ProjectFacts`** — bundle id from a fixture pbxproj/Info.plist; package from a fixture
  build.gradle; app name/version from pubspec; missing → null.
- **Scanner** — a Flutter root with no fastlane → setup candidate (`HasFastlane=false`); with
  fastlane → unchanged.
- **Wizard VMs** — step navigation, validation gating, mode (Install vs AddToExisting offering only
  missing platforms/lanes), `ScaffoldPlan` built correctly; `ProjectScaffoldService` apply writes
  files + stores secrets (fake `ISecretStore`, temp project) and would-run `bundle install` (fake
  `IPtyFactory`).
- **UI** — headless Skia snapshot of `SetupWizardView` (a couple of steps), light + dark.
- **Integration (separate solution, macOS-only, Assert.Ignore otherwise)** — generate into a temp
  project then real `bundle install`.

## Success criteria

From a Flutter project with no fastlane: open LaunchFast → the project shows in the grid with
*Set up →* → walk the wizard (most fields pre-filled), enter the match repo + secrets → see the exact
files in a diff → Apply → watch `bundle install` run → the project re-opens as a normal 12-section
LaunchFast project whose lanes are immediately runnable. And: in a project that already has iOS
fastlane, *Add lane/platform* → add Android (or a missing `screenshots` lane) → see the diff
(new Android files / lane inserted into the iOS Fastfile) → Apply.
