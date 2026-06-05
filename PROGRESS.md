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
- 🔨 **Phase 8 — Store status (iOS / App Store Connect).** LaneDestination mapping,
  AppStoreConnectClient (ES256 JWT, TestFlight + App Store version reads), StoreStatusProvider
  with cache + graceful "unavailable". Unit-tested vs recorded JSON.
- ⬜ **Phase 9 — Store status (Android / Play).** PlayStoreClient (service-account OAuth2,
  track versions), wire Android branch into StoreStatusProvider.
- ⬜ **Phase 10 — IntegrationTests + CI + docs.** Separate `.slnx` (real `fastlane lanes`
  vs parser, PTY no-op, Keychain roundtrip), GitHub Actions CI, readme/license, UI snapshot
  tests if feasible.

## Cross-cutting (loop requirements)

- ✅ `.gitignore` hardened against secrets (`*.p8`, `.env*`, `deploy-env.sh`, SA JSON…).
- ✅ Verified no real secrets committed (fixtures use fake values).
- ✅ `claude.md` + this `PROGRESS.md` for session continuity.
- 🔨 Every phase committed with a useful message.
- ⬜ UI snapshot tests (Avalonia headless frame capture) — Phase 10 if feasible.
- ⬜ Final Simon-standard cleanup pass (sealed/records/file-scoped/naming) + full review.

## Test count (keep current)
- After Phase 7: **43** passing (Core 33 + App 10), build 0 warnings.

## Verification still needing the human
- Visible window appearance + a real `bundle exec fastlane` run on the owner's Mac.
- Store-status end-to-end needs the real ASC `.p8` key + Play service-account JSON.
