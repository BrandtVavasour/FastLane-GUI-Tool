using LaunchFast.App.ViewModels;
using LaunchFast.Core.Models;
using LaunchFast.Core.Stores;

namespace LaunchFast.App.Tests;

public class WhatsNewSectionViewModelTests
{
    [Test]
    public void Defaults_to_ios_and_surfaces_real_release_notes()
    {
        var project = TestProjects.MakeProjectWithStoreMetadata();
        var vm = new WhatsNewSectionViewModel(project);

        Assert.That(vm.Platform, Is.EqualTo(Platform.Ios));
        Assert.That(vm.IsEmpty, Is.False);
        Assert.That(vm.SelectedLocale?.Code, Is.EqualTo("en-US")); // first sorted
        Assert.That(vm.NoteText, Does.Contain("Faster sync"));
        Assert.That(vm.CharLimit, Is.EqualTo(StoreFieldLimits.AppStoreReleaseNotes)); // 4000
        Assert.That(vm.FastlanePath, Is.EqualTo("fastlane/metadata/en-US/release_notes.txt"));
    }

    [Test]
    public void Locale_dot_reflects_presence_of_notes()
    {
        var project = TestProjects.MakeProjectWithStoreMetadata();
        var vm = new WhatsNewSectionViewModel(project);

        var en = vm.Locales.Single(l => l.Code == "en-US");
        var ja = vm.Locales.Single(l => l.Code == "ja");
        Assert.That(en.HasText, Is.True);   // en-US has release_notes.txt
        Assert.That(ja.HasText, Is.False);  // ja has only name.txt
    }

    [Test]
    public void Switching_to_ja_reads_empty_notes()
    {
        var project = TestProjects.MakeProjectWithStoreMetadata();
        var vm = new WhatsNewSectionViewModel(project);

        var ja = vm.Locales.Single(l => l.Code == "ja");
        vm.SelectLocaleCommand.Execute(ja);

        Assert.That(vm.NoteText, Is.Empty);
        Assert.That(vm.FastlanePath, Is.EqualTo("fastlane/metadata/ja/release_notes.txt"));
    }

    [Test]
    public void Switching_platform_changes_limit_and_path_and_reads_changelog()
    {
        var project = TestProjects.MakeProjectWithStoreMetadata();
        var vm = new WhatsNewSectionViewModel(project)
        {
            Platform = Platform.Android,
        };

        Assert.That(vm.CharLimit, Is.EqualTo(StoreFieldLimits.PlayWhatsNew)); // 500
        Assert.That(vm.SelectedLocale?.Code, Is.EqualTo("en-US"));
        Assert.That(vm.NoteText, Does.Contain("build 9")); // latest changelog
        // Android path includes the selected version's build/versionCode.
        Assert.That(vm.FastlanePath, Does.StartWith("fastlane/metadata/android/en-US/changelogs/"));
    }

    [Test]
    public void Android_version_rail_includes_ondisk_changelog_codes()
    {
        var project = TestProjects.MakeProjectWithStoreMetadata();
        var vm = new WhatsNewSectionViewModel(project)
        {
            Platform = Platform.Android,
        };

        var builds = vm.Versions.Select(v => v.Build).ToList();
        // pubspec build is 9 (current); changelog 8 is an extra on-disk code.
        Assert.That(builds, Does.Contain("9"));
        Assert.That(builds, Does.Contain("8"));
        Assert.That(vm.Versions.All(v => v.IsDerived), Is.True);
    }

    [Test]
    public void Empty_state_when_no_metadata_on_disk()
    {
        var root = TestProjects.MakeFlutterProject(); // no metadata tree
        var project = LaunchFast.Core.Scanning.ProjectScanner.TryScanRoot(root)!;
        var vm = new WhatsNewSectionViewModel(project);

        Assert.That(vm.IsEmpty, Is.True);
        Assert.That(vm.Locales, Is.Empty);
        Assert.That(vm.NoteText, Is.Empty);
        Assert.That(vm.EmptyStateText, Does.Contain("fastlane/metadata"));
    }

    [Test]
    public void Counter_flags_over_limit()
    {
        var project = TestProjects.MakeProjectWithStoreMetadata();
        var vm = new WhatsNewSectionViewModel(project)
        {
            Platform = Platform.Android, // 500 limit
        };

        vm.NoteText = new string('x', 501);
        Assert.That(vm.IsOverLimit, Is.True);
        Assert.That(vm.CounterText, Is.EqualTo("501 / 500"));
    }

    [Test]
    public void ParseVersion_splits_name_and_build()
    {
        Assert.That(WhatsNewSectionViewModel.ParseVersion("1.4.2+18"),
            Is.EqualTo(("1.4.2", "18")));
        Assert.That(WhatsNewSectionViewModel.ParseVersion("2.0.0"),
            Is.EqualTo(("2.0.0", (string?)null)));
        Assert.That(WhatsNewSectionViewModel.ParseVersion(null),
            Is.EqualTo(((string?)null, (string?)null)));
    }

    // ---- editing / save / discard --------------------------------------------

    [Test]
    public void Editing_notes_marks_dirty_and_enables_save()
    {
        var project = TestProjects.MakeProjectWithStoreMetadata();
        var vm = new WhatsNewSectionViewModel(project);

        Assert.That(vm.IsDirty, Is.False);
        Assert.That(vm.SaveChangelogCommand.CanExecute(null), Is.False);

        vm.NoteText = "Brand new iOS release notes.";

        Assert.That(vm.IsDirty, Is.True);
        Assert.That(vm.SaveChangelogCommand.CanExecute(null), Is.True);
        Assert.That(vm.DiscardCommand.CanExecute(null), Is.True);
    }

    [Test]
    public void Save_ios_writes_release_notes_file_and_clears_dirty()
    {
        var project = TestProjects.MakeProjectWithStoreMetadata();
        var vm = new WhatsNewSectionViewModel(project);

        vm.NoteText = "Brand new iOS release notes.";
        vm.SaveChangelogCommand.Execute(null);

        var onDisk = StoreMetadataReader.ReadListing(project, Platform.Ios, "en-US");
        Assert.That(onDisk.ReleaseNotes, Is.EqualTo("Brand new iOS release notes."));
        Assert.That(vm.IsDirty, Is.False);
        Assert.That(vm.SaveFailed, Is.False);
    }

    [Test]
    public void Save_ios_to_empty_locale_makes_dot_full()
    {
        var project = TestProjects.MakeProjectWithStoreMetadata();
        var vm = new WhatsNewSectionViewModel(project);

        // ja starts empty (name only, no release_notes).
        var ja = vm.Locales.Single(l => l.Code == "ja");
        vm.SelectLocaleCommand.Execute(ja);
        Assert.That(ja.HasText, Is.False);

        vm.NoteText = "Japanese notes.";
        vm.SaveChangelogCommand.Execute(null);

        Assert.That(vm.Locales.Single(l => l.Code == "ja").HasText, Is.True);
        Assert.That(StoreMetadataReader.ReadListing(project, Platform.Ios, "ja").ReleaseNotes,
            Is.EqualTo("Japanese notes."));
    }

    [Test]
    public void Save_android_writes_changelog_for_selected_version_code()
    {
        var project = TestProjects.MakeProjectWithStoreMetadata();
        var vm = new WhatsNewSectionViewModel(project) { Platform = Platform.Android };

        // Selected version is the current build (9).
        Assert.That(vm.SelectedVersion?.Build, Is.EqualTo("9"));

        vm.NoteText = "Updated build 9 changelog.";
        vm.SaveChangelogCommand.Execute(null);

        var path = Path.Combine(
            project.AndroidFastlaneDir!, "metadata", "android", "en-US", "changelogs", "9.txt");
        Assert.That(File.ReadAllText(path).Trim(), Is.EqualTo("Updated build 9 changelog."));
        Assert.That(vm.IsDirty, Is.False);
    }

    [Test]
    public void Discard_reverts_notes_to_on_disk_value()
    {
        var project = TestProjects.MakeProjectWithStoreMetadata();
        var vm = new WhatsNewSectionViewModel(project);

        var original = vm.NoteText;
        vm.NoteText = "Scratch notes.";
        Assert.That(vm.IsDirty, Is.True);

        vm.DiscardCommand.Execute(null);

        Assert.That(vm.IsDirty, Is.False);
        Assert.That(vm.NoteText, Is.EqualTo(original));
        // Disk untouched.
        Assert.That(StoreMetadataReader.ReadListing(project, Platform.Ios, "en-US").ReleaseNotes,
            Does.Contain("Faster sync"));
    }
}
