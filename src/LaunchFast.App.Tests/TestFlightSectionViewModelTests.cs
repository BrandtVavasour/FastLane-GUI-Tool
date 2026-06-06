using LaunchFast.App.ViewModels;
using LaunchFast.Core.Models;

namespace LaunchFast.App.Tests;

public class TestFlightSectionViewModelTests
{
    [Test]
    public void Exposes_non_empty_placeholder_testers()
    {
        var project = TestProjects.MakeFlutterProjectWithRealFastfiles();
        var vm = new TestFlightSectionViewModel(project);

        Assert.That(vm.IsPlaceholder, Is.True);
        Assert.That(vm.Testers, Is.Not.Empty);
        Assert.That(vm.NotesCountText, Does.Contain("/ 4000"));
    }

    [Test]
    public void CanDistribute_reflects_whether_the_beta_lane_exists()
    {
        var project = TestProjects.MakeFlutterProjectWithRealFastfiles();

        var present = new TestFlightSectionViewModel(project, hasBetaLane: () => true);
        Assert.That(present.CanDistribute, Is.True);

        var absent = new TestFlightSectionViewModel(project, hasBetaLane: () => false);
        Assert.That(absent.CanDistribute, Is.False);
    }

    [Test]
    public void Distribute_invokes_the_run_delegate_with_beta_and_does_not_throw()
    {
        var project = TestProjects.MakeFlutterProjectWithRealFastfiles();
        (Platform Platform, string Lane)? called = null;

        var vm = new TestFlightSectionViewModel(project,
            runLane: (p, l) => called = (p, l),
            hasBetaLane: () => true);

        Assert.DoesNotThrow(() => vm.DistributeCommand.Execute(null));

        Assert.That(called, Is.Not.Null);
        Assert.That(called!.Value.Platform, Is.EqualTo(Platform.Ios));
        Assert.That(called.Value.Lane, Is.EqualTo("beta"));
    }

    [Test]
    public void Distribute_is_a_no_op_when_the_lane_is_absent()
    {
        var project = TestProjects.MakeFlutterProjectWithRealFastfiles();
        var calls = 0;

        var vm = new TestFlightSectionViewModel(project,
            runLane: (_, _) => calls++,
            hasBetaLane: () => false);

        vm.DistributeCommand.Execute(null);
        Assert.That(calls, Is.EqualTo(0));
    }

    [Test]
    public void Filter_segmented_control_is_mutually_exclusive()
    {
        var project = TestProjects.MakeFlutterProjectWithRealFastfiles();
        var vm = new TestFlightSectionViewModel(project);

        Assert.That(vm.FilterAll, Is.True);

        vm.FilterInternal = true;
        Assert.That(vm.Filter, Is.EqualTo(TesterFilter.Internal));
        Assert.That(vm.FilterAll, Is.False);
        Assert.That(vm.FilterInternal, Is.True);
    }
}
