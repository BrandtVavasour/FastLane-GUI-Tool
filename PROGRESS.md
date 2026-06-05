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
- ⬜ **Phase 10 — IntegrationTests + CI + docs.** Separate `.slnx` (real `fastlane lanes`
  vs parser, PTY no-op, Keychain roundtrip), GitHub Actions CI, readme/license.
- ⬜ **Final pass.** Simon-standard cleanup (sealed/records/file-scoped/naming, remove unused
  package pins), UI snapshot tests if feasible, full end-to-end review.

## Cross-cutting (loop requirements)

- ✅ `.gitignore` hardened against secrets (`*.p8`, `.env*`, `deploy-env.sh`, SA JSON…).
- ✅ Verified no real secrets committed (fixtures use fake values).
- ✅ `claude.md` + this `PROGRESS.md` for session continuity.
- 🔨 Every phase committed with a useful message.
- ⬜ UI snapshot tests (Avalonia headless frame capture) — Phase 10 if feasible.
- ⬜ Final Simon-standard cleanup pass (sealed/records/file-scoped/naming) + full review.

## Test count (keep current)
- After store-status UI wiring: **82** passing (Core 69 + App 13), build 0 warnings (Debug + Release).

## Verification still needing the human
- Visible window appearance + a real `bundle exec fastlane` run on the owner's Mac.
- Store-status end-to-end needs the real ASC `.p8` key + Play service-account JSON.
