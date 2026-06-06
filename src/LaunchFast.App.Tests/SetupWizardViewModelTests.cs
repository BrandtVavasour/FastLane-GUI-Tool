using LaunchFast.App.ViewModels.Wizard;
using LaunchFast.Core.Models;
using LaunchFast.Core.Scaffolding;
using LaunchFast.Core.Scanning;

namespace LaunchFast.App.Tests;

public class SetupWizardViewModelTests
{
    [Test]
    public void Install_mode_builds_a_plan_for_selected_platforms_and_lanes()
    {
        var project = SetupCandidate(out var root);
        var vm = SetupWizardViewModel.ForInstall(project);
        vm.Platforms.Ios = true; vm.Platforms.Android = false;
        vm.Ios.BundleId = "com.acme.demo"; vm.Ios.TeamId = "ABCDE12345";
        vm.Lanes.SetIos(["sync_certificates", "beta"]);
        var plan = vm.BuildPlan();
        Assert.That(plan.Files.Select(f => f.Path), Has.Some.Contains(Path.Combine("ios", "fastlane", "Fastfile").Replace('\\', '/')));
        Assert.That(plan.Files.Any(f => f.Path.Replace('\\', '/').EndsWith("android/fastlane/Fastfile")), Is.False);
    }

    [Test]
    public void Validation_gates_next_on_ios_step()
    {
        var vm = SetupWizardViewModel.ForInstall(SetupCandidate(out _));
        vm.Platforms.Ios = true; vm.Platforms.Android = false;
        vm.Next();                      // advance to iOS step
        Assert.That(vm.Ios.IsValid, Is.False);            // bundle id blank
        vm.Ios.BundleId = "com.x"; vm.Ios.TeamId = "T";
        Assert.That(vm.Ios.IsValid, Is.True);
    }

    [Test]
    public void AddToExisting_flags_present_platforms()
    {
        var project = ProjectWithIosFastlane(out _);
        var vm = SetupWizardViewModel.ForAddToExisting(project);
        Assert.That(vm.Platforms.IosAlreadyPresent, Is.True);
        Assert.That(vm.Platforms.AndroidAlreadyPresent, Is.False);
    }

    [Test]
    public void AddToExisting_inserts_a_lane_into_the_existing_fastfile()
    {
        var project = ProjectWithIosFastlane(out var root);   // ios Fastfile has only :beta
        var vm = SetupWizardViewModel.ForAddToExisting(project);
        vm.Platforms.Ios = true;
        vm.Ios.BundleId = "com.x"; vm.Ios.TeamId = "T";
        vm.Lanes.SetIos(["release"]);                          // add :release
        var plan = vm.BuildPlan();
        var change = plan.Files.Single(f => f.Path.Replace('\\', '/').EndsWith("ios/fastlane/Fastfile"));
        Assert.That(change.Kind, Is.EqualTo(FileChangeKind.InsertLane));
        Assert.That(change.NewContent, Does.Contain("lane :release"));
        Assert.That(change.NewContent, Does.Contain("lane :beta"));   // existing lane preserved
    }

    [Test]
    public async Task Apply_invokes_the_apply_func_and_closes()
    {
        var project = SetupCandidate(out _);
        var applied = new List<ScaffoldPlan>();
        var vm = SetupWizardViewModel.ForInstall(project, apply: p => { applied.Add(p); return Task.CompletedTask; });
        vm.Platforms.Ios = true; vm.Platforms.Android = false;
        vm.Ios.BundleId = "com.x"; vm.Ios.TeamId = "T"; vm.Lanes.SetIos(["beta"]);
        bool closed = false; vm.Closed = () => closed = true;
        await vm.ApplyAsync();
        Assert.That(applied.Count, Is.EqualTo(1));
        Assert.That(closed, Is.True);
    }

    [Test]
    public async Task Apply_sets_IsApplying_and_AppendApplyLog_appends()
    {
        var project = SetupCandidate(out _);
        var vm = SetupWizardViewModel.ForInstall(project, apply: _ => Task.CompletedTask);
        vm.Platforms.Ios = true; vm.Platforms.Android = false;
        vm.Ios.BundleId = "com.acme.demo"; vm.Ios.TeamId = "T"; vm.Lanes.SetIos(["beta"]);

        vm.AppendApplyLog("Fetching gem metadata…");
        vm.AppendApplyLog("Bundle complete!");
        Assert.That(vm.ApplyLog, Has.Count.EqualTo(2));
        Assert.That(vm.ApplyLog[0], Is.EqualTo("Fetching gem metadata…"));

        Assert.That(vm.IsApplying, Is.False);
        await vm.ApplyAsync();
        Assert.That(vm.IsApplying, Is.True);
    }

    /// <summary>
    /// A fastlane-less temp Flutter project (ios/ + android/ + pubspec) — a valid
    /// install candidate. Returns the scanned <see cref="Project"/>; out-param is the root.
    /// </summary>
    static Project SetupCandidate(out string root)
    {
        root = Path.Combine(Path.GetTempPath(), "lf-wizard-" + Guid.NewGuid().ToString("N"), "demo");
        Directory.CreateDirectory(Path.Combine(root, "ios"));
        Directory.CreateDirectory(Path.Combine(root, "android"));
        File.WriteAllText(Path.Combine(root, "pubspec.yaml"), "name: demo\nversion: 1.0.0+1\n");
        return ProjectScanner.TryScanRoot(root)!;
    }

    // ---- Finding 2: flutter_root helper in AddPlatformBlock merge path --------

    [Test]
    public void AddToExisting_android_merge_prepends_flutter_root_when_missing()
    {
        // Existing Android Fastfile has the fastlane dir but NO platform :android block
        // and NO flutter_root definition. The merged result must contain def flutter_root
        // so that Android lanes (e.g. build) can call Dir.chdir(flutter_root).
        var project = ProjectWithAndroidFastlaneNoBlock(out _);
        var vm = SetupWizardViewModel.ForAddToExisting(project);
        vm.Platforms.Android = true;
        vm.Android.Package = "com.example.app";
        vm.Lanes.SetAndroid(["build"]);
        var plan = vm.BuildPlan();
        var change = plan.Files.Single(f => f.Path.Replace('\\', '/').EndsWith("android/fastlane/Fastfile"));
        Assert.That(change.Kind, Is.EqualTo(FileChangeKind.AddPlatformBlock));
        Assert.That(change.NewContent, Does.Contain("def flutter_root"));
        Assert.That(change.NewContent, Does.Contain("platform :android"));
        Assert.That(change.NewContent, Does.Contain("lane :build"));
    }

    [Test]
    public void AddToExisting_android_merge_does_not_duplicate_flutter_root_when_present()
    {
        // Existing Android Fastfile already defines flutter_root.
        // The merged result must NOT contain a second flutter_root definition.
        var project = ProjectWithAndroidFastlaneNoBlockButHasHelper(out _);
        var vm = SetupWizardViewModel.ForAddToExisting(project);
        vm.Platforms.Android = true;
        vm.Android.Package = "com.example.app";
        vm.Lanes.SetAndroid(["build"]);
        var plan = vm.BuildPlan();
        var change = plan.Files.Single(f => f.Path.Replace('\\', '/').EndsWith("android/fastlane/Fastfile"));
        var content = change.NewContent;
        // Exactly one occurrence.
        var firstIndex = content.IndexOf("def flutter_root", StringComparison.Ordinal);
        var lastIndex = content.LastIndexOf("def flutter_root", StringComparison.Ordinal);
        Assert.That(firstIndex, Is.Not.EqualTo(-1), "flutter_root should be present");
        Assert.That(firstIndex, Is.EqualTo(lastIndex), "flutter_root must not be duplicated");
    }

    /// <summary>
    /// A temp project with an Android fastlane dir, a Fastfile that has NO platform
    /// block and NO flutter_root definition — the merge path must prepend the helper.
    /// </summary>
    static Project ProjectWithAndroidFastlaneNoBlock(out string root)
    {
        root = Path.Combine(Path.GetTempPath(), "lf-wizard-android-" + Guid.NewGuid().ToString("N"), "demo");
        var androidFl = Path.Combine(root, "android", "fastlane");
        Directory.CreateDirectory(androidFl);
        Directory.CreateDirectory(Path.Combine(root, "ios"));
        File.WriteAllText(Path.Combine(root, "pubspec.yaml"), "name: demo\nversion: 1.0.0+1\n");
        // Minimal Fastfile with no platform block and no flutter_root.
        File.WriteAllText(Path.Combine(androidFl, "Fastfile"),
            "default_platform(:android)\n\n# no platform block yet\n");
        return ProjectScanner.TryScanRoot(root)!;
    }

    /// <summary>
    /// Like <see cref="ProjectWithAndroidFastlaneNoBlock"/> but the Fastfile already
    /// defines <c>flutter_root</c> — the merge must not duplicate it.
    /// </summary>
    static Project ProjectWithAndroidFastlaneNoBlockButHasHelper(out string root)
    {
        root = Path.Combine(Path.GetTempPath(), "lf-wizard-android2-" + Guid.NewGuid().ToString("N"), "demo");
        var androidFl = Path.Combine(root, "android", "fastlane");
        Directory.CreateDirectory(androidFl);
        Directory.CreateDirectory(Path.Combine(root, "ios"));
        File.WriteAllText(Path.Combine(root, "pubspec.yaml"), "name: demo\nversion: 1.0.0+1\n");
        // Fastfile already has flutter_root but no platform block.
        File.WriteAllText(Path.Combine(androidFl, "Fastfile"),
            "default_platform(:android)\n\ndef flutter_root\n  File.expand_path('../..', __dir__)\nend\n\n# no platform block yet\n");
        return ProjectScanner.TryScanRoot(root)!;
    }

    /// <summary>
    /// A temp project with an iOS fastlane dir holding a minimal Fastfile (one
    /// <c>:beta</c> lane) and no Android fastlane. Used by add-to-existing tests.
    /// </summary>
    static Project ProjectWithIosFastlane(out string root)
    {
        root = Path.Combine(Path.GetTempPath(), "lf-wizard-add-" + Guid.NewGuid().ToString("N"), "demo");
        var iosFl = Path.Combine(root, "ios", "fastlane");
        Directory.CreateDirectory(iosFl);
        Directory.CreateDirectory(Path.Combine(root, "android"));
        File.WriteAllText(Path.Combine(root, "pubspec.yaml"), "name: demo\nversion: 1.0.0+1\n");
        File.WriteAllText(Path.Combine(iosFl, "Fastfile"),
            "default_platform(:ios)\n\nplatform :ios do\n  desc \"Beta\"\n  lane :beta do\n  end\nend\n");
        return ProjectScanner.TryScanRoot(root)!;
    }
}
