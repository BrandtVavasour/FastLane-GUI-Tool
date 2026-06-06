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
