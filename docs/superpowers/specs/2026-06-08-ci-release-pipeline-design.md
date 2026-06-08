# Design — CI release pipeline + in-app update check

Date: 2026-06-08
Status: Approved (brainstorm)
Repo: `BrandtVavasour/FastLane-GUI-Tool` (branch `main`)

## Goal

When a version tag is pushed, GitHub Actions builds a distributable macOS app and
publishes it to **GitHub Releases**, and the app makes it **easy to know when an
update is available**.

## Decisions (locked during brainstorming)

1. **Trigger / versioning:** tag-triggered semver. Pushing a tag `vX.Y.Z` cuts a
   release built at version `X.Y.Z`. Plain `main` pushes keep their current behaviour
   (build + test only, via the existing `ci.yml`).
2. **Signing:** unsigned to start. No Apple secrets in CI. First launch (and each
   update) needs a one-time right-click → Open. Notarization can be added later.
3. **Update mechanism:** in-app update *check* + link. The app compares its running
   version to the latest GitHub release and, when newer, shows a banner that opens the
   release page in the browser. The user downloads + replaces manually.
4. **Architecture:** `osx-arm64` (Apple Silicon) only to start. x64/universal can be
   added later.
5. **Bundle id:** `au.com.jabtech.launchfast`. App name `LaunchFast`. A simple
   placeholder icon to start (replaceable).

## Current state (relevant facts)

- `src/LaunchFast.App` is `OutputType=WinExe`, `net10.0`, Avalonia — **no** macOS
  packaging today (no `.app`, no `Info.plist`, no icon). Run via `dotnet run`.
- Version is `0.1.0` in `src/Directory.Build.props`. No git tags yet.
- `global.json` pins SDK `10.0.200` (allowPrerelease, latestFeature).
- Existing CI: `.github/workflows/ci.yml` — `macos-latest`, builds + tests the main
  solution on push to `main` and PRs. (It already has a "Surface failing tests"
  annotation step.)

## Architecture

Three independent pieces:

### A. Packaging assets (committed, reusable locally + in CI)

Under `build/macos/`:

- **`Info.plist`** — a template with placeholders for the version. Keys:
  `CFBundleName=LaunchFast`, `CFBundleDisplayName=LaunchFast`,
  `CFBundleIdentifier=au.com.jabtech.launchfast`, `CFBundleExecutable=LaunchFast.App`
  (the .NET apphost name), `CFBundleShortVersionString=__VERSION__`,
  `CFBundleVersion=__VERSION__`, `CFBundleIconFile=LaunchFast`,
  `CFBundlePackageType=APPL`, `LSMinimumSystemVersion=12.0`,
  `NSHighResolutionCapable=true`, `LSApplicationCategoryType=public.app-category.developer-tools`.
- **`LaunchFast.icns`** — a simple placeholder icon, generated once from a flat-colour
  PNG via `iconutil`/`sips` and committed (so CI needs no icon tooling). Documented how
  to regenerate.
- **`make-app.sh`** — single source of truth for building the distributable. Usage:
  `build/macos/make-app.sh <VERSION>` (run from repo root). It does the **whole**
  build end to end so CI and local runs are identical:
  1. `dotnet publish src/LaunchFast.App -c Release -r osx-arm64 --self-contained true -p:Version=$VERSION` to a temp publish dir.
  2. Create `LaunchFast.app/Contents/{MacOS,Resources}`.
  3. Copy the publish output into `Contents/MacOS/`.
  4. Render `Info.plist` (substitute `__VERSION__`) into `Contents/Info.plist`.
  5. Copy `LaunchFast.icns` into `Contents/Resources/`.
  6. `chmod +x Contents/MacOS/LaunchFast.App`.
  7. Output `LaunchFast-$VERSION-osx-arm64.zip` containing `LaunchFast.app` (zipped with
     `ditto -c -k --keepParent` to preserve bundle structure + the executable bit).
  Fails fast (`set -euo pipefail`). The CI workflow simply calls this script; the owner
  can run the same command locally to produce an identical artifact.

### B. Release workflow

`.github/workflows/release.yml`, `on: push: tags: ['v*.*.*']`, `runs-on: macos-latest`:

1. Checkout, setup .NET (`10.0.x`).
2. Derive `VERSION` from the tag (`${GITHUB_REF_NAME#v}`).
3. `dotnet test LaunchFast.slnx` — **gate**: a failure aborts the release.
4. `build/macos/make-app.sh $VERSION` → produces `LaunchFast-$VERSION-osx-arm64.zip`.
5. Create the GitHub Release for the tag with the zip attached and auto-generated
   notes (`softprops/action-gh-release@v2`, `generate_release_notes: true`,
   `files: LaunchFast-*-osx-arm64.zip`). Uses the default `GITHUB_TOKEN` (needs
   `permissions: contents: write`).

### C. In-app update check

**Core (`LaunchFast.Core`, no Avalonia, unit-tested):**

- `record ReleaseInfo(string TagName, string HtmlUrl)`.
- `static class GitHubReleases`:
  - `ReleaseInfo? ParseLatest(string json)` — pulls `tag_name` + `html_url` from the
    `releases/latest` response. Total; returns null on malformed input.
  - `bool IsNewer(string currentVersion, string latestTag)` — semver-ish compare.
    Strips a leading `v`; compares dotted numeric components (major/minor/patch),
    missing components treated as 0; non-numeric → false (never throws). Returns true
    only when `latest > current`.
- `static class AppVersion { string Current { get; } }` — reads the entry assembly's
  informational/assembly version (set at build from the tag; `0.1.0` in dev).

**App (`LaunchFast.App`):**

- `UpdateService` — `Task<ReleaseInfo?> CheckAsync()`:
  - HTTP GET `https://api.github.com/repos/BrandtVavasour/FastLane-GUI-Tool/releases/latest`
    with a `User-Agent` header and a short timeout (≈5 s).
  - Fail-silent: any exception / non-200 / no network → returns null.
  - Returns the `ReleaseInfo` only when `GitHubReleases.IsNewer(AppVersion.Current, tag)`.
  - Injectable `HttpMessageHandler`/`Func` seam so it's testable without real network
    (the pure parse/compare lives in Core and is the main test target).
- Wiring: the shell kicks off `UpdateService.CheckAsync()` on launch (fire-and-forget,
  marshalled to the UI thread); on a non-null result it sets an `UpdateAvailable`
  state (the latest tag + url).

**UI:**

- A small banner/button in the Launcher toolbar (`LauncherView`): visible only when
  `UpdateAvailable` — text `⬆ Update available: vX.Y.Z`, click opens `HtmlUrl` via
  Avalonia `ILauncher.LaunchUriAsync`. Hidden when current/offline (no error noise).

## Install / update flow (documented in README)

1. Download `LaunchFast-<version>-osx-arm64.zip` from the Releases page.
2. Unzip, drag `LaunchFast.app` to `/Applications`.
3. First launch: right-click → Open (once — the app is unsigned).
4. The in-app banner notifies when a newer release is out and links straight to it;
   repeat 1–3 to update.

## Testing

- **Core unit tests:** `IsNewer` (newer/older/equal, `v` prefix, missing components,
  non-numeric/garbage → false), `ParseLatest` (valid JSON, missing fields, malformed).
- **App test:** `UpdateService` returns null on a non-200/empty body via the injected
  handler; returns `ReleaseInfo` when the parsed tag is newer than `AppVersion.Current`.
- **Workflow:** validated by pushing a `v0.1.1` test tag (owner action) — the run should
  produce the zip and a release. Not unit-testable in CI.
- Build stays warning-free (`TreatWarningsAsErrors`); all existing tests green.

## Out of scope (future)

- Code signing + notarization (removes the right-click-Open step).
- x64 / universal binaries.
- Homebrew cask tap (`brew upgrade`).
- Full in-app auto-download + self-replace.
- A designed app icon.

## Risks / notes

- `macos-latest` is Apple Silicon, so `osx-arm64` self-contained publish matches the
  runner and the owner's Mac.
- GitHub API rate limit for unauthenticated `releases/latest` is low but ample for a
  once-per-launch check; fail-silent covers throttling.
- The `.app` must be zipped with `ditto` (not `zip`) to preserve bundle structure and
  the executable bit.
