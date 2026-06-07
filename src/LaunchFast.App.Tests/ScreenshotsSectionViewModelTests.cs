using LaunchFast.App.ViewModels;
using LaunchFast.Core.Models;
using LaunchFast.Core.Screenshots;

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
            // These shots are named "0_iphone.png" — no parseable device label, so they
            // don't map to a device class and the toggles stay off.
            Assert.That(vm.SelectedDeviceCount, Is.EqualTo(0));
            // No classifiable shots → "No Snapfile — devices not configured."
            Assert.That(vm.DevicesNote, Is.EqualTo("No Snapfile — devices not configured."));
            // Languages derived from captured-screenshot locales on disk.
            Assert.That(vm.Languages.Select(l => l.Code), Does.Contain("en-US"));
            Assert.That(vm.HasScreenshots, Is.True);
        });
    }

    [Test]
    public void No_snapfile_toggles_reflect_captured_screenshots_on_disk()
    {
        // MakeProjectWithMixedDeviceScreenshots has NO Snapfile but captured
        // iPhone 16 Pro Max + iPad Pro 13-inch (M5) screenshots on disk.
        var project = TestProjects.MakeProjectWithMixedDeviceScreenshots();
        var vm = new ScreenshotsSectionViewModel(project);

        Assert.Multiple(() =>
        {
            Assert.That(vm.NoSnapfile, Is.True);
            // 6.9" (Pro Max) and iPad 13" toggles On from captured shots; others Off.
            Assert.That(vm.Devices.Single(d => d.Name == "iPhone 6.9″").On, Is.True);
            Assert.That(vm.Devices.Single(d => d.Name == "iPad Pro 13″").On, Is.True);
            Assert.That(vm.Devices.Single(d => d.Name == "iPhone 6.5″").On, Is.False);
            Assert.That(vm.Devices.Single(d => d.Name == "iPhone 5.5″").On, Is.False);
            Assert.That(vm.Devices.Single(d => d.Name == "iPad Pro 11″").On, Is.False);
            Assert.That(vm.SelectedDeviceCount, Is.EqualTo(2));
            Assert.That(vm.DevicesNote, Does.Contain("captured"));
        });
    }

    [Test]
    public void Toggle_reads_on_for_current_hardware_captured_without_snapfile()
    {
        // A config with NO Snapfile but a captured iPhone 17 Pro Max screenshot
        // (current hardware the old stale match strings would have missed).
        var config = new SnapshotConfig(
            HasSnapfile: false,
            Devices: [],
            Languages: [],
            Scheme: null,
            LaunchArguments: null,
            FrameitEnabled: false,
            FrameTitle: null,
            FrameBackground: null,
            OutputDirectory: null,
            Captured:
            [
                new ScreenshotGroup("en-US",
                    ["/shots/en-US/iPhone 17 Pro Max-01_home_en.png"]),
            ]);

        var vm = new ScreenshotsSectionViewModel(
            TestProjects.MakeFlutterProjectWithRealFastfiles(),
            readConfig: _ => config);

        Assert.That(vm.Devices.Single(d => d.Name == "iPhone 6.9″").On, Is.True);
        Assert.That(vm.SelectedDeviceCount, Is.EqualTo(1));
    }

    [Test]
    public void Snapfile_configured_device_with_no_shots_reads_on()
    {
        // A Snapfile device list (no captured shots) still drives the toggle On.
        var config = new SnapshotConfig(
            HasSnapfile: true,
            Devices: ["iPhone 8 Plus"],
            Languages: ["en-US"],
            Scheme: null,
            LaunchArguments: null,
            FrameitEnabled: false,
            FrameTitle: null,
            FrameBackground: null,
            OutputDirectory: null,
            Captured: []);

        var vm = new ScreenshotsSectionViewModel(
            TestProjects.MakeFlutterProjectWithRealFastfiles(),
            readConfig: _ => config);

        Assert.Multiple(() =>
        {
            Assert.That(vm.Devices.Single(d => d.Name == "iPhone 5.5″").On, Is.True);
            Assert.That(vm.SelectedDeviceCount, Is.EqualTo(1));
            Assert.That(vm.DevicesNote, Does.Contain("Snapfile"));
        });
    }

    [Test]
    public void Snapfile_is_authoritative_disk_shots_do_not_add_toggles()
    {
        // Snapfile lists only "iPhone 16 Pro Max" (→ 6.9") but the only captured shots
        // on disk are iPad Pro 13-inch — the disk signal must NOT light the iPad toggle.
        var config = new SnapshotConfig(
            HasSnapfile: true,
            Devices: ["iPhone 16 Pro Max"],
            Languages: ["en-US"],
            Scheme: null,
            LaunchArguments: null,
            FrameitEnabled: false,
            FrameTitle: null,
            FrameBackground: null,
            OutputDirectory: null,
            Captured:
            [
                new ScreenshotGroup("en-US",
                    ["/shots/en-US/iPad Pro 13-inch (M5)-01_home_en.png"]),
            ]);

        var vm = new ScreenshotsSectionViewModel(
            TestProjects.MakeFlutterProjectWithRealFastfiles(),
            readConfig: _ => config);

        Assert.Multiple(() =>
        {
            Assert.That(vm.Devices.Single(d => d.Name == "iPhone 6.9″").On, Is.True);
            Assert.That(vm.Devices.Single(d => d.Name == "iPad Pro 13″").On, Is.False);
            Assert.That(vm.SelectedDeviceCount, Is.EqualTo(1));
        });
    }

    [Test]
    public void DevicesNote_reflects_which_source_drove_the_toggles()
    {
        // Branch 1: HasSnapfile → canonical Snapfile wording.
        var snapfileConfig = new SnapshotConfig(
            HasSnapfile: true,
            Devices: ["iPhone 16 Pro Max"],
            Languages: ["en-US"],
            Scheme: null,
            LaunchArguments: null,
            FrameitEnabled: false,
            FrameTitle: null,
            FrameBackground: null,
            OutputDirectory: null,
            Captured: []);

        var vmSnapfile = new ScreenshotsSectionViewModel(
            TestProjects.MakeFlutterProjectWithRealFastfiles(),
            readConfig: _ => snapfileConfig);
        Assert.That(vmSnapfile.DevicesNote, Is.EqualTo("Toggles reflect the Snapfile device list."));

        // Branch 2: No Snapfile, captured shots that classify → disk-fallback wording.
        var diskConfig = new SnapshotConfig(
            HasSnapfile: false,
            Devices: [],
            Languages: [],
            Scheme: null,
            LaunchArguments: null,
            FrameitEnabled: false,
            FrameTitle: null,
            FrameBackground: null,
            OutputDirectory: null,
            Captured:
            [
                new ScreenshotGroup("en-US",
                    ["/shots/en-US/iPhone 17 Pro Max-01_home_en.png"]),
            ]);

        var vmDisk = new ScreenshotsSectionViewModel(
            TestProjects.MakeFlutterProjectWithRealFastfiles(),
            readConfig: _ => diskConfig);
        Assert.That(vmDisk.DevicesNote,
            Is.EqualTo("No Snapfile — showing device classes with screenshots captured on disk."));

        // Branch 3: No Snapfile, no classifiable shots → not configured wording.
        var emptyConfig = new SnapshotConfig(
            HasSnapfile: false,
            Devices: [],
            Languages: [],
            Scheme: null,
            LaunchArguments: null,
            FrameitEnabled: false,
            FrameTitle: null,
            FrameBackground: null,
            OutputDirectory: null,
            Captured: []);

        var vmEmpty = new ScreenshotsSectionViewModel(
            TestProjects.MakeFlutterProjectWithRealFastfiles(),
            readConfig: _ => emptyConfig);
        Assert.That(vmEmpty.DevicesNote, Is.EqualTo("No Snapfile — devices not configured."));
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
