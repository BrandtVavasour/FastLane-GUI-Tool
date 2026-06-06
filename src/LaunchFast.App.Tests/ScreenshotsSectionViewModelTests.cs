using LaunchFast.App.ViewModels;
using LaunchFast.Core.Models;

namespace LaunchFast.App.Tests;

public class ScreenshotsSectionViewModelTests
{
    [Test]
    public void Surfaces_real_devices_languages_and_scheme_from_snapfile()
    {
        var project = TestProjects.MakeProjectWithSnapshotConfig();
        var vm = new ScreenshotsSectionViewModel(project);

        Assert.Multiple(() =>
        {
            Assert.That(vm.NoSnapfile, Is.False);
            // iPhone 6.9" and iPad Pro 13" match the Snapfile device list.
            Assert.That(vm.Devices.Single(d => d.Name == "iPhone 6.9″").On, Is.True);
            Assert.That(vm.Devices.Single(d => d.Name == "iPad Pro 13″").On, Is.True);
            Assert.That(vm.Devices.Single(d => d.Name == "iPhone 5.5″").On, Is.False);
            Assert.That(vm.SelectedDeviceCount, Is.EqualTo(2));

            Assert.That(vm.Languages.Select(l => l.Code), Is.EqualTo(new[] { "en-US", "ja" }));
            Assert.That(vm.Scheme, Is.EqualTo("DemoAppUITests"));
            Assert.That(vm.LaunchArguments, Is.EqualTo("-FASTLANE_SNAPSHOT YES -ui_testing"));
        });
    }

    [Test]
    public void Surfaces_real_frameit_flag_and_title()
    {
        var project = TestProjects.MakeProjectWithSnapshotConfig();
        var vm = new ScreenshotsSectionViewModel(project);

        Assert.That(vm.FrameitEnabled, Is.True);
        Assert.That(vm.HasFrameTitle, Is.True);
        Assert.That(vm.FrameTitle, Is.EqualTo("Track every machine"));
        Assert.That(vm.ChipText, Does.Contain("frameit"));
    }

    [Test]
    public void Surfaces_captured_screenshots_from_disk_grouped_by_locale()
    {
        var project = TestProjects.MakeProjectWithSnapshotConfig();
        var vm = new ScreenshotsSectionViewModel(project);

        Assert.Multiple(() =>
        {
            Assert.That(vm.CapturedLocales, Is.EquivalentTo(new[] { "en-US", "ja" }));
            // The first locale (en-US) is selected by default → 2 shots.
            Assert.That(vm.HasScreenshots, Is.True);
            Assert.That(vm.Screenshots, Has.Count.EqualTo(2));
            Assert.That(vm.CapturedCountText, Does.Contain("of"));

            // Switching to ja surfaces its single screenshot.
            vm.SelectLocaleCommand.Execute("ja");
            Assert.That(vm.Screenshots, Has.Count.EqualTo(1));
        });
    }

    [Test]
    public void Builds_device_grouped_screenshot_sections()
    {
        // en-US has one iPhone shot and one iPad shot (deliver naming).
        var project = TestProjects.MakeProjectWithMixedDeviceScreenshots();
        var vm = new ScreenshotsSectionViewModel(project);

        Assert.That(vm.ScreenshotGroups, Has.Count.EqualTo(2));

        var iphone = vm.ScreenshotGroups[0]; // iPhone ranked before iPad
        Assert.That(iphone.Device, Is.EqualTo("iPhone 16 Pro Max"));
        Assert.That(iphone.Paths, Has.Count.EqualTo(1));
        Assert.That(iphone.Paths[0], Does.Contain("iPhone"));

        var ipad = vm.ScreenshotGroups[1];
        Assert.That(ipad.Device, Is.EqualTo("iPad Pro 13-inch (M5)"));
        Assert.That(ipad.Paths, Has.Count.EqualTo(1));
        Assert.That(ipad.Paths[0], Does.Contain("iPad"));
    }

    [Test]
    public void No_snapfile_derives_locales_from_disk_and_shows_devices_off()
    {
        // MakeProjectWithStoreMetadata has iOS screenshots under en-US but no Snapfile.
        var project = TestProjects.MakeProjectWithStoreMetadata();
        var vm = new ScreenshotsSectionViewModel(project);

        Assert.Multiple(() =>
        {
            Assert.That(vm.NoSnapfile, Is.True);
            Assert.That(vm.SelectedDeviceCount, Is.EqualTo(0));
            Assert.That(vm.DevicesNote, Does.Contain("No Snapfile"));
            // Languages derived from captured-screenshot locales on disk.
            Assert.That(vm.Languages.Select(l => l.Code), Does.Contain("en-US"));
            Assert.That(vm.HasScreenshots, Is.True);
        });
    }

    [Test]
    public void Empty_project_shows_empty_gallery_and_no_languages()
    {
        var project = TestProjects.MakeFlutterProjectWithRealFastfiles();
        var vm = new ScreenshotsSectionViewModel(project);

        Assert.Multiple(() =>
        {
            Assert.That(vm.HasScreenshots, Is.False);
            Assert.That(vm.HasLanguages, Is.False);
            Assert.That(vm.CapturedCountText, Does.Contain("No screenshots"));
            Assert.That(vm.EmptyGalleryText, Is.Not.Empty);
        });
    }

    [Test]
    public void CanRunSnapshot_reflects_whether_the_screenshots_lane_exists()
    {
        var project = TestProjects.MakeProjectWithSnapshotConfig();

        var present = new ScreenshotsSectionViewModel(project, hasScreenshotsLane: () => true);
        Assert.That(present.CanRunSnapshot, Is.True);

        var absent = new ScreenshotsSectionViewModel(project, hasScreenshotsLane: () => false);
        Assert.That(absent.CanRunSnapshot, Is.False);
    }

    [Test]
    public void RunSnapshot_invokes_the_run_delegate_with_screenshots_and_does_not_throw()
    {
        var project = TestProjects.MakeProjectWithSnapshotConfig();
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
        var project = TestProjects.MakeProjectWithSnapshotConfig();
        var calls = 0;

        var vm = new ScreenshotsSectionViewModel(project,
            runLane: (_, _) => calls++,
            hasScreenshotsLane: () => false);

        vm.RunSnapshotCommand.Execute(null);
        Assert.That(calls, Is.EqualTo(0));
    }
}
