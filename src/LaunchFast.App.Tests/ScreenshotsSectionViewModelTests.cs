using LaunchFast.App.ViewModels;
using LaunchFast.Core.Models;

namespace LaunchFast.App.Tests;

public class ScreenshotsSectionViewModelTests
{
    [Test]
    public void Exposes_non_empty_placeholder_collections()
    {
        var project = TestProjects.MakeFlutterProjectWithRealFastfiles();
        var vm = new ScreenshotsSectionViewModel(project);

        Assert.That(vm.IsPlaceholder, Is.True);
        Assert.That(vm.Devices, Is.Not.Empty);
        Assert.That(vm.Languages, Is.Not.Empty);
        Assert.That(vm.Schemes, Is.Not.Empty);
        Assert.That(vm.Backgrounds, Is.Not.Empty);
        Assert.That(vm.DevicesSelectedText, Does.Contain("selected"));
    }

    [Test]
    public void CanRunSnapshot_reflects_whether_the_screenshots_lane_exists()
    {
        var project = TestProjects.MakeFlutterProjectWithRealFastfiles();

        var present = new ScreenshotsSectionViewModel(project, hasScreenshotsLane: () => true);
        Assert.That(present.CanRunSnapshot, Is.True);

        var absent = new ScreenshotsSectionViewModel(project, hasScreenshotsLane: () => false);
        Assert.That(absent.CanRunSnapshot, Is.False);
    }

    [Test]
    public void RunSnapshot_invokes_the_run_delegate_with_screenshots_and_does_not_throw()
    {
        var project = TestProjects.MakeFlutterProjectWithRealFastfiles();
        (Platform Platform, string Lane)? called = null;

        var vm = new ScreenshotsSectionViewModel(project,
            runLane: (p, l) => called = (p, l),
            hasScreenshotsLane: () => true);

        Assert.DoesNotThrow(() => vm.RunSnapshotCommand.Execute(null));

        Assert.That(called, Is.Not.Null);
        Assert.That(called!.Value.Platform, Is.EqualTo(Platform.Ios));
        Assert.That(called.Value.Lane, Is.EqualTo("screenshots"));
    }

    [Test]
    public void RunSnapshot_is_a_no_op_when_the_lane_is_absent()
    {
        var project = TestProjects.MakeFlutterProjectWithRealFastfiles();
        var calls = 0;

        var vm = new ScreenshotsSectionViewModel(project,
            runLane: (_, _) => calls++,
            hasScreenshotsLane: () => false);

        vm.RunSnapshotCommand.Execute(null);
        Assert.That(calls, Is.EqualTo(0));
    }

    [Test]
    public void SelectBackground_is_single_select_and_updates_SelectedBackground()
    {
        var project = TestProjects.MakeFlutterProjectWithRealFastfiles();
        var vm = new ScreenshotsSectionViewModel(project);

        var second = vm.Backgrounds[1];
        vm.SelectBackgroundCommand.Execute(second);

        Assert.That(second.Selected, Is.True);
        Assert.That(vm.Backgrounds.Count(b => b.Selected), Is.EqualTo(1));
        Assert.That(vm.SelectedBackground, Is.EqualTo(second.Hex));
    }
}
