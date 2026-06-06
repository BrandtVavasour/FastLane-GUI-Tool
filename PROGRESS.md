# LaunchFast — Progress Log

Running checklist for sub-project #1 ("Launcher + detect + run existing lanes").
Update this as each phase lands. See `claude.md` for goals/architecture.

## Status legend
✅ done & reviewed · 🔨 in progress · ⬜ not started

## Phases

- ✅ **Phase 0 — Scaffolding.** Repo, `.slnx`, CPM, `Directory.Build.props`, `global.json`,
  Core/Tests/App projects. Build 0/0.
- ✅ **Phase 1 — Models + FastfileParser.** Platform/Lane/Project; static lane parse,
  skips private lanes; Verify snapshots vs real Fastfiles.
- ✅ **Phase 2 — ProjectScanner + ProjectStore.** Root + workspace detection (Flutter-only),
  JSON recents/workspaces persistence.
- ✅ **Phase 3 — IconExtractor.** Largest iOS/Android launcher icon, null fallback.
- ✅ **Phase 4 — Env + Keychain.** EnvFileReader, ISecretStore/EnvResolver,
  KeychainSecretStore (ArgumentList-safe). Live Keychain test deferred to Phase 10.
- ✅ **Phase 5 — LaneRunner + Preflight.** PTY seam, LaneRunner (single clean signature),
  Preflight (Gemfile/tool checks).
- ✅ **Phase 6 — Launcher grid UI.** LauncherViewModel/ProjectCard, grid view, folder
  pickers, path→Bitmap converter; Avalonia.Headless tests.
- ✅ **Phase 7 — Project detail + run panel.** ProjectDetailViewModel (lanes, env banner,
  one-run gating), detail view, run panel, SecretsDialog, navigation.
  - Real run backend `ProcessPtyFactory` (replaces unavailable Pty.Net).
  - Fixed: only genuine secrets gate runs (`SecretEnvFilter`) — control vars don't block.
- ✅ **Phase 8 — Store status (iOS / App Store Connect) [Core].** LaneDestination mapping,
  Destination/StoreStatus models, AppStoreConnectClient (ES256 JWT + pure version mappers),
  StoreStatusProvider (cache successful-only, graceful "unavailable"). Unit-tested vs recorded JSON.
- ✅ **Phase 9 — Store status (Android / Play) [Core].** PlayStoreClient (service-account auth,
  pure `MapTracks`), provider wired for Android. (Google.Apis.AndroidPublisher.v3 pinned to
  1.69.0.3710 — 1.68.0.3675 wasn't on the feed.)
- ✅ **Store status — UI wiring.** Per-lane store-status line in ProjectDetailView (async,
  non-blocking, graceful "unavailable") + Refresh button. `AppfileReader` (bundle id /
  package name / json_key_file), `AppStoreConnectClient.FromKeyFile`, `StoreStatusFactory`
  (disk credential discovery, never throws), wired via ShellViewModel. Reviewed/approved.
- ✅ **Phase 10 — IntegrationTests + CI + docs.** Separate `IntegrationTests.slnx`; 3 guarded
  smoke tests that RAN & PASSED here: real `fastlane lanes` vs `FastfileParser` (parser matches
  fastlane's own view of the real VendingMachine iOS lanes), real `ProcessPtyFactory` streaming,
  real Keychain roundtrip with special chars. GitHub Actions CI (`macos-latest`, builds+tests the
  main solution only), `readme.md`, MIT `license.txt`.
- ✅ **Final pass.** Removed unused package pins (JWT, DI container); tidied store-status
  kickoff; added multi-versionCode MapTracks test; **wired Preflight into the run path** and
  disabled run buttons while a run is in flight (both with tests); full end-to-end review →
  SHIP-READY. UI **pixel snapshots not feasible** in the build sandbox (no headless Skia render
  backend / `IPlatformRenderInterface`); construct-without-throw headless tests + 3 real
  integration tests cover the UI paths instead — revisit pixel snapshots on a real Mac.

## ✅ Sub-project #1 COMPLETE — runnable & reviewed (2026-06-06)
All planned scope delivered: launcher grid + icons, lane detection, run with live output +
preflight + stop + one-run gating, env files + Keychain secrets + secret-only gating, per-lane
store version (iOS + Android) with graceful unavailable. Remaining verification needs the owner's
Mac (visible window + a real deploy) and real ASC `.p8` / Play service-account JSON for live
store data. Next: sub-project #2 (lane scaffolding).

## Cross-cutting (loop requirements)

- ✅ `.gitignore` hardened against secrets (`*.p8`, `.env*`, `deploy-env.sh`, SA JSON…).
- ✅ Verified no real secrets committed (fixtures use fake values).
- ✅ `claude.md` + this `PROGRESS.md` for session continuity.
- 🔨 Every phase committed with a useful message.
- ✅ UI snapshot tests (Avalonia headless Skia frame capture) — see the expansion section.
- ✅ Final conventions cleanup pass (sealed/records/file-scoped/naming) + full review.

## Fastlane feature expansion (from Claude Design "Signing, Beta & Build")
Goal: cover most of fastlane via per-project section screens. Built as shells now, made real incrementally.
- ✅ **Component theme** — reusable styles/tokens (segmented control, toggle switch, pills ok/warn/bad,
  list cards + colored icon chips, panels/def-rows, inputs/selects, danger zone, chips). Light/Dark.
- ✅ **Per-project navigation shell** — sidebar with sections (Lanes · Signing · Secrets · TestFlight ·
  Screenshots · Build & Test) + content host; Lanes = existing run screen; clean section→content→view seam.
- ✅ **Secrets & Credentials section — REAL.** Live secret status (process-env → `.env*` → Keychain →
  missing) with source chips, reveal, add/edit → Keychain. Shared `Core/Env/ProjectSecretScanner`
  (also used by the Lanes screen). Auth toggle (ASC key vs Apple ID) is informational for now.
- ✅ **Signing · TestFlight · Screenshots · Build & Test — themed shells.** Faithful to the design with
  honest placeholder data (`IsPlaceholder` + "Illustrative" hints) and primary Run buttons genuinely
  wired to the matching lane (Run match→sync_certificates, Distribute→beta, Run snapshot→screenshots)
  or disabled when absent. `ProjectDetailViewModel.TryRunLane`/`HasLane` + `ProjectShellViewModel.RunLane`.
  → **"Signing, Beta & Build" design fully implemented.**
- ⬜ Make each shell's data REAL (cert/profile parsing, ASC testers/builds, snapshot/frameit config,
  gym/scan config + test-result parsing) — own slices, later.
- ✅ **UI snapshot tests** — real Avalonia **headless Skia** frame capture works; 8 views × Light/Dark
  rendered and asserted to draw real pixels (`SnapshotHarness`, `ViewSnapshotTests`, `RenderProbeTests`).
  No committed `.verified.png` baselines (cross-machine AA/font instability) — PNGs emitted to gitignored
  output for eyeballing; tests catch crashes/blank-render, not subtle pixel regressions.
- ✅ **Store & Release** design fully implemented (3 screens): **Store Listing** (real metadata +
  screenshots from disk), **What's New** (real release notes per version/locale), **Release Flow**
  (compose + pre-flight checklist with REAL checks — secrets/metadata/screenshots/version — Submit
  wired to the release lane). Edit/Save for metadata + release notes is a noted follow-up.
- ✅ **Lanes, History & Android Signing** design fully implemented: **Fastfile inspector** (real
  ParseDetailed), **Run history** (real RunHistoryStore + recording), **Android Signing** (real gradle
  signingConfig parse + credential presence; keystore keys/fingerprints illustrative — keytool is a
  follow-up).
- 🎉 **ALL THREE design files fully implemented.** Per-project sidebar: Lanes · Fastfile · History ·
  Signing · Secrets · TestFlight · Screenshots · Build & Test · Store Listing · What's New · Release ·
  Android Signing.
- ✅ Closing review: SHIP-READY (244→ growing tests, build 0/0, no author names, conventions OK).
- 🔨 **Make-real passes for the 4 iOS shells** (turn illustrative data into real disk/API reads):
  ✅ **Screenshots — REAL** (`SnapshotConfigReader`: Snapfile devices/languages/scheme/launch-args +
  Framefile + captured screenshots from disk; only the framed-preview mock stays illustrative).
  ⬜ Build & Test (gym/scan config + test results) · ⬜ Signing iOS (provisioning profiles + certs) ·
  ⬜ TestFlight (ASC builds/testers) — next.

## UI design
- ✅ **macOS-native restyle** from a Claude Design handoff (`LaunchFast.html`): tokenised
  Light/Dark theme (`Themes/Tokens.axaml`) + reusable control styles (`Themes/Controls.axaml`),
  applied to the launcher grid + project detail (cards, badges, warning banner, lane list,
  dark terminal panel). Follows the OS theme. Reviewed/approved; **needs a visual eyeball on a
  real Mac** (`dotnet run --project src/LaunchFast.App`). Known approximations: platform glyphs
  are simple colour markers (no brand-icon set in Avalonia); button drop-shadows omitted
  (Avalonia `BoxShadow` is Border-only).

## Test count (keep current)
- Current: **244** passing (Core 112 + App 132) + **3** integration tests (real fastlane/PTY/Keychain),
  build 0 warnings (both solutions). (Was 85 at the end of sub-project #1, before the fastlane expansion.)

## Verification still needing the human
- Visible window appearance + a real `bundle exec fastlane` run on the owner's Mac.
- Store-status end-to-end needs the real ASC `.p8` key + Play service-account JSON.
