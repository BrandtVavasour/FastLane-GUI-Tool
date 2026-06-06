using LaunchFast.App.ViewModels;
using LaunchFast.Core.Models;

namespace LaunchFast.App.Tests;

public class StoreListingSectionViewModelTests
{
    [Test]
    public void Defaults_to_ios_with_first_locale_and_real_fields()
    {
        var project = TestProjects.MakeProjectWithStoreMetadata();
        var vm = new StoreListingSectionViewModel(project);

        Assert.That(vm.Platform, Is.EqualTo(Platform.Ios));
        Assert.That(vm.IsEmpty, Is.False);
        Assert.That(vm.Locales, Does.Contain("en-US"));
        Assert.That(vm.SelectedLocale, Is.EqualTo("en-US")); // first sorted

        var appName = vm.Fields.Single(f => f.Label == "App name");
        Assert.That(appName.Value, Is.EqualTo("Demo App"));
        Assert.That(appName.Max, Is.EqualTo(30));
        Assert.That(appName.CounterText, Is.EqualTo("8 / 30"));

        var subtitle = vm.Fields.Single(f => f.Label == "Subtitle");
        Assert.That(subtitle.Badge, Is.EqualTo("iOS"));
        Assert.That(subtitle.Value, Is.EqualTo("Track everything"));
    }

    [Test]
    public void Surfaces_ios_screenshots_for_locale()
    {
        var project = TestProjects.MakeProjectWithStoreMetadata();
        var vm = new StoreListingSectionViewModel(project);

        Assert.That(vm.Screenshots, Has.Count.EqualTo(2));
        Assert.That(vm.HasScreenshots, Is.True);
        Assert.That(vm.ScreenshotCountText, Does.Contain("2 uploaded"));
    }

    [Test]
    public void Switching_platform_to_android_re_reads_metadata()
    {
        var project = TestProjects.MakeProjectWithStoreMetadata();
        var vm = new StoreListingSectionViewModel(project);

        vm.Platform = Platform.Android;

        Assert.That(vm.IsEmpty, Is.False);
        Assert.That(vm.SelectedLocale, Is.EqualTo("en-US"));

        var title = vm.Fields.Single(f => f.Label == "Title");
        Assert.That(title.Value, Is.EqualTo("Demo Play"));

        var shortDesc = vm.Fields.Single(f => f.Label == "Short description");
        Assert.That(shortDesc.Badge, Is.EqualTo("Android"));
        Assert.That(shortDesc.Max, Is.EqualTo(80));

        // iOS-only fields are gone on Android.
        Assert.That(vm.Fields.Any(f => f.Label == "Keywords"), Is.False);
    }

    [Test]
    public void Switching_locale_re_reads_fields()
    {
        var project = TestProjects.MakeProjectWithStoreMetadata();
        var vm = new StoreListingSectionViewModel(project)
        {
            SelectedLocale = "ja",
        };

        var appName = vm.Fields.Single(f => f.Label == "App name");
        Assert.That(appName.Value, Is.EqualTo("デモアプリ"));
        // ja has no screenshots dir.
        Assert.That(vm.Screenshots, Is.Empty);
    }

    [Test]
    public void Empty_state_when_no_metadata_on_disk()
    {
        var root = TestProjects.MakeFlutterProject(); // no metadata tree
        var project = LaunchFast.Core.Scanning.ProjectScanner.TryScanRoot(root)!;
        var vm = new StoreListingSectionViewModel(project);

        Assert.That(vm.IsEmpty, Is.True);
        Assert.That(vm.Locales, Is.Empty);
        Assert.That(vm.Fields, Is.Empty);
        Assert.That(vm.EmptyStateText, Does.Contain("fastlane/metadata"));
    }

    [Test]
    public void Over_limit_field_flags_over()
    {
        var field = new StoreFieldViewModel("App name", null, new string('x', 35), 30);
        Assert.That(field.IsOverLimit, Is.True);
        Assert.That(field.CounterText, Is.EqualTo("35 / 30"));

        var ok = new StoreFieldViewModel("App name", null, "short", 30);
        Assert.That(ok.IsOverLimit, Is.False);
        Assert.That(ok.IsNearLimit, Is.False);

        var near = new StoreFieldViewModel("App name", null, new string('y', 29), 30);
        Assert.That(near.IsNearLimit, Is.True);
        Assert.That(near.IsOverLimit, Is.False);
    }
}
