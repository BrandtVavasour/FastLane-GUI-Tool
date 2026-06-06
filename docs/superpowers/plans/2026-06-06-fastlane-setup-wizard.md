# fastlane Setup Wizard Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** A guided wizard that takes a Flutter project from *no fastlane* to *ready-to-deploy* (and adds a missing platform or a single lane to an existing setup), generating files modelled on the owner's proven VendingMachine fastlane, previewing the exact diff, storing secrets in the Keychain, and running `bundle install`.

**Architecture:** Pure Core generators (`FastlaneScaffolder` renders the file set; `FastfileMerger` does Ruby-aware lane insertion; `ProjectFacts` auto-detects bundle id/package/version) feed a reviewable `ScaffoldPlan`. The `ProjectScanner` surfaces fastlane-less Flutter projects; an Avalonia wizard (step VMs + views) collects answers, shows the plan's diffs, and `ProjectScaffoldService` applies it (write files + Keychain + `bundle install` via the PTY backend + re-scan).

**Tech Stack:** .NET 10 / C# 14, Avalonia 11 MVVM (CommunityToolkit.Mvvm), Verify + NUnit 4, Central Package Management. Reuses `KeychainSecretStore`, `IPtyFactory`/`LaneRunner`, `FastfileParser` block logic, the section-shell + theme.

**Spec:** `docs/superpowers/specs/2026-06-06-fastlane-setup-wizard-design.md`

**Baseline:** branch `main`, **351 unit tests** green, build 0/0.

---

## File Structure

```
src/LaunchFast.Core/
  Models/Project.cs                       # + HasFastlane computed property
  Scanning/ProjectScanner.cs              # TryScanRoot also returns fastlane-less Flutter candidates
  Scaffolding/
    WizardAnswers.cs                      # collected inputs (records)
    ProjectFacts.cs                       # auto-detect bundle id / package / app name+version
    LaneTemplate.cs                       # per-lane Ruby template registry (ios + android)
    FastlaneScaffolder.cs                 # render full file set -> ScaffoldPlan
    FastfileMerger.cs                     # Ruby-aware insert-lane / add-platform-block
    ScaffoldPlan.cs                       # FileChange[] + SecretToStore[]
  Core.Tests/                             # ProjectFacts, LaneTemplate/Scaffolder (Verify), Merger, scanner
src/LaunchFast.App/
  Services/ProjectScaffoldService.cs      # apply a ScaffoldPlan (write + keychain + bundle install + rescan)
  ViewModels/Wizard/
    SetupWizardViewModel.cs               # orchestrates steps + mode + Apply
    WizardPlatformsStepViewModel.cs
    WizardIosStepViewModel.cs
    WizardAndroidStepViewModel.cs
    WizardLanesStepViewModel.cs
    WizardReviewStepViewModel.cs
  Views/Wizard/SetupWizardView.axaml(.cs) # step rail + content host + per-step content
  ViewModels/ShellViewModel.cs            # OpenWizard(install|addToExisting); launcher card CTA
  ViewModels/LauncherViewModel.cs + ProjectCardViewModel.cs   # surface setup candidates
  Views/FastfileSectionView.axaml         # "+ Add lane / platform" toolbar button
IntegrationTests/IntegrationTests/ScaffoldIntegrationTests.cs   # real bundle install
```

---

## Phase 0 — Scanner surfaces fastlane-less Flutter projects

### Task 0.1: `Project.HasFastlane`

**Files:**
- Modify: `src/LaunchFast.Core/Models/Project.cs`
- Test: `src/LaunchFast.Core.Tests/ProjectScannerTests.cs` (add)

- [ ] **Step 1: Failing test**

```csharp
[Test]
public void HasFastlane_true_when_a_platform_dir_present()
{
    var p = new Project("n", "/p", null, "/p/ios/fastlane", null, false, null);
    Assert.That(p.HasFastlane, Is.True);
    var none = p with { IosFastlaneDir = null };
    Assert.That(none.HasFastlane, Is.False);
}
```

- [ ] **Step 2: Run → FAIL** (`HasFastlane` missing).
Run: `dotnet test src/LaunchFast.Core.Tests --filter HasFastlane_true_when_a_platform_dir_present`

- [ ] **Step 3: Implement** — add to `Project`:

```csharp
public sealed record Project(
    string Name, string Path, string? Version,
    string? IosFastlaneDir, string? AndroidFastlaneDir, bool HasMatchfile, string? IconPath)
{
    public bool HasFastlane => IosFastlaneDir is not null || AndroidFastlaneDir is not null;
}
```

- [ ] **Step 4: Run → PASS. Step 5: Commit** `feat: Project.HasFastlane`.

### Task 0.2: `TryScanRoot` returns fastlane-less Flutter candidates

**Files:**
- Modify: `src/LaunchFast.Core/Scanning/ProjectScanner.cs`
- Test: `src/LaunchFast.Core.Tests/ProjectScannerTests.cs`

- [ ] **Step 1: Failing tests**

```csharp
[Test]
public void Flutter_project_without_fastlane_is_a_setup_candidate()
{
    var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(Path.Combine(root, "ios"));
    Directory.CreateDirectory(Path.Combine(root, "android"));
    File.WriteAllText(Path.Combine(root, "pubspec.yaml"), "name: demo\nversion: 1.0.0+1\n");

    var p = ProjectScanner.TryScanRoot(root);
    Assert.That(p, Is.Not.Null);
    Assert.That(p!.HasFastlane, Is.False);
    Assert.That(p.Version, Is.EqualTo("1.0.0+1"));
}

[Test]
public void Non_flutter_non_fastlane_dir_is_null()
{
    var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(root);   // no pubspec, no ios/android, no fastlane
    Assert.That(ProjectScanner.TryScanRoot(root), Is.Null);
}
```

- [ ] **Step 2: Run → FAIL** (today the candidate returns null).

- [ ] **Step 3: Implement** — widen `TryScanRoot`:

```csharp
public static Project? TryScanRoot(string root)
{
    var iosFl = Path.Combine(root, "ios", "fastlane");
    var androidFl = Path.Combine(root, "android", "fastlane");
    bool hasIos = Directory.Exists(iosFl);
    bool hasAndroid = Directory.Exists(androidFl);

    // Fastlane-less Flutter project → a "setup candidate" (no fastlane dirs).
    bool isFlutterCandidate =
        File.Exists(Path.Combine(root, "pubspec.yaml")) &&
        (Directory.Exists(Path.Combine(root, "ios")) || Directory.Exists(Path.Combine(root, "android")));

    if (!hasIos && !hasAndroid && !isFlutterCandidate) return null;

    var version = ReadPubspecVersion(Path.Combine(root, "pubspec.yaml"));
    bool match = File.Exists(Path.Combine(iosFl, "Matchfile"));

    return new Project(
        Name: new DirectoryInfo(root).Name,
        Path: root,
        Version: version,
        IosFastlaneDir: hasIos ? iosFl : null,
        AndroidFastlaneDir: hasAndroid ? androidFl : null,
        HasMatchfile: match,
        IconPath: null);
}
```

- [ ] **Step 4: Run → PASS** (and the whole Core suite — no existing test should break: previously-null Flutter candidates now return a project, but no existing test scans a fastlane-less Flutter dir). Run: `dotnet test src/LaunchFast.Core.Tests`.

- [ ] **Step 5: Commit** `feat: scanner surfaces fastlane-less Flutter projects as setup candidates`.

### Task 0.3: Launcher card shows the setup-candidate CTA

**Files:**
- Modify: `src/LaunchFast.App/ViewModels/ProjectCardViewModel.cs`, `Views/LauncherView.axaml`
- Test: `src/LaunchFast.App.Tests/LauncherViewModelTests.cs`

- [ ] **Step 1: Failing test**

```csharp
[Test]
public void Card_without_fastlane_is_a_setup_candidate()
{
    var project = new Project("New App", "/p", "1.0.0+1", null, null, false, null);
    var vm = new ProjectCardViewModel(project);
    Assert.That(vm.NeedsSetup, Is.True);
    Assert.That(vm.HasIos, Is.False);
}
```

- [ ] **Step 2: Run → FAIL. Step 3: Implement** — add to `ProjectCardViewModel`:

```csharp
public bool NeedsSetup => !project.HasFastlane;
```

- [ ] **Step 4:** In `LauncherView.axaml`, in the card template add (bound to `NeedsSetup`) a distinct footer: when `NeedsSetup`, show a "No fastlane" muted label + a `Button Classes="btn accent" Content="Set up →"` whose Click opens the wizard (wired in Task 7.1); hide the platform/match badges. When not, show the existing badges. Use `IsVisible` toggles.

- [ ] **Step 5: Run → PASS. Commit** `feat: launcher setup-candidate card`.

---

## Phase 1 — `ProjectFacts` auto-detection

### Task 1.1: ProjectFacts reader

**Files:**
- Create: `src/LaunchFast.Core/Scaffolding/ProjectFacts.cs`
- Test: `src/LaunchFast.Core.Tests/ProjectFactsTests.cs`

- [ ] **Step 1: Failing test**

```csharp
using LaunchFast.Core.Scaffolding;

public class ProjectFactsTests
{
    static string TempProject(out string root)
    {
        root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "ios", "Runner.xcodeproj"));
        Directory.CreateDirectory(Path.Combine(root, "android", "app"));
        File.WriteAllText(Path.Combine(root, "pubspec.yaml"), "name: demo_app\nversion: 2.3.1+7\n");
        File.WriteAllText(Path.Combine(root, "ios", "Runner.xcodeproj", "project.pbxproj"),
            "PRODUCT_BUNDLE_IDENTIFIER = com.acme.demo;");
        File.WriteAllText(Path.Combine(root, "android", "app", "build.gradle"),
            "android { defaultConfig { applicationId \"com.acme.demo_android\" } }");
        return root;
    }

    [Test]
    public void Reads_bundle_id_package_name_version()
    {
        TempProject(out var root);
        var f = ProjectFacts.Read(root);
        Assert.That(f.IosBundleId, Is.EqualTo("com.acme.demo"));
        Assert.That(f.AndroidPackage, Is.EqualTo("com.acme.demo_android"));
        Assert.That(f.AppName, Is.EqualTo("demo_app"));
        Assert.That(f.Version, Is.EqualTo("2.3.1+7"));
    }

    [Test]
    public void Missing_sources_yield_nulls()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var f = ProjectFacts.Read(root);
        Assert.That(f.IosBundleId, Is.Null);
        Assert.That(f.AndroidPackage, Is.Null);
    }
}
```

- [ ] **Step 2: Run → FAIL. Step 3: Implement**

```csharp
using System.Text.RegularExpressions;
using LaunchFast.Core.Models;

namespace LaunchFast.Core.Scaffolding;

public sealed record ProjectFacts(string? IosBundleId, string? AndroidPackage, string? AppName, string? Version);

public static class ProjectFactsReader   // class name 'ProjectFacts' is the record; the static API is ProjectFacts.Read via a partial—keep one type:
{
}
```

> Note: to keep `ProjectFacts.Read(...)` AND a `ProjectFacts` record, make the record `partial` with a static `Read`:

```csharp
using System.Text.RegularExpressions;

namespace LaunchFast.Core.Scaffolding;

public sealed partial record ProjectFacts(string? IosBundleId, string? AndroidPackage, string? AppName, string? Version)
{
    public static ProjectFacts Read(string root)
    {
        string? bundle = FirstMatch(Path.Combine(root, "ios", "Runner.xcodeproj", "project.pbxproj"),
            @"PRODUCT_BUNDLE_IDENTIFIER\s*=\s*([A-Za-z0-9_.$()-]+)\s*;");
        if (bundle is null || bundle.Contains('$'))
            bundle = PlistString(Path.Combine(root, "ios", "Runner", "Info.plist"), "CFBundleIdentifier") ?? bundle;

        string? pkg = FirstMatch(Path.Combine(root, "android", "app", "build.gradle"),
            @"applicationId\s+[""']([A-Za-z0-9_.]+)[""']")
            ?? FirstMatch(Path.Combine(root, "android", "app", "build.gradle.kts"),
                @"applicationId\s*=?\s*[""']([A-Za-z0-9_.]+)[""']");

        string? name = FirstMatch(Path.Combine(root, "pubspec.yaml"), @"^name:\s*(\S+)");
        string? version = FirstMatch(Path.Combine(root, "pubspec.yaml"), @"^version:\s*(\S+)");
        return new ProjectFacts(bundle, pkg, name, version);
    }

    static string? FirstMatch(string path, string pattern)
    {
        if (!File.Exists(path)) return null;
        try
        {
            var m = Regex.Match(File.ReadAllText(path), pattern, RegexOptions.Multiline);
            return m.Success ? m.Groups[1].Value : null;
        }
        catch { return null; }
    }

    static string? PlistString(string path, string key)
    {
        if (!File.Exists(path)) return null;
        try
        {
            var m = Regex.Match(File.ReadAllText(path),
                $@"<key>{Regex.Escape(key)}</key>\s*<string>([^<]*)</string>");
            var v = m.Success ? m.Groups[1].Value : null;
            return string.IsNullOrWhiteSpace(v) || v.Contains("$(") ? null : v;
        }
        catch { return null; }
    }
}
```

- [ ] **Step 4: Run → PASS. Step 5: Commit** `feat: ProjectFacts auto-detection`.

---

## Phase 2 — Generator: answers, lane templates, scaffolder, plan

### Task 2.1: WizardAnswers + ScaffoldPlan models

**Files:**
- Create: `src/LaunchFast.Core/Scaffolding/WizardAnswers.cs`, `Scaffolding/ScaffoldPlan.cs`

- [ ] **Step 1: Write the models** (records; no behaviour → no test yet)

```csharp
// WizardAnswers.cs
using LaunchFast.Core.Models;
namespace LaunchFast.Core.Scaffolding;

public sealed record WizardAnswers(
    bool Ios, bool Android,
    string? IosBundleId, string? AppleId, string? TeamId, string? ItcTeamId,
    string? MatchGitUrl,
    string? AndroidPackage, string? PlayJsonKeyPath,
    IReadOnlyList<string> IosLanes,       // e.g. ["sync_certificates","beta","release","screenshots"]
    IReadOnlyList<string> AndroidLanes,   // e.g. ["build","internal","beta","production"]
    IReadOnlyDictionary<string,string> DartDefines,   // name -> .env var name, e.g. API_URL
    IReadOnlyList<SecretInput> Secrets);  // key+value to store in Keychain

public sealed record SecretInput(string Key, string Value);
```

```csharp
// ScaffoldPlan.cs
namespace LaunchFast.Core.Scaffolding;

public enum FileChangeKind { Create, InsertLane, AddPlatformBlock, AppendEnv }

public sealed record FileChange(string Path, string OldContent, string NewContent, FileChangeKind Kind);
public sealed record SecretToStore(string Key, string Value);

public sealed record ScaffoldPlan(
    IReadOnlyList<FileChange> Files,
    IReadOnlyList<SecretToStore> Secrets);
```

- [ ] **Step 2: Build** `dotnet build src/LaunchFast.Core`. **Step 3: Commit** `feat: wizard answers + scaffold plan models`.

### Task 2.2: LaneTemplate registry (TDD, Verify)

**Files:**
- Create: `src/LaunchFast.Core/Scaffolding/LaneTemplate.cs`
- Test: `src/LaunchFast.Core.Tests/LaneTemplateTests.cs` + `.verified.txt`

- [ ] **Step 1: Failing test** — Verify-snapshot the rendered Ruby of each named lane for a fixed `WizardAnswers`.

```csharp
using LaunchFast.Core.Models;
using LaunchFast.Core.Scaffolding;

public class LaneTemplateTests
{
    static WizardAnswers Answers() => new(
        Ios: true, Android: true,
        IosBundleId: "com.acme.demo", AppleId: null, TeamId: "ABCDE12345", ItcTeamId: null,
        MatchGitUrl: null, AndroidPackage: "com.acme.demo", PlayJsonKeyPath: null,
        IosLanes: ["sync_certificates","beta","release","screenshots"],
        AndroidLanes: ["build","internal","beta","production"],
        DartDefines: new Dictionary<string,string>{["API_URL"]="API_URL",["API_TOKEN"]="API_TOKEN"},
        Secrets: []);

    [Test]
    public Task Renders_ios_beta() =>
        Verify(LaneTemplate.Render(Platform.Ios, "beta", Answers()));

    [Test]
    public Task Renders_android_production() =>
        Verify(LaneTemplate.Render(Platform.Android, "production", Answers()));

    [Test]
    public void Lists_available_lanes_per_platform()
    {
        Assert.That(LaneTemplate.Available(Platform.Ios),
            Is.EquivalentTo(new[]{"sync_certificates","beta","release","screenshots"}));
        Assert.That(LaneTemplate.Available(Platform.Android),
            Is.EquivalentTo(new[]{"build","internal","beta","production"}));
    }
}
```

- [ ] **Step 2: Run → FAIL.**

- [ ] **Step 3: Implement** `LaneTemplate` — a registry of per-lane renderers. Model the Ruby on the VendingMachine Fastfiles (build with dart-defines, match readonly, supply track promotions). Provide a `desc` + body per lane:

```csharp
using System.Text;
using LaunchFast.Core.Models;

namespace LaunchFast.Core.Scaffolding;

public static class LaneTemplate
{
    public static IReadOnlyList<string> Available(Platform p) => p == Platform.Ios
        ? ["sync_certificates", "beta", "release", "screenshots"]
        : ["build", "internal", "beta", "production"];

    public static string Render(Platform platform, string lane, WizardAnswers a) =>
        platform == Platform.Ios ? RenderIos(lane, a) : RenderAndroid(lane, a);

    static string DartDefineArgs(WizardAnswers a) =>
        string.Concat(a.DartDefines.Select(kv => $"\n        \"--dart-define={kv.Key}=#{{ENV['{kv.Value}']}}\","));

    static string RenderIos(string lane, WizardAnswers a) => lane switch
    {
        "sync_certificates" =>
"""
  desc "Sync code signing certificates"
  lane :sync_certificates do
    match(type: "appstore", readonly: is_ci, git_url: ENV["MATCH_GIT_URL"])
  end
""",
        "beta" =>
$$"""
  desc "Build and upload to TestFlight"
  lane :beta do
    sync_certificates
    Dir.chdir("..") do
      sh("flutter", "clean")
      sh("flutter", "pub", "get")
      sh("flutter", "build", "ipa", "--release",{{DartDefineArgs(a)}}
        "--export-options-plist=#{File.expand_path('../ExportOptions.plist', __dir__)}")
    end
    upload_to_testflight(
      ipa: "../build/ios/ipa/#{ENV['IPA_NAME'] || 'app'}.ipa",
      skip_waiting_for_build_processing: true,
      api_key_path: ENV["APP_STORE_CONNECT_API_KEY_PATH"])
  end
""",
        "release" =>
$$"""
  desc "Build and upload to App Store"
  lane :release do
    sync_certificates
    Dir.chdir("..") do
      sh("flutter", "clean")
      sh("flutter", "pub", "get")
      sh("flutter", "build", "ipa", "--release",{{DartDefineArgs(a)}}
        "--export-options-plist=#{File.expand_path('../ExportOptions.plist', __dir__)}")
    end
    upload_to_app_store(
      ipa: "../build/ios/ipa/#{ENV['IPA_NAME'] || 'app'}.ipa",
      submit_for_review: false, automatic_release: false,
      api_key_path: ENV["APP_STORE_CONNECT_API_KEY_PATH"])
  end
""",
        "screenshots" =>
"""
  desc "Capture screenshots for App Store"
  lane :screenshots do
    capture_screenshots
    upload_to_app_store(skip_binary_upload: true, skip_metadata: true,
      api_key_path: ENV["APP_STORE_CONNECT_API_KEY_PATH"])
  end
""",
        _ => throw new ArgumentException($"Unknown iOS lane '{lane}'")
    };

    static string RenderAndroid(string lane, WizardAnswers a) => lane switch
    {
        "build" =>
$$"""
  desc "Build Flutter app bundle"
  lane :build do
    Dir.chdir(flutter_root) do
      sh("flutter", "clean")
      sh("flutter", "pub", "get")
      sh("flutter", "build", "appbundle", "--release"{{DartDefineArgs(a)}})
    end
  end
""",
        "internal" =>
"""
  desc "Deploy to Google Play internal testing track"
  lane :internal do
    build
    upload_to_play_store(track: "internal", release_status: "completed",
      aab: "../build/app/outputs/bundle/release/app-release.aab")
  end
""",
        "beta" =>
"""
  desc "Promote internal to beta"
  lane :beta do
    upload_to_play_store(track: "internal", track_promote_to: "beta",
      skip_upload_metadata: true, skip_upload_images: true, skip_upload_screenshots: true)
  end
""",
        "production" =>
"""
  desc "Promote beta to production"
  lane :production do
    upload_to_play_store(track: "beta", track_promote_to: "production",
      skip_upload_metadata: true, skip_upload_images: true, skip_upload_screenshots: true)
  end
""",
        _ => throw new ArgumentException($"Unknown Android lane '{lane}'")
    };
}
```

> Note: the `DartDefineArgs` for Android needs a trailing fix (it produces a leading comma+newline that must sit inside the `sh(...)` arg list). During Step 3, adjust spacing so the rendered Ruby is valid; the Verify snapshot is the oracle — eyeball the `.received.txt` before accepting.

- [ ] **Step 4: Run; review `.received.txt`** — confirm each lane's Ruby is valid and matches the VendingMachine style, then accept snapshots:
```bash
for f in src/LaunchFast.Core.Tests/*.received.*; do mv "$f" "${f/received/verified}"; done
dotnet test src/LaunchFast.Core.Tests --filter LaneTemplateTests
```
Expected: PASS.

- [ ] **Step 5: Commit** `feat: per-lane Ruby templates`.

### Task 2.3: FastlaneScaffolder — full file set (TDD, Verify)

**Files:**
- Create: `src/LaunchFast.Core/Scaffolding/FastlaneScaffolder.cs`
- Test: `src/LaunchFast.Core.Tests/FastlaneScaffolderTests.cs` + `.verified.txt`

- [ ] **Step 1: Failing tests** — Verify the full plan's file contents for iOS-only, Android-only, both; assert secrets are placeholders only.

```csharp
[Test]
public Task Generates_ios_only_file_set()
{
    var a = Answers() with { Android = false, AndroidLanes = [] };
    var plan = FastlaneScaffolder.Render(a, root: "/proj");
    return Verify(plan.Files.Select(f => new { f.Path, f.Kind, f.NewContent }));
}

[Test]
public void Env_example_has_placeholders_not_secret_values()
{
    var plan = FastlaneScaffolder.Render(Answers(), "/proj");
    var env = plan.Files.Single(f => f.Path.EndsWith(".env.example")).NewContent;
    Assert.That(env, Does.Contain("MATCH_PASSWORD="));
    Assert.That(env, Does.Not.Contain("supersecret"));   // a real secret value never appears
}
```

- [ ] **Step 2: Run → FAIL. Step 3: Implement** — assemble the Fastfile from `default_platform`, a `platform :ios do … end` block wrapping a `before_all` dotenv loader + the chosen lanes, plus Appfile/Matchfile/Gemfile/`.env.example`. Paths: iOS files under `<root>/ios/fastlane/...` + `<root>/ios/Gemfile`; Android under `<root>/android/fastlane/...` + `<root>/android/Gemfile`; `.env.example` at `<root>/.env.example`. `Kind = Create`.

```csharp
using System.Text;
using LaunchFast.Core.Models;

namespace LaunchFast.Core.Scaffolding;

public static class FastlaneScaffolder
{
    public static ScaffoldPlan Render(WizardAnswers a, string root)
    {
        var files = new List<FileChange>();
        if (a.Ios)
        {
            files.Add(Create(Path.Combine(root, "ios", "fastlane", "Fastfile"), IosFastfile(a)));
            files.Add(Create(Path.Combine(root, "ios", "fastlane", "Appfile"), IosAppfile(a)));
            if (a.IosLanes.Contains("sync_certificates"))
                files.Add(Create(Path.Combine(root, "ios", "fastlane", "Matchfile"), Matchfile(a)));
            files.Add(Create(Path.Combine(root, "ios", "Gemfile"), Gemfile()));
        }
        if (a.Android)
        {
            files.Add(Create(Path.Combine(root, "android", "fastlane", "Fastfile"), AndroidFastfile(a)));
            files.Add(Create(Path.Combine(root, "android", "fastlane", "Appfile"), AndroidAppfile(a)));
            files.Add(Create(Path.Combine(root, "android", "Gemfile"), Gemfile()));
        }
        files.Add(Create(Path.Combine(root, ".env.example"), EnvExample(a)));
        return new ScaffoldPlan(files, a.Secrets.Select(s => new SecretToStore(s.Key, s.Value)).ToList());
    }

    static FileChange Create(string path, string content) =>
        new(path, OldContent: "", NewContent: content, FileChangeKind.Create);

    static string IosFastfile(WizardAnswers a)
    {
        var sb = new StringBuilder();
        sb.AppendLine("require 'dotenv'\n");
        sb.AppendLine("default_platform(:ios)\n");
        sb.AppendLine("platform :ios do");
        sb.AppendLine("  before_all do");
        sb.AppendLine("    setup_ci if ENV['CI']");
        sb.AppendLine("    env_path = File.join(File.expand_path('../..', __dir__), ENV['FASTLANE_ENV'] || '.env.production')");
        sb.AppendLine("    Dotenv.load(env_path) if File.exist?(env_path)");
        sb.AppendLine("  end\n");
        foreach (var lane in a.IosLanes)
            sb.AppendLine(LaneTemplate.Render(Platform.Ios, lane, a)).AppendLine();
        sb.AppendLine("end");
        return sb.ToString();
    }

    static string AndroidFastfile(WizardAnswers a)
    {
        var sb = new StringBuilder();
        sb.AppendLine("default_platform(:android)\n");
        sb.AppendLine("def flutter_root; File.expand_path('../..', Dir.pwd); end\n");
        sb.AppendLine("platform :android do");
        foreach (var lane in a.AndroidLanes)
            sb.AppendLine(LaneTemplate.Render(Platform.Android, lane, a)).AppendLine();
        sb.AppendLine("end");
        return sb.ToString();
    }

    static string IosAppfile(WizardAnswers a) =>
        $"app_identifier(\"{a.IosBundleId}\")\napple_id(ENV[\"APPLE_ID\"])\nitc_team_id(ENV[\"ITC_TEAM_ID\"])\nteam_id(\"{a.TeamId}\")\n";

    static string AndroidAppfile(WizardAnswers a) =>
        $"json_key_file(ENV[\"SUPPLY_JSON_KEY\"])\npackage_name(\"{a.AndroidPackage}\")\n";

    static string Matchfile(WizardAnswers a) =>
        $"git_url(ENV[\"MATCH_GIT_URL\"])\nstorage_mode(\"git\")\ntype(\"appstore\")\napp_identifier([\"{a.IosBundleId}\"])\nusername(ENV[\"APPLE_ID\"])\nteam_id(\"{a.TeamId}\")\nreadonly(true)\n";

    static string Gemfile() =>
        "source \"https://rubygems.org\"\n\ngem \"fastlane\"\ngem \"dotenv\"\n";

    static string EnvExample(WizardAnswers a)
    {
        var keys = new List<string>();
        foreach (var v in a.DartDefines.Values) keys.Add(v);
        if (a.Ios) keys.AddRange(["MATCH_GIT_URL", "MATCH_PASSWORD", "APPLE_ID", "ITC_TEAM_ID", "APP_STORE_CONNECT_API_KEY_PATH"]);
        if (a.Android) keys.Add("SUPPLY_JSON_KEY");
        return string.Concat(keys.Distinct().Select(k => $"{k}=\n"));
    }
}
```

- [ ] **Step 4: Run; review `.received.txt`** (confirm a valid Fastfile with the before_all + lanes inside one `platform` block; Appfile/Matchfile/Gemfile/.env.example correct), accept snapshots, re-run → PASS.

- [ ] **Step 5: Commit** `feat: FastlaneScaffolder full file-set generation`.

---

## Phase 3 — `FastfileMerger` (Ruby-aware insertion)

### Task 3.1: Insert a lane into an existing Fastfile + add a platform block

**Files:**
- Create: `src/LaunchFast.Core/Scaffolding/FastfileMerger.cs`
- Test: `src/LaunchFast.Core.Tests/FastfileMergerTests.cs`

- [ ] **Step 1: Failing tests**

```csharp
using LaunchFast.Core.Scaffolding;

public class FastfileMergerTests
{
    const string Existing =
"""
default_platform(:ios)

platform :ios do
  lane :beta do
    build_app
  end
end
""";

    [Test]
    public void Inserts_lane_before_platform_end()
    {
        var laneRuby = "  lane :release do\n    build_app\n  end";
        var merged = FastfileMerger.InsertLane(Existing, laneRuby, "ios");
        // the new lane sits inside the ios block, after beta, before the final end
        var iosBlock = merged[merged.IndexOf("platform :ios do")..];
        Assert.That(iosBlock, Does.Contain("lane :release"));
        Assert.That(merged.TrimEnd().EndsWith("end"), Is.True);
        Assert.That(merged.IndexOf("lane :release"), Is.GreaterThan(merged.IndexOf("lane :beta")));
        Assert.That(merged.IndexOf("lane :release"), Is.LessThan(merged.LastIndexOf("end")));
    }

    [Test]
    public void Adds_platform_block_when_absent()
    {
        var androidBlock = "platform :android do\n  lane :build do\n    gradle(task: \"bundleRelease\")\n  end\nend";
        var merged = FastfileMerger.AddPlatformBlock(Existing, androidBlock);
        Assert.That(merged, Does.Contain("platform :ios do"));
        Assert.That(merged, Does.Contain("platform :android do"));
    }

    [Test]
    public void Insert_returns_unchanged_when_block_missing()
    {
        var merged = FastfileMerger.InsertLane("# empty\n", "  lane :x do\n  end", "ios");
        Assert.That(merged, Does.Not.Contain("lane :x"));   // signals caller to fall back to AddPlatformBlock
        Assert.That(FastfileMerger.HasPlatformBlock("# empty\n", "ios"), Is.False);
    }
}
```

- [ ] **Step 2: Run → FAIL. Step 3: Implement** — reuse the line-based `do/end` depth logic from `FastfileParser.ParseDetailed` (BlockDelta) to find the `platform :<p> do` line and its matching `end`; insert `laneRuby` (+ a blank line) just before that `end`. `HasPlatformBlock` checks for the platform line. `AddPlatformBlock` appends `\n<block>\n`.

```csharp
namespace LaunchFast.Core.Scaffolding;

public static class FastfileMerger
{
    public static bool HasPlatformBlock(string text, string platform) =>
        FindPlatformBounds(text, platform) is not null;

    public static string InsertLane(string text, string laneRuby, string platform)
    {
        var bounds = FindPlatformBounds(text, platform);
        if (bounds is null) return text;            // caller falls back to AddPlatformBlock
        var (_, endLine, lines) = bounds.Value;
        var list = lines.ToList();
        list.Insert(endLine, laneRuby.TrimEnd() + "\n");
        return string.Join('\n', list);
    }

    public static string AddPlatformBlock(string text, string platformBlock) =>
        text.TrimEnd() + "\n\n" + platformBlock.Trim() + "\n";

    // Returns (startLine, endLine index of the platform's closing `end`, all lines).
    static (int Start, int End, string[] Lines)? FindPlatformBounds(string text, string platform)
    {
        var lines = text.Replace("\r\n", "\n").Split('\n');
        int start = -1, depth = 0;
        for (int i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            if (start < 0)
            {
                if (System.Text.RegularExpressions.Regex.IsMatch(line, $@"^\s*platform\s+:{platform}\s+do\b"))
                { start = i; depth = 1; }
                continue;
            }
            depth += BlockDelta(line);
            if (depth == 0) return (start, i, lines);   // this line is the matching `end`
        }
        return null;
    }

    // Mirror FastfileParser.ParseDetailed: +1 per statement-leading block opener / trailing `do`, -1 per leading `end`.
    static int BlockDelta(string raw)
    {
        var line = StripComment(raw).Trim();
        if (line.Length == 0) return 0;
        int delta = 0;
        if (System.Text.RegularExpressions.Regex.IsMatch(line, @"^(if|unless|case|begin|while|until|for|def)\b")) delta++;
        if (System.Text.RegularExpressions.Regex.IsMatch(line, @"\bdo(\s*\|[^|]*\|)?\s*$")) delta++;
        if (System.Text.RegularExpressions.Regex.IsMatch(line, @"^end\b")) delta--;
        return delta;
    }

    static string StripComment(string line)
    {
        bool inS = false, inD = false;
        for (int i = 0; i < line.Length; i++)
        {
            var c = line[i];
            if (c == '\'' && !inD) inS = !inS;
            else if (c == '"' && !inS) inD = !inD;
            else if (c == '#' && !inS && !inD) return line[..i];
        }
        return line;
    }
}
```

> Note: if the App layer already has a `BlockDelta`/`StripComment` in `FastfileParser`, consider extracting a shared internal helper to avoid duplication — but only if it's a clean lift; otherwise the small duplication here is acceptable (flag it in the commit). Keep the Verify snapshot of the existing `ParseDetailed` green.

- [ ] **Step 4: Run → PASS** (+ full Core suite). **Step 5: Commit** `feat: Ruby-aware Fastfile merger`.

---

## Phase 4 — Apply the plan (`ProjectScaffoldService`)

### Task 4.1: ProjectScaffoldService (write files + Keychain; bundle install seam)

**Files:**
- Create: `src/LaunchFast.App/Services/ProjectScaffoldService.cs`
- Test: `src/LaunchFast.App.Tests/ProjectScaffoldServiceTests.cs`

- [ ] **Step 1: Failing test** — apply a plan to a temp dir: files written, secrets stored in a fake `ISecretStore`, and `bundle install` requested via a fake `IPtyFactory` per platform dir.

```csharp
using LaunchFast.Core.Scaffolding;
using LaunchFast.App.Services;

public class ProjectScaffoldServiceTests
{
    [Test]
    public async Task Writes_files_stores_secrets_and_runs_bundle_install()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var plan = new ScaffoldPlan(
            Files: [ new FileChange(Path.Combine(root, "ios", "fastlane", "Fastfile"), "", "FF", FileChangeKind.Create) ],
            Secrets: [ new SecretToStore("MATCH_PASSWORD", "hunter2") ]);

        var secrets = new FakeSecretStore();
        var pty = new RecordingPtyFactory();             // existing test double
        var svc = new ProjectScaffoldService(secrets, pty, projectId: root);

        await svc.ApplyAsync(plan, root);

        Assert.That(File.ReadAllText(Path.Combine(root, "ios", "fastlane", "Fastfile")), Is.EqualTo("FF"));
        Assert.That(secrets.Get(root, "MATCH_PASSWORD"), Is.EqualTo("hunter2"));
        Assert.That(pty.Commands, Does.Contain("bundle"));     // bundle install invoked
        Assert.That(pty.LastCwd, Is.EqualTo(Path.Combine(root, "ios")));
    }
}
```

- [ ] **Step 2: Run → FAIL. Step 3: Implement** — write each `FileChange.NewContent` (create parent dirs), `ISecretStore.Set` each secret, then for each platform dir that got files run `bundle install` via `IPtyFactory.Start("bundle", ["install"], platformDir, env)`, streaming output to an `Action<string>` the caller supplies; expose the `RunHandle`-style completion. Surface write/IO exceptions (don't swallow). Provide an `event Action<string> Output` + an awaitable completion for the wizard's run panel.

```csharp
using LaunchFast.Core.Env;
using LaunchFast.Core.Running;
using LaunchFast.Core.Scaffolding;

namespace LaunchFast.App.Services;

public sealed class ProjectScaffoldService(ISecretStore secrets, IPtyFactory pty, string projectId)
{
    public event Action<string>? Output;

    public async Task ApplyAsync(ScaffoldPlan plan, string root, CancellationToken ct = default)
    {
        foreach (var f in plan.Files)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(f.Path)!);
            await File.WriteAllTextAsync(f.Path, f.NewContent, ct);
        }
        foreach (var s in plan.Secrets) secrets.Set(projectId, s.Key, s.Value);

        foreach (var platformDir in PlatformDirs(plan, root))
            await BundleInstall(platformDir);
    }

    static IEnumerable<string> PlatformDirs(ScaffoldPlan plan, string root)
    {
        var dirs = new HashSet<string>();
        if (plan.Files.Any(f => f.Path.Contains($"{Path.DirectorySeparatorChar}ios{Path.DirectorySeparatorChar}")))
            dirs.Add(Path.Combine(root, "ios"));
        if (plan.Files.Any(f => f.Path.Contains($"{Path.DirectorySeparatorChar}android{Path.DirectorySeparatorChar}")))
            dirs.Add(Path.Combine(root, "android"));
        return dirs;
    }

    Task BundleInstall(string platformDir)
    {
        var tcs = new TaskCompletionSource();
        var proc = pty.Start("bundle", ["install"], platformDir, new Dictionary<string, string>());
        proc.OutputReceived += s => Output?.Invoke(s);
        proc.Exited += _ => tcs.TrySetResult();
        return tcs.Task;
    }
}
```

- [ ] **Step 4: Run → PASS. Step 5: Commit** `feat: ProjectScaffoldService (write + keychain + bundle install)`.

---

## Phase 5 — Wizard view-models

### Task 5.1: Step VMs + SetupWizardViewModel orchestration

**Files:**
- Create: `src/LaunchFast.App/ViewModels/Wizard/*.cs` (the six listed)
- Test: `src/LaunchFast.App.Tests/SetupWizardViewModelTests.cs`

- [ ] **Step 1: Failing tests** — drive the wizard: platform selection gates steps; validation gates Apply; mode (Install vs AddToExisting) offers only missing platforms/lanes; building the plan; Apply calls the service.

```csharp
[Test]
public void Install_mode_builds_a_plan_for_selected_platforms_and_lanes()
{
    var project = SetupCandidate(out var root);   // helper: fastlane-less temp Flutter project
    var vm = SetupWizardViewModel.ForInstall(project);
    vm.Platforms.Ios = true; vm.Platforms.Android = false;
    vm.Ios.BundleId = "com.acme.demo"; vm.Ios.TeamId = "ABCDE12345";
    vm.Lanes.SetIos(["sync_certificates","beta"]);
    var plan = vm.BuildPlan();
    Assert.That(plan.Files.Select(f => f.Path), Has.Some.Contains(Path.Combine("ios","fastlane","Fastfile")));
    Assert.That(plan.Files.Any(f => f.Path.EndsWith("android/fastlane/Fastfile")), Is.False);
}

[Test]
public void AddToExisting_offers_only_missing_platform()
{
    // project has iOS fastlane only → Android offered, iOS lanes limited to those not present
    var project = ProjectWithIosFastlane(out _);
    var vm = SetupWizardViewModel.ForAddToExisting(project);
    Assert.That(vm.Platforms.IosAlreadyPresent, Is.True);
    Assert.That(vm.Platforms.AndroidAlreadyPresent, Is.False);
}

[Test]
public async Task Apply_invokes_the_scaffold_service()
{
    var project = SetupCandidate(out var root);
    var (vm, applied) = WizardWithFakeService(project);   // injects a fake apply callback
    vm.Platforms.Ios = true; vm.Ios.BundleId = "com.x"; vm.Ios.TeamId = "T";
    vm.Lanes.SetIos(["beta"]);
    await vm.ApplyAsync();
    Assert.That(applied.Count, Is.EqualTo(1));
}
```

- [ ] **Step 2: Run → FAIL. Step 3: Implement** the step VMs + `SetupWizardViewModel`:
  - `SetupWizardViewModel` holds the step VMs + a `Mode` enum (`Install`/`AddToExisting`), current `StepIndex`, `Next`/`Back` commands (gated by per-step `IsValid`), `BuildPlan()` (Install → `FastlaneScaffolder.Render`; AddToExisting → per chosen lane, `FastfileMerger.InsertLane` into the existing Fastfile (read from disk) or `AddPlatformBlock`/`FastlaneScaffolder.Render` for a new platform — produce `FileChange`s with the right `Kind` + `OldContent` for the diff), and `ApplyAsync()` (delegates to an injected apply func / `ProjectScaffoldService`).
  - Static factories `ForInstall(Project)` (pre-fills via `ProjectFacts.Read`) and `ForAddToExisting(Project)` (reads existing lanes via `FastfileParser`, flags present platforms/lanes).
  - The Platforms step exposes `IosAlreadyPresent`/`AndroidAlreadyPresent`; the iOS/Android steps expose the typed fields + which are secret; the Lanes step exposes per-platform lane checklists (defaulting to the full set, minus already-present in add-mode); the Review step exposes the `ScaffoldPlan` for diff rendering.
  - Validation: iOS requires BundleId (+ TeamId, + MatchGitUrl when `sync_certificates` chosen); Android requires AndroidPackage (+ PlayJsonKeyPath when a Play lane chosen). Secrets collected into `WizardAnswers.Secrets`.
  - Make the apply path injectable (a `Func<ScaffoldPlan, Task>` defaulting to the real `ProjectScaffoldService`) so tests don't write disk/run bundle.

  (Write the concrete C# for each VM — records of fields with `[ObservableProperty]`, the `BuildPlan`/validation/factory logic above. Keep each VM file focused.)

- [ ] **Step 4: Run → PASS. Step 5: Commit** `feat: setup wizard view-models`.

---

## Phase 6 — Wizard views

### Task 6.1: SetupWizardView (step rail + step content + review diff)

**Files:**
- Create: `src/LaunchFast.App/Views/Wizard/SetupWizardView.axaml(.cs)`
- Modify: `Views/MainWindow.axaml` (host `SetupWizardViewModel` via DataTemplate)
- Test: `src/LaunchFast.App.Tests` headless render

- [ ] **Step 1: Build the view** to match the mockup + theme: a left **step rail** (numbered, current highlighted via the existing nav style) and a content host bound to the current step VM (DataTemplates: Platforms/iOS/Android/Lanes/Review). Platforms = two selectable cards (iOS/Android, with `*AlreadyPresent` shown as "already set up"). iOS/Android = a form of `TextBox.inp` fields (bundle id, team id, match url, package, play json path) + secret `TextBox` (PasswordChar) for MATCH_PASSWORD/APPLE_ID/API_TOKEN. Lanes = `ToggleSwitch` per lane. Review = an `ItemsControl` of the plan's `FileChange`s, each showing the path, a `Kind` pill, and the `NewContent` in a mono code block (for `InsertLane`, show the inserted lane highlighted). Footer: Back / Cancel / Next / **Generate** (enabled only when valid). On Generate → `ApplyAsync` → swap to a run panel showing the `bundle install` output → on completion, return to the launcher (re-scanned).

- [ ] **Step 2: Verify** `dotnet build LaunchFast.slnx` (0/0). Add a headless `[AvaloniaTest]` constructing `SetupWizardView` with an Install-mode VM (couple of steps) — renders without throwing; add a Light/Dark Skia snapshot following `ViewSnapshotTests`. Note the live window needs manual verification.

- [ ] **Step 3: Commit** `feat: setup wizard view`.

---

## Phase 7 — Entry points & wiring

### Task 7.1: Launch the wizard from the launcher card + Fastfile toolbar

**Files:**
- Modify: `ViewModels/ShellViewModel.cs`, `LauncherViewModel.cs`, `Views/LauncherView.axaml`, `Views/FastfileSectionView.axaml(.cs)`, `ViewModels/FastfileSectionViewModel.cs`
- Test: `src/LaunchFast.App.Tests` (shell nav)

- [ ] **Step 1: Failing test** — `ShellViewModel.OpenSetupWizard(project, install)` sets the shell's current view to a `SetupWizardViewModel`; on wizard Cancel/complete it returns to the launcher (re-scanned). A `LauncherViewModel` setup-candidate card's CTA calls it; a `FastfileSectionViewModel.AddLaneCommand` calls it in AddToExisting mode.

```csharp
[Test]
public void Setup_candidate_cta_opens_the_wizard_in_install_mode()
{
    var shell = NewShell();
    var candidate = new Project("New", "/p", "1.0.0+1", null, null, false, null);
    shell.OpenSetupWizard(candidate, install: true);
    Assert.That(shell.CurrentView, Is.InstanceOf<SetupWizardViewModel>());
    Assert.That(((SetupWizardViewModel)shell.CurrentView!).Mode, Is.EqualTo(WizardMode.Install));
}
```

- [ ] **Step 2: Run → FAIL. Step 3: Implement** — `ShellViewModel.OpenSetupWizard(Project, bool install)` builds `SetupWizardViewModel.ForInstall/ForAddToExisting` (wiring the real `ProjectScaffoldService` with `KeychainSecretStore` + the `DefaultPtyFactory`), sets `CurrentView`; the wizard's Cancel/Done callback calls `GoHome()` and refreshes the launcher (re-scan the recents/workspaces so a now-configured project appears with its sections). `LauncherViewModel` gains `OpenSetupCommand(ProjectCardViewModel)` → `shell.OpenSetupWizard(card.Project, install:true)`. `FastfileSectionViewModel` gains `AddLaneCommand` → `shell.OpenSetupWizard(project, install:false)`. Add the `MainWindow` DataTemplate `SetupWizardViewModel → SetupWizardView`. Wire the launcher card "Set up →" button (Task 0.3) + a "＋ Add lane / platform" `.btn` on the Fastfile toolbar.

- [ ] **Step 4: Run → PASS** (+ full suite). **Step 5: Commit** `feat: wizard entry points (launcher CTA + Fastfile toolbar)`.

---

## Phase 8 — Integration test + docs

### Task 8.1: Real `bundle install` integration test

**Files:**
- Create: `IntegrationTests/IntegrationTests/ScaffoldIntegrationTests.cs`

- [ ] **Step 1:** Generate an iOS+Android plan into a temp Flutter-ish dir (write a minimal `pubspec.yaml`), apply the `FileChange`s, then run `bundle install` in `ios/` via the real `DefaultPtyFactory`; assert a `Gemfile.lock` appears and exit 0. **Guard:** `Assert.Ignore` if `bundle` isn't on PATH or the sandbox blocks it.

- [ ] **Step 2: Run** `dotnet test IntegrationTests/IntegrationTests.slnx --filter Scaffold`. Report ran-vs-ignored.

- [ ] **Step 3: Commit** `test: scaffold + bundle install integration test`.

### Task 8.2: Docs

**Files:**
- Modify: `claude.md`, `PROGRESS.md`, `readme.md`

- [ ] **Step 1:** Document the wizard (sub-project #2): the two Core generators, the scanner change, the entry points, and that interactive match/first-upload stay on the Lanes screen. Update the architecture tree + the roadmap (sub-project #2 now done). **Step 2: Commit** `docs: setup wizard`.

---

## Self-Review (completed by plan author)

**Spec coverage:**
- Clone-your-setup templates → 2.2, 2.3 ✓ · Ruby-aware merge → 3.1 ✓ · files + bundle install → 4.1 ✓ ·
  Keychain secrets / .env placeholders → 2.3 (env), 4.1 (Keychain) ✓ · scanner surfaces candidates → 0.2 ✓ ·
  launcher CTA + Fastfile "Add lane" entry points → 0.3, 7.1 ✓ · auto-detection → 1.1 ✓ ·
  wizard steps/fields/validation/modes → 5.1 ✓ · review diff → 5.1 (BuildPlan kinds) + 6.1 (diff view) ✓ ·
  re-scan after apply → 7.1 ✓ · testing (Verify snapshots, merger, scanner, VMs, headless UI, integration) → throughout ✓.

**Placeholder scan:** Phase 5 (step VMs) and Phase 6 (view) describe the VM/XAML at structural level with the key logic spelled out (factories, BuildPlan, validation, diff rendering) rather than full per-field code — intentional, as those are wide/mechanical and verified by the VM tests + headless render; every Core task has complete code. No TODO/TBD in logic.

**Type consistency:** `WizardAnswers`, `ScaffoldPlan`/`FileChange`/`FileChangeKind`/`SecretToStore`, `ProjectFacts`, `LaneTemplate.Render/Available`, `FastfileMerger.InsertLane/AddPlatformBlock/HasPlatformBlock`, `ProjectScaffoldService.ApplyAsync`, `SetupWizardViewModel.ForInstall/ForAddToExisting/BuildPlan/ApplyAsync/Mode`, `Project.HasFastlane` are consistent across tasks. One flagged wart: Task 2.2's Android `DartDefineArgs` spacing needs hand-adjustment so the rendered Ruby is valid — Verify snapshot is the oracle (review `.received.txt` before accepting).

**Known follow-ups (out of scope):** interactive match init / first upload (Lanes screen), editing existing lanes, non-Flutter layouts.
