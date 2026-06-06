using LaunchFast.App.ViewModels;
using LaunchFast.Core.Models;

namespace LaunchFast.App.Tests;

public class SigningSectionViewModelTests
{
    [Test]
    public void Exposes_non_empty_placeholder_collections()
    {
        var project = TestProjects.MakeFlutterProjectWithRealFastfiles();
        var vm = new SigningSectionViewModel(project);

        Assert.That(vm.IsPlaceholder, Is.True);
        Assert.That(vm.Certificates, Is.Not.Empty);
        Assert.That(vm.Profiles, Is.Not.Empty);
        Assert.That(vm.Devices, Is.Not.Empty);
    }

    [Test]
    public void CanRunMatch_reflects_whether_the_sync_certificates_lane_exists()
    {
        var project = TestProjects.MakeFlutterProjectWithRealFastfiles();

        var present = new SigningSectionViewModel(project, hasSyncLane: () => true);
        Assert.That(present.CanRunMatch, Is.True);

        var absent = new SigningSectionViewModel(project, hasSyncLane: () => false);
        Assert.That(absent.CanRunMatch, Is.False);
    }

    [Test]
    public void RunMatch_invokes_the_run_delegate_with_sync_certificates_and_does_not_throw()
    {
        var project = TestProjects.MakeFlutterProjectWithRealFastfiles();
        (Platform Platform, string Lane)? called = null;

        var vm = new SigningSectionViewModel(project,
            runLane: (p, l) => called = (p, l),
            hasSyncLane: () => true);

        Assert.DoesNotThrow(() => vm.RunMatchCommand.Execute(null));

        Assert.That(called, Is.Not.Null);
        Assert.That(called!.Value.Platform, Is.EqualTo(Platform.Ios));
        Assert.That(called.Value.Lane, Is.EqualTo("sync_certificates"));
    }

    [Test]
    public void RunMatch_is_a_no_op_when_the_lane_is_absent()
    {
        var project = TestProjects.MakeFlutterProjectWithRealFastfiles();
        var calls = 0;

        var vm = new SigningSectionViewModel(project,
            runLane: (_, _) => calls++,
            hasSyncLane: () => false);

        vm.RunMatchCommand.Execute(null);
        Assert.That(calls, Is.EqualTo(0));
    }
}
