using LaunchFast.App.ViewModels;
using LaunchFast.Core.Models;

namespace LaunchFast.App.Tests;

public class BuildTestSectionViewModelTests
{
    [Test]
    public void Exposes_non_empty_placeholder_collections()
    {
        var project = TestProjects.MakeFlutterProjectWithRealFastfiles();
        var vm = new BuildTestSectionViewModel(project);

        Assert.That(vm.IsPlaceholder, Is.True);
        Assert.That(vm.Schemes, Is.Not.Empty);
        Assert.That(vm.Configurations, Is.Not.Empty);
        Assert.That(vm.TestPlans, Is.Not.Empty);
        Assert.That(vm.Simulators, Is.Not.Empty);
        Assert.That(vm.BuildToggles, Is.Not.Empty);
        Assert.That(vm.Results, Is.Not.Empty);
    }

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

    [Test]
    public void Export_method_segmented_control_is_mutually_exclusive()
    {
        var project = TestProjects.MakeFlutterProjectWithRealFastfiles();
        var vm = new BuildTestSectionViewModel(project);

        Assert.That(vm.ExportAppStore, Is.True);

        vm.ExportAdHoc = true;
        Assert.That(vm.ExportMethod, Is.EqualTo(ExportMethod.AdHoc));
        Assert.That(vm.ExportAppStore, Is.False);
        Assert.That(vm.ExportAdHoc, Is.True);
    }
}
