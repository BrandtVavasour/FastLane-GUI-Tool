using LaunchFast.App.ViewModels;
using LaunchFast.Core.Models;
using LaunchFast.Core.Stores;

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

    // ---- editing / save / discard --------------------------------------------

    [Test]
    public void Editing_a_field_marks_dirty_and_enables_save()
    {
        var project = TestProjects.MakeProjectWithStoreMetadata();
        var vm = new StoreListingSectionViewModel(project);

        Assert.That(vm.IsDirty, Is.False);
        Assert.That(vm.SaveCommand.CanExecute(null), Is.False);

        vm.Fields.Single(f => f.Label == "App name").Value = "Renamed App";

        Assert.That(vm.IsDirty, Is.True);
        Assert.That(vm.SaveCommand.CanExecute(null), Is.True);
        Assert.That(vm.DiscardCommand.CanExecute(null), Is.True);
    }

    [Test]
    public void Save_writes_new_value_to_disk_and_clears_dirty()
    {
        var project = TestProjects.MakeProjectWithStoreMetadata();
        var vm = new StoreListingSectionViewModel(project);

        vm.Fields.Single(f => f.Label == "App name").Value = "Renamed App";
        vm.Fields.Single(f => f.Label == "Subtitle").Value = "A new subtitle";
        vm.SaveCommand.Execute(null);

        // On disk now.
        var onDisk = StoreMetadataReader.ReadListing(project, Platform.Ios, "en-US");
        Assert.That(onDisk.Name, Is.EqualTo("Renamed App"));
        Assert.That(onDisk.Subtitle, Is.EqualTo("A new subtitle"));

        // Baseline refreshed → no longer dirty; reloaded field shows the saved value.
        Assert.That(vm.IsDirty, Is.False);
        Assert.That(vm.SaveFailed, Is.False);
        Assert.That(vm.Fields.Single(f => f.Label == "App name").Value, Is.EqualTo("Renamed App"));
    }

    [Test]
    public void Save_android_writes_supply_files()
    {
        var project = TestProjects.MakeProjectWithStoreMetadata();
        var vm = new StoreListingSectionViewModel(project) { Platform = Platform.Android };

        vm.Fields.Single(f => f.Label == "Title").Value = "Renamed Play";
        vm.SaveCommand.Execute(null);

        var onDisk = StoreMetadataReader.ReadListing(project, Platform.Android, "en-US");
        Assert.That(onDisk.Name, Is.EqualTo("Renamed Play"));
        Assert.That(vm.IsDirty, Is.False);
    }

    [Test]
    public void Discard_reverts_edits_to_on_disk_value()
    {
        var project = TestProjects.MakeProjectWithStoreMetadata();
        var vm = new StoreListingSectionViewModel(project);

        var field = vm.Fields.Single(f => f.Label == "App name");
        field.Value = "Scratch edit";
        Assert.That(vm.IsDirty, Is.True);

        vm.DiscardCommand.Execute(null);

        Assert.That(vm.IsDirty, Is.False);
        Assert.That(vm.Fields.Single(f => f.Label == "App name").Value, Is.EqualTo("Demo App"));
        // Disk untouched.
        Assert.That(StoreMetadataReader.ReadListing(project, Platform.Ios, "en-US").Name,
            Is.EqualTo("Demo App"));
    }

    [Test]
    public void Over_limit_edit_is_flagged_but_still_saves()
    {
        var project = TestProjects.MakeProjectWithStoreMetadata();
        var vm = new StoreListingSectionViewModel(project);

        vm.Fields.Single(f => f.Label == "App name").Value = new string('x', 40); // > 30
        Assert.That(vm.HasOverLimit, Is.True);
        Assert.That(vm.SaveCommand.CanExecute(null), Is.True); // not blocked

        vm.SaveCommand.Execute(null);

        Assert.That(vm.SaveFailed, Is.False);
        Assert.That(StoreMetadataReader.ReadListing(project, Platform.Ios, "en-US").Name,
            Is.EqualTo(new string('x', 40)));
    }

    // ---- app-name fallback ----------------------------------------------------

    [Test]
    public void App_name_falls_back_to_native_display_name_when_metadata_lacks_name()
    {
        // Metadata has no name.txt; Info.plist carries a literal display name.
        var project = TestProjects.MakeProjectWithMixedDeviceScreenshots();
        var vm = new StoreListingSectionViewModel(project);

        var appName = vm.Fields.Single(f => f.Label == "App name");
        Assert.That(appName.Value, Is.EqualTo("Example App"));
        // The fallback is the baseline → does not mark the field/VM dirty on load.
        Assert.That(appName.IsDirty, Is.False);
        Assert.That(vm.IsDirty, Is.False);
    }

    // ---- guessed name not persisted to disk ----------------------------------

    [Test]
    public void Save_does_not_create_name_file_when_app_name_field_left_at_fallback()
    {
        // MakeProjectWithMixedDeviceScreenshots has no name.txt; fallback is "Example App".
        var project = TestProjects.MakeProjectWithMixedDeviceScreenshots();
        var vm = new StoreListingSectionViewModel(project);

        var appName = vm.Fields.Single(f => f.Label == "App name");
        Assert.That(appName.Value, Is.EqualTo("Example App"), "pre-condition: fallback is shown");
        Assert.That(appName.IsDirty, Is.False, "pre-condition: not dirty");

        // Edit a DIFFERENT field so Save can execute, but leave App name at the fallback.
        vm.Fields.Single(f => f.Label == "Subtitle").Value = "Edited subtitle";
        Assert.That(vm.IsDirty, Is.True);

        vm.SaveCommand.Execute(null);
        Assert.That(vm.SaveFailed, Is.False);

        // name.txt must NOT have been created.
        var onDisk = StoreMetadataReader.ReadListing(project, Platform.Ios, "en-US");
        Assert.That(onDisk.Name, Is.Null,
            "name.txt should NOT be created when app-name field was left at the computed fallback");
    }

    [Test]
    public void Save_creates_name_file_when_app_name_field_edited_away_from_fallback()
    {
        // MakeProjectWithMixedDeviceScreenshots has no name.txt; fallback is "Example App".
        var project = TestProjects.MakeProjectWithMixedDeviceScreenshots();
        var vm = new StoreListingSectionViewModel(project);

        // User explicitly types a new app name.
        vm.Fields.Single(f => f.Label == "App name").Value = "My New App";
        vm.SaveCommand.Execute(null);
        Assert.That(vm.SaveFailed, Is.False);

        // name.txt IS created because the user authored a real value.
        var onDisk = StoreMetadataReader.ReadListing(project, Platform.Ios, "en-US");
        Assert.That(onDisk.Name, Is.EqualTo("My New App"),
            "name.txt should be written when the user edited the app-name field");
    }

    [Test]
    public void BuildListing_name_is_null_when_field_at_fallback_and_no_name_on_disk()
    {
        // Confirms the ViewModel-level contract without touching disk.
        var project = TestProjects.MakeProjectWithMixedDeviceScreenshots();
        var vm = new StoreListingSectionViewModel(project);

        // Don't edit the name field; the fallback is the displayed value.
        // Trigger dirty via another field so we can inspect BuildListing indirectly
        // by doing a Save and reading back from disk.
        vm.Fields.Single(f => f.Label == "Subtitle").Value = "Changed";
        vm.SaveCommand.Execute(null);

        var onDisk = StoreMetadataReader.ReadListing(project, Platform.Ios, "en-US");
        Assert.That(onDisk.Name, Is.Null);
    }

    // ---- device filtering -----------------------------------------------------

    [Test]
    public void Selecting_ipad_device_filters_screenshots_to_ipad_only()
    {
        var project = TestProjects.MakeProjectWithMixedDeviceScreenshots();
        var vm = new StoreListingSectionViewModel(project);

        // Default device is iPhone → only the iPhone shot.
        Assert.That(vm.Screenshots, Has.Count.EqualTo(1));
        Assert.That(vm.Screenshots[0], Does.Contain("iPhone"));

        vm.SelectedDevice = vm.Devices.Single(d => d.Key == "iPad");

        Assert.That(vm.Screenshots, Has.Count.EqualTo(1));
        Assert.That(vm.Screenshots[0], Does.Contain("iPad"));
    }

    [Test]
    public void Switching_locale_discards_unsaved_edits()
    {
        var project = TestProjects.MakeProjectWithStoreMetadata();
        var vm = new StoreListingSectionViewModel(project);

        vm.Fields.Single(f => f.Label == "App name").Value = "Scratch edit";
        Assert.That(vm.IsDirty, Is.True);

        vm.SelectedLocale = "ja"; // switch away

        Assert.That(vm.IsDirty, Is.False);
        // Disk for en-US never saw the scratch edit.
        Assert.That(StoreMetadataReader.ReadListing(project, Platform.Ios, "en-US").Name,
            Is.EqualTo("Demo App"));
    }
}
