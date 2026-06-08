# CI Release Pipeline + In-App Update Check — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** On `git push` of a `vX.Y.Z` tag, GitHub Actions builds an unsigned macOS `.app`, zips it, and publishes it to GitHub Releases; the app checks GitHub on launch and shows an "update available" banner linking to the release.

**Architecture:** Pure version-compare + release-JSON parsing live in `LaunchFast.Core` (unit-tested). An App-layer `UpdateService` does the fail-silent HTTP call and feeds the `LauncherViewModel`, which shows a toolbar banner. Packaging is a committed `build/macos/` directory (`Info.plist`, icon, `make-app.sh`) that both CI and the owner run identically. A new `release.yml` workflow ties it together.

**Tech Stack:** .NET 10 / C# 14, Avalonia 11, NUnit, GitHub Actions (`macos-latest`), `softprops/action-gh-release`, `ditto`/`iconutil`/`sips`.

**Conventions:** `TreatWarningsAsErrors`, nullable enable, sealed/records/file-scoped namespaces, collection expressions `[]`. `LaunchFast.Core` has ZERO Avalonia dependency. No author-name strings except the existing `LICENSE`/`readme.md`. Commit only the files each task changes (the working tree has 4 pre-existing IDE-modified files — `App.axaml.cs`, `Views/SecretsSectionView.axaml.cs`, `LaunchFast.Core.Tests/ProcessPtyFactoryTests.cs`, `Scaffolding/WizardAnswers.cs` — leave them untouched and out of every commit). Push is the owner's call unless asked.

**Baseline:** build 0/0, 465 tests green (257 Core + 208 App). Repo `BrandtVavasour/FastLane-GUI-Tool`, branch `main`.

---

## File Structure

**Create:**
- `src/LaunchFast.Core/Updates/ReleaseInfo.cs` — record for a release (tag + url).
- `src/LaunchFast.Core/Updates/GitHubReleases.cs` — pure `ParseLatest` + `IsNewer`.
- `src/LaunchFast.Core/Updates/AppVersion.cs` — running app version accessor.
- `src/LaunchFast.App/Services/UpdateService.cs` — fail-silent HTTP check.
- `src/LaunchFast.Core.Tests/GitHubReleasesTests.cs`
- `src/LaunchFast.App.Tests/UpdateServiceTests.cs`
- `build/macos/Info.plist`, `build/macos/make-app.sh`, `build/macos/make-icon.sh`, `build/macos/LaunchFast.icns`
- `.github/workflows/release.yml`

**Modify:**
- `src/LaunchFast.App/ViewModels/LauncherViewModel.cs` — update banner state.
- `src/LaunchFast.App/Views/LauncherView.axaml` (+ `.axaml.cs`) — banner UI + open handler.
- `src/LaunchFast.App/ViewModels/ShellViewModel.cs` — kick off the check.
- `src/LaunchFast.App/Services/AppServices.cs` — wire `UpdateService`.
- `readme.md`, `claude.md`, `PROGRESS.md` — docs.

---

## Task 1: Core — `GitHubReleases.IsNewer` semver compare

**Files:**
- Create: `src/LaunchFast.Core/Updates/GitHubReleases.cs`
- Test: `src/LaunchFast.Core.Tests/GitHubReleasesTests.cs`

- [ ] **Step 1: Write the failing test**

```csharp
using LaunchFast.Core.Updates;

namespace LaunchFast.Core.Tests;

public class GitHubReleasesTests
{
    [TestCase("0.1.0", "0.2.0", true)]
    [TestCase("0.1.0", "v0.2.0", true)]      // 'v' prefix tolerated
    [TestCase("0.1.0", "0.1.1", true)]
    [TestCase("1.0.0", "1.0.0", false)]      // equal is not newer
    [TestCase("0.2.0", "0.1.9", false)]      // older
    [TestCase("0.1.0", "0.1.0.0", false)]    // 4th component ignored, equal
    [TestCase("0.1.0", "v1", true)]          // missing components treated as 0
    [TestCase("0.1.0", "garbage", false)]    // non-numeric -> not newer
    [TestCase("0.1.0", "0.2.0-beta", true)]  // prerelease suffix stripped
    public void IsNewer_compares_semver(string current, string latest, bool expected)
    {
        Assert.That(GitHubReleases.IsNewer(current, latest), Is.EqualTo(expected));
    }
}
```

- [ ] **Step 2: Run it to verify it fails**

Run: `dotnet test src/LaunchFast.Core.Tests/LaunchFast.Core.Tests.csproj --filter GitHubReleases`
Expected: FAIL — `GitHubReleases` does not exist (compile error).

- [ ] **Step 3: Write minimal implementation**

```csharp
namespace LaunchFast.Core.Updates;

/// <summary>
/// Pure helpers for the in-app update check: comparing the running version to the
/// latest GitHub release tag, and parsing the GitHub releases API response. Total —
/// never throws.
/// </summary>
public static class GitHubReleases
{
    /// <summary>
    /// True only when <paramref name="latest"/> is a strictly greater version than
    /// <paramref name="current"/>. Tolerates a leading 'v', ignores any 4th component
    /// and prerelease/build suffixes, treats missing components as 0, and returns false
    /// for anything non-numeric (can't compare → not newer).
    /// </summary>
    public static bool IsNewer(string current, string latest)
    {
        var c = Parse(current);
        var l = Parse(latest);
        if (c is null || l is null) return false;

        for (var i = 0; i < 3; i++)
        {
            if (l[i] > c[i]) return true;
            if (l[i] < c[i]) return false;
        }
        return false;
    }

    static int[]? Parse(string version)
    {
        var v = version.Trim();
        if (v.StartsWith('v') || v.StartsWith('V')) v = v[1..];

        var cut = v.IndexOfAny(['+', '-']);
        if (cut >= 0) v = v[..cut];

        var parts = v.Split('.');
        var result = new int[3];
        for (var i = 0; i < 3; i++)
        {
            if (i >= parts.Length) { result[i] = 0; continue; }
            if (!int.TryParse(parts[i], out result[i])) return null;
        }
        return result;
    }
}
```

- [ ] **Step 4: Run it to verify it passes**

Run: `dotnet test src/LaunchFast.Core.Tests/LaunchFast.Core.Tests.csproj --filter GitHubReleases`
Expected: PASS (9 cases).

- [ ] **Step 5: Commit**

```bash
git add src/LaunchFast.Core/Updates/GitHubReleases.cs src/LaunchFast.Core.Tests/GitHubReleasesTests.cs
git commit -m "feat(core): GitHubReleases.IsNewer semver compare"
```

---

## Task 2: Core — `ReleaseInfo` + `GitHubReleases.ParseLatest`

**Files:**
- Create: `src/LaunchFast.Core/Updates/ReleaseInfo.cs`
- Modify: `src/LaunchFast.Core/Updates/GitHubReleases.cs`
- Test: `src/LaunchFast.Core.Tests/GitHubReleasesTests.cs`

- [ ] **Step 1: Write the failing test (append to GitHubReleasesTests)**

```csharp
    [Test]
    public void ParseLatest_reads_tag_and_url()
    {
        var json = """
            { "tag_name": "v0.3.0", "html_url": "https://github.com/o/r/releases/tag/v0.3.0", "name": "0.3.0" }
            """;
        var rel = GitHubReleases.ParseLatest(json);
        Assert.That(rel, Is.Not.Null);
        Assert.That(rel!.TagName, Is.EqualTo("v0.3.0"));
        Assert.That(rel.HtmlUrl, Is.EqualTo("https://github.com/o/r/releases/tag/v0.3.0"));
    }

    [Test]
    public void ParseLatest_returns_null_without_tag()
    {
        Assert.That(GitHubReleases.ParseLatest("""{ "message": "Not Found" }"""), Is.Null);
    }

    [Test]
    public void ParseLatest_returns_null_on_garbage()
    {
        Assert.That(GitHubReleases.ParseLatest("not json"), Is.Null);
    }
```

- [ ] **Step 2: Run it to verify it fails**

Run: `dotnet test src/LaunchFast.Core.Tests/LaunchFast.Core.Tests.csproj --filter GitHubReleases`
Expected: FAIL — `ReleaseInfo` / `ParseLatest` do not exist.

- [ ] **Step 3: Write `ReleaseInfo.cs`**

```csharp
namespace LaunchFast.Core.Updates;

/// <summary>A published GitHub release: its tag and the human-facing release page.</summary>
public sealed record ReleaseInfo(string TagName, string HtmlUrl);
```

- [ ] **Step 4: Add `ParseLatest` to `GitHubReleases` (add the using + method)**

At the top of `GitHubReleases.cs` add:

```csharp
using System.Text.Json;
```

Add inside the class:

```csharp
    /// <summary>
    /// Parses the GitHub <c>releases/latest</c> response into a <see cref="ReleaseInfo"/>.
    /// Returns null on malformed JSON or a missing/empty <c>tag_name</c>. Never throws.
    /// </summary>
    public static ReleaseInfo? ParseLatest(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return null;

            if (!root.TryGetProperty("tag_name", out var tag) ||
                tag.ValueKind != JsonValueKind.String) return null;

            var tagName = tag.GetString();
            if (string.IsNullOrWhiteSpace(tagName)) return null;

            var url = root.TryGetProperty("html_url", out var u) && u.ValueKind == JsonValueKind.String
                ? u.GetString() ?? string.Empty
                : string.Empty;

            return new ReleaseInfo(tagName, url);
        }
        catch (JsonException)
        {
            return null;
        }
    }
```

- [ ] **Step 5: Run it to verify it passes**

Run: `dotnet test src/LaunchFast.Core.Tests/LaunchFast.Core.Tests.csproj --filter GitHubReleases`
Expected: PASS (all cases).

- [ ] **Step 6: Commit**

```bash
git add src/LaunchFast.Core/Updates/ReleaseInfo.cs src/LaunchFast.Core/Updates/GitHubReleases.cs src/LaunchFast.Core.Tests/GitHubReleasesTests.cs
git commit -m "feat(core): ReleaseInfo + GitHubReleases.ParseLatest"
```

---

## Task 3: App — `AppVersion` + `UpdateService`

**Files:**
- Create: `src/LaunchFast.Core/Updates/AppVersion.cs`
- Create: `src/LaunchFast.App/Services/UpdateService.cs`
- Test: `src/LaunchFast.App.Tests/UpdateServiceTests.cs`

- [ ] **Step 1: Write `AppVersion.cs` (Core)**

```csharp
using System.Reflection;

namespace LaunchFast.Core.Updates;

/// <summary>The running application's version, read from the entry assembly. In a dev
/// build this is the <c>Version</c> from Directory.Build.props; in a released build it
/// is the tag the release was built from.</summary>
public static class AppVersion
{
    public static string Current =>
        Assembly.GetEntryAssembly()?.GetName().Version?.ToString(3) ?? "0.0.0";
}
```

- [ ] **Step 2: Write the failing test**

```csharp
using System.Net;
using LaunchFast.App.Services;

namespace LaunchFast.App.Tests;

public class UpdateServiceTests
{
    sealed class StubHandler(HttpStatusCode code, string body) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken ct) =>
            Task.FromResult(new HttpResponseMessage(code)
            {
                Content = new StringContent(body),
            });
    }

    static UpdateService Make(HttpStatusCode code, string body, string current) =>
        new(new HttpClient(new StubHandler(code, body)), currentVersion: current);

    [Test]
    public async Task Returns_release_when_newer()
    {
        var svc = Make(HttpStatusCode.OK,
            """{ "tag_name": "v0.9.0", "html_url": "https://x/releases/tag/v0.9.0" }""",
            current: "0.1.0");

        var rel = await svc.CheckAsync();

        Assert.That(rel, Is.Not.Null);
        Assert.That(rel!.TagName, Is.EqualTo("v0.9.0"));
    }

    [Test]
    public async Task Returns_null_when_current_is_latest()
    {
        var svc = Make(HttpStatusCode.OK,
            """{ "tag_name": "v0.1.0", "html_url": "https://x" }""",
            current: "0.1.0");

        Assert.That(await svc.CheckAsync(), Is.Null);
    }

    [Test]
    public async Task Returns_null_on_http_error()
    {
        var svc = Make(HttpStatusCode.NotFound, "", current: "0.1.0");
        Assert.That(await svc.CheckAsync(), Is.Null);
    }
}
```

- [ ] **Step 3: Run it to verify it fails**

Run: `dotnet test src/LaunchFast.App.Tests/LaunchFast.App.Tests.csproj --filter UpdateService`
Expected: FAIL — `UpdateService` does not exist.

- [ ] **Step 4: Write `UpdateService.cs`**

```csharp
using LaunchFast.Core.Updates;

namespace LaunchFast.App.Services;

/// <summary>
/// Checks GitHub for a newer release of LaunchFast. Fail-silent: any network error,
/// non-200, throttle, or malformed body yields null (no UI noise). Returns a
/// <see cref="ReleaseInfo"/> only when the latest release tag is strictly newer than
/// the running version.
/// </summary>
public sealed class UpdateService
{
    const string DefaultUrl =
        "https://api.github.com/repos/BrandtVavasour/FastLane-GUI-Tool/releases/latest";

    readonly HttpClient _http;
    readonly string _currentVersion;
    readonly string _url;

    public UpdateService(HttpClient http, string? currentVersion = null, string? url = null)
    {
        _http = http;
        _currentVersion = currentVersion ?? AppVersion.Current;
        _url = url ?? DefaultUrl;
    }

    public async Task<ReleaseInfo?> CheckAsync(CancellationToken ct = default)
    {
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, _url);
            req.Headers.UserAgent.ParseAdd("LaunchFast-update-check");
            req.Headers.Accept.ParseAdd("application/vnd.github+json");

            using var resp = await _http.SendAsync(req, ct);
            if (!resp.IsSuccessStatusCode) return null;

            var json = await resp.Content.ReadAsStringAsync(ct);
            var rel = GitHubReleases.ParseLatest(json);
            if (rel is null) return null;

            return GitHubReleases.IsNewer(_currentVersion, rel.TagName) ? rel : null;
        }
        catch
        {
            return null;
        }
    }
}
```

- [ ] **Step 5: Run it to verify it passes**

Run: `dotnet test src/LaunchFast.App.Tests/LaunchFast.App.Tests.csproj --filter UpdateService`
Expected: PASS (3 cases).

- [ ] **Step 6: Commit**

```bash
git add src/LaunchFast.Core/Updates/AppVersion.cs src/LaunchFast.App/Services/UpdateService.cs src/LaunchFast.App.Tests/UpdateServiceTests.cs
git commit -m "feat(app): UpdateService + AppVersion"
```

---

## Task 4: App — update banner state on `LauncherViewModel` + shell kickoff

**Files:**
- Modify: `src/LaunchFast.App/ViewModels/LauncherViewModel.cs`
- Modify: `src/LaunchFast.App/ViewModels/ShellViewModel.cs`
- Test: Create `src/LaunchFast.App.Tests/LauncherUpdateBannerTests.cs`

- [ ] **Step 1: Write the failing test**

Create `src/LaunchFast.App.Tests/LauncherUpdateBannerTests.cs`:

```csharp
using LaunchFast.App.ViewModels;
using LaunchFast.Core.Updates;

namespace LaunchFast.App.Tests;

public class LauncherUpdateBannerTests
{
    [Test]
    public void No_update_by_default()
    {
        var vm = LauncherViewModel.ForTest();   // existing test factory; see note below
        Assert.That(vm.HasUpdate, Is.False);
    }

    [Test]
    public void Setting_update_exposes_banner_text_and_url()
    {
        var vm = LauncherViewModel.ForTest();
        vm.SetAvailableUpdate(new ReleaseInfo("v0.2.0", "https://x/releases/tag/v0.2.0"));

        Assert.That(vm.HasUpdate, Is.True);
        Assert.That(vm.UpdateBannerText, Does.Contain("v0.2.0"));
        Assert.That(vm.UpdateUrl, Is.EqualTo("https://x/releases/tag/v0.2.0"));
    }
}
```

> **Note:** if `LauncherViewModel` has no `ForTest()` factory, look at how existing
> `LauncherViewModel` tests (e.g. `LauncherViewModelTests.cs`) construct it and mirror that
> construction inline instead of `ForTest()`. Do NOT add a new factory if the tests already
> build it another way.

- [ ] **Step 2: Run it to verify it fails**

Run: `dotnet test src/LaunchFast.App.Tests/LaunchFast.App.Tests.csproj --filter LauncherUpdateBanner`
Expected: FAIL — `HasUpdate`/`SetAvailableUpdate` do not exist.

- [ ] **Step 3: Add update state to `LauncherViewModel`**

Add `using LaunchFast.Core.Updates;` at the top. Add these members (the class is a
`partial` `ObservableObject`):

```csharp
    [ObservableProperty]
    private ReleaseInfo? _availableUpdate;

    public bool HasUpdate => AvailableUpdate is not null;

    public string UpdateBannerText =>
        AvailableUpdate is { } r ? $"⬆ Update available: {r.TagName}" : string.Empty;

    public string UpdateUrl => AvailableUpdate?.HtmlUrl ?? string.Empty;

    partial void OnAvailableUpdateChanged(ReleaseInfo? value)
    {
        OnPropertyChanged(nameof(HasUpdate));
        OnPropertyChanged(nameof(UpdateBannerText));
        OnPropertyChanged(nameof(UpdateUrl));
    }

    /// <summary>Sets (or clears) the available-update banner. Called by the shell after
    /// the background update check completes.</summary>
    public void SetAvailableUpdate(ReleaseInfo? update) => AvailableUpdate = update;
```

- [ ] **Step 4: Kick off the check in `ShellViewModel`**

In `ShellViewModel`, add `using LaunchFast.Core.Updates;` and a constructor parameter for
the check. Find the existing ShellViewModel constructor; add an optional delegate
parameter `Func<CancellationToken, Task<ReleaseInfo?>>? checkForUpdate = null` and store it
as `_checkForUpdate`. After the launcher is created/loaded, add a fire-and-forget kickoff:

```csharp
    readonly Func<CancellationToken, Task<ReleaseInfo?>>? _checkForUpdate;

    void StartUpdateCheck()
    {
        if (_checkForUpdate is null) return;
        _ = Task.Run(async () =>
        {
            var rel = await _checkForUpdate(CancellationToken.None);
            if (rel is null) return;
            Avalonia.Threading.Dispatcher.UIThread.Post(() => Launcher.SetAvailableUpdate(rel));
        });
    }
```

Call `StartUpdateCheck();` at the end of the constructor (after `Launcher` exists). In
tests, `checkForUpdate` is null → no-op (no network).

- [ ] **Step 5: Run it to verify it passes + full suite still green**

Run: `dotnet test LaunchFast.slnx --filter LauncherUpdateBanner`
Expected: PASS.
Run: `dotnet build LaunchFast.slnx` → 0/0.

- [ ] **Step 6: Commit**

```bash
git add src/LaunchFast.App/ViewModels/LauncherViewModel.cs src/LaunchFast.App/ViewModels/ShellViewModel.cs src/LaunchFast.App.Tests/LauncherUpdateBannerTests.cs
git commit -m "feat(app): update-available banner state + shell kickoff"
```

---

## Task 5: App — banner UI in `LauncherView` + wire real `UpdateService`

**Files:**
- Modify: `src/LaunchFast.App/Views/LauncherView.axaml`
- Modify: `src/LaunchFast.App/Views/LauncherView.axaml.cs`
- Modify: `src/LaunchFast.App/Services/AppServices.cs`

- [ ] **Step 1: Add the banner to `LauncherView.axaml`**

In the toolbar `Grid` (the one with `ColumnDefinitions="Auto,Auto,*,Auto"` containing
"Open project…" / "Register workspace…" / the count pill), the `*` column (Grid.Column=2)
is currently empty space. Place the banner there, right-aligned, before the count pill.
Add this element inside that Grid:

```xml
                <Button Grid.Column="2" Classes="btn accent"
                        HorizontalAlignment="Right" Margin="0,0,12,0"
                        IsVisible="{Binding HasUpdate}"
                        Content="{Binding UpdateBannerText}"
                        Click="OnOpenUpdate" />
```

- [ ] **Step 2: Add the click handler to `LauncherView.axaml.cs`**

Add `using System;` and `using Avalonia.Controls;` if not present. Add the handler
(mirror the existing `OnOpenProject` code-behind style; `LauncherView` already has
code-behind handlers):

```csharp
    private void OnOpenUpdate(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is not ViewModels.LauncherViewModel vm) return;
        if (string.IsNullOrEmpty(vm.UpdateUrl)) return;
        var top = TopLevel.GetTopLevel(this);
        if (top is null) return;
        _ = top.Launcher.LaunchUriAsync(new Uri(vm.UpdateUrl));
    }
```

- [ ] **Step 3: Wire the real `UpdateService` in `AppServices`**

In `AppServices` (the composition root that builds the shell), create a single shared
`HttpClient` and pass an update-check delegate into `ShellViewModel`. Find where
`ShellViewModel` is constructed (e.g. `CreateShell`). Add:

```csharp
        var http = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
        var updates = new UpdateService(http);
```

and pass `checkForUpdate: updates.CheckAsync` to the `ShellViewModel` constructor (the
new optional parameter added in Task 4). Add `using LaunchFast.App.Services;` /
`using System.Net.Http;` / `using System;` as needed.

- [ ] **Step 4: Build + run the existing view/headless tests**

Run: `dotnet build LaunchFast.slnx` → 0/0.
Run: `dotnet test LaunchFast.slnx` → all green (the existing LauncherView headless
construction test must still pass with the new banner + handler).

- [ ] **Step 5: Commit**

```bash
git add src/LaunchFast.App/Views/LauncherView.axaml src/LaunchFast.App/Views/LauncherView.axaml.cs src/LaunchFast.App/Services/AppServices.cs
git commit -m "feat(app): launcher update banner UI + wire UpdateService"
```

---

## Task 6: Packaging — `Info.plist`, `make-app.sh`, icon

**Files:**
- Create: `build/macos/Info.plist`
- Create: `build/macos/make-app.sh`
- Create: `build/macos/make-icon.sh`
- Create: `build/macos/LaunchFast.icns` (generated)

- [ ] **Step 1: Create `build/macos/Info.plist`**

```xml
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
  <key>CFBundleName</key><string>LaunchFast</string>
  <key>CFBundleDisplayName</key><string>LaunchFast</string>
  <key>CFBundleIdentifier</key><string>au.com.jabtech.launchfast</string>
  <key>CFBundleExecutable</key><string>LaunchFast.App</string>
  <key>CFBundleShortVersionString</key><string>__VERSION__</string>
  <key>CFBundleVersion</key><string>__VERSION__</string>
  <key>CFBundleIconFile</key><string>LaunchFast</string>
  <key>CFBundlePackageType</key><string>APPL</string>
  <key>LSMinimumSystemVersion</key><string>12.0</string>
  <key>NSHighResolutionCapable</key><true/>
  <key>LSApplicationCategoryType</key><string>public.app-category.developer-tools</string>
</dict>
</plist>
```

- [ ] **Step 2: Create `build/macos/make-app.sh`**

```bash
#!/usr/bin/env bash
# Builds the unsigned LaunchFast.app for osx-arm64 and zips it.
# Usage (from anywhere): build/macos/make-app.sh <version>
set -euo pipefail

VERSION="${1:?usage: make-app.sh <version>}"
ROOT="$(cd "$(dirname "$0")/../.." && pwd)"
APP_NAME="LaunchFast"
EXE="LaunchFast.App"
RID="osx-arm64"

PUBLISH_DIR="$ROOT/artifacts/publish"
APP_DIR="$ROOT/artifacts/$APP_NAME.app"
ZIP="$ROOT/$APP_NAME-$VERSION-$RID.zip"

rm -rf "$PUBLISH_DIR" "$APP_DIR" "$ZIP"

dotnet publish "$ROOT/src/LaunchFast.App/LaunchFast.App.csproj" \
  -c Release -r "$RID" --self-contained true \
  -p:Version="$VERSION" \
  -o "$PUBLISH_DIR"

mkdir -p "$APP_DIR/Contents/MacOS" "$APP_DIR/Contents/Resources"
cp -R "$PUBLISH_DIR/." "$APP_DIR/Contents/MacOS/"
sed "s/__VERSION__/$VERSION/g" "$ROOT/build/macos/Info.plist" > "$APP_DIR/Contents/Info.plist"

if [ -f "$ROOT/build/macos/$APP_NAME.icns" ]; then
  cp "$ROOT/build/macos/$APP_NAME.icns" "$APP_DIR/Contents/Resources/$APP_NAME.icns"
fi

chmod +x "$APP_DIR/Contents/MacOS/$EXE"

ditto -c -k --keepParent "$APP_DIR" "$ZIP"
echo "Built $ZIP"
```

Then: `chmod +x build/macos/make-app.sh`

- [ ] **Step 3: Create `build/macos/make-icon.sh` (one-time icon generator)**

```bash
#!/usr/bin/env bash
# One-time: regenerate build/macos/LaunchFast.icns (commit the result).
# Requires ImageMagick:  brew install imagemagick
set -euo pipefail

ROOT="$(cd "$(dirname "$0")/../.." && pwd)"
WORK="$(mktemp -d)"
ICONSET="$WORK/icon.iconset"
mkdir -p "$ICONSET"

magick -size 1024x1024 xc:none \
  -fill '#1E8E64' -draw 'roundrectangle 96,96 928,928 200,200' \
  -fill white -gravity center -pointsize 440 -font Helvetica-Bold -annotate 0 'LF' \
  "$WORK/base.png"

gen() { sips -z "$2" "$2" "$WORK/base.png" --out "$ICONSET/$1" >/dev/null; }
gen icon_16x16.png 16
gen icon_16x16@2x.png 32
gen icon_32x32.png 32
gen icon_32x32@2x.png 64
gen icon_128x128.png 128
gen icon_128x128@2x.png 256
gen icon_256x256.png 256
gen icon_256x256@2x.png 512
gen icon_512x512.png 512
gen icon_512x512@2x.png 1024

iconutil -c icns "$ICONSET" -o "$ROOT/build/macos/LaunchFast.icns"
echo "Wrote build/macos/LaunchFast.icns"
```

Then: `chmod +x build/macos/make-icon.sh`

- [ ] **Step 4: Generate the icon**

Run: `which magick || brew install imagemagick`
Run: `build/macos/make-icon.sh`
Expected: `build/macos/LaunchFast.icns` exists.
If ImageMagick can't be installed, skip — `make-app.sh` tolerates a missing icon (the
app ships with the default icon); note this in the commit message.

- [ ] **Step 5: Smoke-test the packaging locally**

Run: `build/macos/make-app.sh 0.1.0`
Expected: `LaunchFast-0.1.0-osx-arm64.zip` is produced; `artifacts/LaunchFast.app` opens
(`open artifacts/LaunchFast.app` — right-click → Open since unsigned). Then clean up:
`rm -rf artifacts LaunchFast-0.1.0-osx-arm64.zip`.

- [ ] **Step 6: Ignore build artifacts**

Append to `.gitignore`:

```
# macOS packaging output
/artifacts/
/LaunchFast-*-osx-arm64.zip
```

- [ ] **Step 7: Commit**

```bash
git add build/macos/Info.plist build/macos/make-app.sh build/macos/make-icon.sh build/macos/LaunchFast.icns .gitignore
git commit -m "build: macOS .app packaging (Info.plist, make-app.sh, icon)"
```

---

## Task 7: Release workflow

**Files:**
- Create: `.github/workflows/release.yml`

- [ ] **Step 1: Create `.github/workflows/release.yml`**

```yaml
name: release

on:
  push:
    tags: ['v*.*.*']

permissions:
  contents: write

jobs:
  build-release:
    runs-on: macos-latest
    steps:
      - uses: actions/checkout@v4

      - name: Set up .NET
        uses: actions/setup-dotnet@v4
        with:
          dotnet-version: 10.0.x

      - name: Derive version from tag
        id: ver
        run: echo "version=${GITHUB_REF_NAME#v}" >> "$GITHUB_OUTPUT"

      - name: Test (gate the release)
        run: dotnet test LaunchFast.slnx

      - name: Build .app + zip
        run: build/macos/make-app.sh "${{ steps.ver.outputs.version }}"

      - name: Publish GitHub Release
        uses: softprops/action-gh-release@v2
        with:
          generate_release_notes: true
          files: LaunchFast-*-osx-arm64.zip
```

- [ ] **Step 2: Validate the YAML locally**

Run: `python3 -c "import yaml,sys; yaml.safe_load(open('.github/workflows/release.yml')); print('ok')"`
Expected: `ok`.

- [ ] **Step 3: Commit**

```bash
git add .github/workflows/release.yml
git commit -m "ci: release workflow — build + publish macOS app on vX.Y.Z tag"
```

- [ ] **Step 4: (Owner-run, after merge) validate end to end**

This step is the owner's to run when ready — it publishes a real release:

```bash
git tag v0.1.1
git push origin v0.1.1
```

Expected: the `release` workflow runs, tests pass, and a GitHub Release `v0.1.1` appears
with `LaunchFast-0.1.1-osx-arm64.zip` attached. Note this in the handoff; do not run it
automatically.

---

## Task 8: Docs

**Files:**
- Modify: `readme.md`
- Modify: `claude.md`
- Modify: `PROGRESS.md`

- [ ] **Step 1: Add an "Install / Update" section to `readme.md`**

Insert after the `## Build / run / test` section:

```markdown
## Install / update

Pre-built macOS apps are attached to each [GitHub Release](https://github.com/BrandtVavasour/FastLane-GUI-Tool/releases).

1. Download `LaunchFast-<version>-osx-arm64.zip` (Apple Silicon).
2. Unzip and drag `LaunchFast.app` to `/Applications`.
3. First launch: right-click the app → **Open** (once — the app is unsigned).

The app checks GitHub on launch and shows an **"⬆ Update available"** banner in the
launcher toolbar when a newer release exists; click it to open the release page, then
repeat the steps above.

**Cutting a release (maintainer):** push a semver tag — the `release` workflow builds
and publishes the app:

```sh
git tag v0.2.0
git push origin v0.2.0
```

You can build the same artifact locally with `build/macos/make-app.sh 0.2.0`.
```

- [ ] **Step 2: Note the pipeline in `claude.md`**

In `claude.md`, under the architecture/tooling notes, add a bullet:

```markdown
- **Releases:** `git push` of a `vX.Y.Z` tag runs `.github/workflows/release.yml`, which
  builds an unsigned `osx-arm64` `LaunchFast.app` via `build/macos/make-app.sh` and
  attaches the zip to a GitHub Release. The app checks `releases/latest` on launch
  (`UpdateService` + `Core/Updates/GitHubReleases`) and shows an update banner.
```

- [ ] **Step 3: Note it in `PROGRESS.md`**

Add a short section:

```markdown
## Sub-project #3 — Release pipeline + in-app update check

- Tag-triggered (`vX.Y.Z`) `release` workflow builds an unsigned `osx-arm64` `.app`
  (`build/macos/make-app.sh` + `Info.plist` + icon) and publishes the zip to GitHub
  Releases.
- In-app update check: `Core/Updates` (`GitHubReleases.IsNewer`/`ParseLatest`,
  `AppVersion`) + `UpdateService` (fail-silent) → launcher "⬆ Update available" banner
  linking to the release page.
- Spec: `docs/superpowers/specs/2026-06-08-ci-release-pipeline-design.md`
- Plan: `docs/superpowers/plans/2026-06-08-ci-release-pipeline.md`
```

- [ ] **Step 4: Commit**

```bash
git add readme.md claude.md PROGRESS.md
git commit -m "docs: install/update + release pipeline"
```

---

## Final verification

- [ ] `dotnet build LaunchFast.slnx` → 0 warnings / 0 errors.
- [ ] `dotnet test LaunchFast.slnx` → all green (baseline 465 + ~15 new ≈ 480).
- [ ] Name-scan: `git ls-files | xargs grep -aliE 'simoncropp|papyrine'` → empty (the
      `BrandtVavasour` repo URL in `UpdateService`/README/workflow is intentional).
- [ ] `build/macos/make-app.sh 0.1.0` produces a launchable `.app` (cleaned up after).
- [ ] The 4 pre-existing IDE-modified files were never staged in any task's commit.
- [ ] Workflow YAML parses.
