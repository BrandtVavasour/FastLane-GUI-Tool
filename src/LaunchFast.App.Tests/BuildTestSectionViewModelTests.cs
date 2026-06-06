using LaunchFast.App.ViewModels;
using LaunchFast.Core.Building;
using LaunchFast.Core.Models;

namespace LaunchFast.App.Tests;

public class BuildTestSectionViewModelTests
{
    // ---- real build/test config (Gymfile + Scanfile + JUnit) -----------------

    [Test]
    public void Surfaces_real_build_settings_from_gymfile()
    {
        var project = TestProjects.MakeProjectWithBuildTestConfig();
        var vm = new BuildTestSectionViewModel(project);

        Assert.Multiple(() =>
        {
            Assert.That(vm.HasBuildSettings, Is.True);
            Assert.That(vm.SchemeText, Is.EqualTo("Runner"));
            Assert.That(vm.ConfigurationText, Is.EqualTo("Release"));
            Assert.That(vm.ExportAppStore, Is.True);
            Assert.That(vm.ExportAdHoc, Is.False);
            Assert.That(vm.CleanText, Is.EqualTo("Yes"));
            Assert.That(vm.IncludeBitcodeText, Is.EqualTo("No"));
            Assert.That(vm.OutputPath, Is.EqualTo("./build/VendingTracker.ipa"));
        });
    }

    [Test]
    public void Surfaces_real_test_settings_from_scanfile()
    {
        var project = TestProjects.MakeProjectWithBuildTestConfig();
        var vm = new BuildTestSectionViewModel(project);

        Assert.Multiple(() =>
        {
            Assert.That(vm.HasTestSettings, Is.True);
            Assert.That(vm.TestSchemeText, Is.EqualTo("RunnerTests"));
            Assert.That(vm.TestPlanText, Is.EqualTo("FullSuite"));
            Assert.That(vm.HasDevices, Is.True);
            Assert.That(vm.DevicesText, Does.Contain("iPhone 15 Pro"));
        });
    }

    [Test]
    public void Surfaces_real_results_from_junit_report()
    {
        var project = TestProjects.MakeProjectWithBuildTestConfig();
        var vm = new BuildTestSectionViewModel(project);

        Assert.Multiple(() =>
        {
            Assert.That(vm.HasResults, Is.True);
            Assert.That(vm.PassedCount, Is.EqualTo("4"));
            Assert.That(vm.FailedCount, Is.EqualTo("1"));
            Assert.That(vm.SkippedCount, Is.EqualTo("1"));
            Assert.That(vm.Results, Has.Count.EqualTo(2));
            Assert.That(vm.PassFraction, Is.EqualTo(4.0 / 6).Within(0.001));
            Assert.That(vm.LastRunMeta, Is.EqualTo("1m 12s"));
        });

        var ui = vm.Results.Single(r => r.Name == "UITests");
        Assert.That(ui.IsFail, Is.True);
        Assert.That(ui.Count, Does.Contain("1 failed"));
    }

    [Test]
    public void Falls_back_to_fastfile_build_app_when_no_gymfile()
    {
        var cfg = new BuildTestConfig(
            HasIos: true,
            Build: new BuildSettings("Runner", "Release", "ad-hoc", Clean: true,
                IncludeBitcode: null, OutputPath: null),
            Test: null,
            LatestResults: null);

        var vm = new BuildTestSectionViewModel(
            TestProjects.MakeFlutterProjectWithRealFastfiles(),
            readConfig: _ => cfg);

        Assert.Multiple(() =>
        {
            Assert.That(vm.SchemeText, Is.EqualTo("Runner"));
            Assert.That(vm.ExportAdHoc, Is.True);
            Assert.That(vm.IncludeBitcodeText, Is.EqualTo("Not set"));
            Assert.That(vm.OutputPath, Is.EqualTo("Not configured"));
        });
    }

    // ---- honest empty states -------------------------------------------------

    [Test]
    public void Shows_not_configured_and_empty_results_when_nothing_on_disk()
    {
        // The sample fastfile project uses `flutter build ipa` (no gym/scan), and
        // has no JUnit report — so everything is honestly empty.
        var project = TestProjects.MakeFlutterProjectWithRealFastfiles();
        var vm = new BuildTestSectionViewModel(project);

        Assert.Multiple(() =>
        {
            Assert.That(vm.HasBuildSettings, Is.False);
            Assert.That(vm.HasTestSettings, Is.False);
            Assert.That(vm.HasResults, Is.False);
            Assert.That(vm.SchemeText, Is.EqualTo("Not configured"));
            Assert.That(vm.OutputPath, Is.EqualTo("Not configured"));
            Assert.That(vm.PassedCount, Is.EqualTo("0"));
            Assert.That(vm.PassFraction, Is.EqualTo(0));
            Assert.That(vm.Results, Is.Empty);
            Assert.That(vm.EmptyResultsText, Is.Not.Empty);
        });
    }

    [Test]
    public void Progress_bar_star_widths_are_valid_grid_lengths()
    {
        var project = TestProjects.MakeProjectWithBuildTestConfig();
        var vm = new BuildTestSectionViewModel(project);

        Assert.That(vm.PassStar.IsStar, Is.True);
        Assert.That(vm.RemainderStar.IsStar, Is.True);
        Assert.That(vm.PassStar.Value + vm.RemainderStar.Value, Is.EqualTo(1.0).Within(0.001));
    }

    // ---- run wiring ----------------------------------------------------------

    [Test]
    public void CanRunTests_and_CanBuild_reflect_lane_presence()
    {
        var project = TestProjects.MakeFlutterProjectWithRealFastfiles();

        var both = new BuildTestSectionViewModel(project,
            hasTestLane: () => true, hasBuildLane: () => true);
        Assert.That(both.CanRunTests, Is.True);
        Assert.That(both.CanBuild, Is.True);

        var neither = new BuildTestSectionViewModel(project,
            hasTestLane: () => false, hasBuildLane: () => false);
        Assert.That(neither.CanRunTests, Is.False);
        Assert.That(neither.CanBuild, Is.False);
    }

    [Test]
    public void RunTests_invokes_the_run_delegate_with_test_and_does_not_throw()
    {
        var project = TestProjects.MakeFlutterProjectWithRealFastfiles();
        (Platform Platform, string Lane)? called = null;

        var vm = new BuildTestSectionViewModel(project,
            runLane: (p, l) => called = (p, l),
            hasTestLane: () => true);

        Assert.DoesNotThrow(() => vm.RunTestsCommand.Execute(null));

        Assert.That(called, Is.Not.Null);
        Assert.That(called!.Value.Platform, Is.EqualTo(Platform.Ios));
        Assert.That(called.Value.Lane, Is.EqualTo("test"));
    }

    [Test]
    public void Build_invokes_the_run_delegate_with_build_and_does_not_throw()
    {
        var project = TestProjects.MakeFlutterProjectWithRealFastfiles();
        (Platform Platform, string Lane)? called = null;

        var vm = new BuildTestSectionViewModel(project,
            runLane: (p, l) => called = (p, l),
            hasBuildLane: () => true);

        Assert.DoesNotThrow(() => vm.BuildCommand.Execute(null));

        Assert.That(called, Is.Not.Null);
        Assert.That(called!.Value.Platform, Is.EqualTo(Platform.Ios));
        Assert.That(called.Value.Lane, Is.EqualTo("build"));
    }

    [Test]
    public void Run_actions_are_no_ops_when_the_lanes_are_absent()
    {
        var project = TestProjects.MakeFlutterProjectWithRealFastfiles();
        var calls = 0;

        var vm = new BuildTestSectionViewModel(project,
            runLane: (_, _) => calls++,
            hasTestLane: () => false,
            hasBuildLane: () => false);

        vm.RunTestsCommand.Execute(null);
        vm.BuildCommand.Execute(null);
        Assert.That(calls, Is.EqualTo(0));
    }
}
