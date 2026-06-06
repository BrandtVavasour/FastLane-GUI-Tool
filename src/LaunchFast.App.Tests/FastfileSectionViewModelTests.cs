using LaunchFast.App.ViewModels;
using LaunchFast.Core.Models;
using LaunchFast.Core.Scanning;

namespace LaunchFast.App.Tests;

public class FastfileSectionViewModelTests
{
    [Test]
    public void Lanes_are_grouped_per_platform_from_real_fastfiles()
    {
        var project = TestProjects.MakeFlutterProjectWithRealFastfiles();
        var vm = new FastfileSectionViewModel(project);

        Assert.That(vm.IsEmpty, Is.False);
        Assert.That(vm.HasIos, Is.True);
        Assert.That(vm.HasAndroid, Is.True);

        // iOS public lanes from the fixture (private lanes excluded).
        var iosNames = vm.IosLanes.Select(l => l.Name).ToList();
        Assert.That(iosNames, Does.Contain("beta"));
        Assert.That(iosNames, Does.Contain("release"));
        Assert.That(iosNames, Does.Contain("sync_certificates"));
        Assert.That(iosNames, Does.Not.Contain("capture_screenshots_for_device"));

        // Android public lanes.
        var androidNames = vm.AndroidLanes.Select(l => l.Name).ToList();
        Assert.That(androidNames, Does.Contain("build"));
        Assert.That(androidNames, Does.Contain("internal"));
        Assert.That(androidNames, Does.Contain("production"));

        Assert.That(vm.LaneCountText, Does.Contain("lanes"));
    }

    [Test]
    public void Default_selection_is_first_lane_with_steps_and_source()
    {
        var project = TestProjects.MakeFlutterProjectWithRealFastfiles();
        var vm = new FastfileSectionViewModel(project);

        Assert.That(vm.SelectedLane, Is.Not.Null);
        Assert.That(vm.HasSelection, Is.True);
        Assert.That(vm.SelectedLane!.IsSelected, Is.True);

        Assert.That(vm.SelectedTitle, Does.StartWith("lane :"));
        Assert.That(vm.SelectedSource, Is.Not.Empty);
    }

    [Test]
    public void Selecting_a_lane_exposes_its_steps_source_and_invocation()
    {
        var project = TestProjects.MakeFlutterProjectWithRealFastfiles();
        var vm = new FastfileSectionViewModel(project);

        var beta = vm.IosLanes.Single(l => l.Name == "beta");
        vm.SelectLaneCommand.Execute(beta);

        Assert.That(vm.SelectedLane, Is.SameAs(beta));
        Assert.That(beta.IsSelected, Is.True);

        Assert.That(vm.SelectedTitle, Is.EqualTo("lane :beta"));
        Assert.That(vm.SelectedPlatformLabel, Is.EqualTo("iOS"));
        Assert.That(vm.SelectedInvocation, Does.Contain("$ fastlane ios beta"));
        Assert.That(vm.SelectedInvocation, Does.Contain("step"));

        // Steps expose the parsed actions + inferred tools.
        var actions = vm.SelectedSteps.Select(s => s.Action).ToList();
        Assert.That(actions, Does.Contain("upload_to_testflight"));
        var testflight = vm.SelectedSteps.Single(s => s.Action == "upload_to_testflight");
        Assert.That(testflight.Tool, Is.EqualTo("pilot"));
        Assert.That(testflight.HasTool, Is.True);

        // Source is the raw Ruby block.
        Assert.That(vm.SelectedSource, Does.StartWith("  lane :beta do"));
        Assert.That(vm.SelectedSource, Does.Contain("upload_to_testflight"));
    }

    [Test]
    public void View_toggle_switches_between_steps_and_source()
    {
        var project = TestProjects.MakeFlutterProjectWithRealFastfiles();
        var vm = new FastfileSectionViewModel(project);

        // Defaults to Steps.
        Assert.That(vm.View, Is.EqualTo(FastfileView.Steps));
        Assert.That(vm.IsStepsView, Is.True);
        Assert.That(vm.IsSourceView, Is.False);

        vm.IsSourceView = true;
        Assert.That(vm.View, Is.EqualTo(FastfileView.Source));
        Assert.That(vm.IsStepsView, Is.False);
        Assert.That(vm.IsSourceView, Is.True);

        vm.IsStepsView = true;
        Assert.That(vm.View, Is.EqualTo(FastfileView.Steps));
    }

    [Test]
    public void Run_is_wired_to_the_selected_lane()
    {
        var project = TestProjects.MakeFlutterProjectWithRealFastfiles();
        var calls = new List<(Platform, string)>();
        var vm = new FastfileSectionViewModel(project, (p, name) => calls.Add((p, name)));

        var internalLane = vm.AndroidLanes.Single(l => l.Name == "internal");
        vm.SelectLaneCommand.Execute(internalLane);

        Assert.That(vm.CanRunSelected, Is.True);
        vm.RunSelectedLaneCommand.Execute(null);

        Assert.That(calls, Has.Count.EqualTo(1));
        Assert.That(calls[0], Is.EqualTo((Platform.Android, "internal")));
    }

    [Test]
    public void Run_is_disabled_when_no_runner_delegate()
    {
        var project = TestProjects.MakeFlutterProjectWithRealFastfiles();
        var vm = new FastfileSectionViewModel(project, runLane: null);

        Assert.That(vm.CanRunSelected, Is.False);
        Assert.That(vm.RunSelectedLaneCommand.CanExecute(null), Is.False);
    }

    [Test]
    public void Empty_state_when_project_has_no_fastfile()
    {
        var root = TestProjects.MakeFlutterProject(); // fastlane dirs exist but no Fastfile
        var project = ProjectScanner.TryScanRoot(root)!;
        var vm = new FastfileSectionViewModel(project);

        Assert.That(vm.IsEmpty, Is.True);
        Assert.That(vm.Lanes, Is.Empty);
        Assert.That(vm.SelectedLane, Is.Null);
        Assert.That(vm.HasSelection, Is.False);
        Assert.That(vm.EmptyStateText, Does.Contain("Fastfile"));
    }
}
