using LaunchFast.Core.Models;
using LaunchFast.Core.Stores;

namespace LaunchFast.Core.Tests;

[TestFixture]
public sealed class StoreMetadataWriterTests
{
    string _root = null!;
    Project _project = null!;

    [SetUp]
    public void SetUp()
    {
        _root = Path.Combine(Path.GetTempPath(), "lf-metaw-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        _project = new Project(
            Name: "demo",
            Path: _root,
            Version: "1.0.0",
            IosFastlaneDir: Path.Combine(_root, "ios", "fastlane"),
            AndroidFastlaneDir: Path.Combine(_root, "android", "fastlane"),
            HasMatchfile: false,
            IconPath: null);
    }

    [TearDown]
    public void TearDown()
    {
        try { Directory.Delete(_root, recursive: true); }
        catch (IOException) { /* best effort */ }
    }

    // ---- iOS listing round-trip ----------------------------------------------

    [Test]
    public void WriteListing_ios_round_trips_through_reader()
    {
        var listing = new StoreListing(
            Platform.Ios, "en-US",
            Name: "My App",
            Subtitle: "Track things",
            ShortDescription: null,
            PromotionalText: "Now faster",
            Keywords: "a,b,c",
            FullDescription: "A full description.",
            ReleaseNotes: "Bug fixes",
            MarketingUrl: "https://example.com",
            SupportUrl: "https://example.com/support",
            PrivacyUrl: "https://example.com/privacy",
            VideoUrl: null,
            ScreenshotPaths: Array.Empty<string>());

        StoreMetadataWriter.WriteListing(_project, Platform.Ios, "en-US", listing);

        var read = StoreMetadataReader.ReadListing(_project, Platform.Ios, "en-US");
        Assert.Multiple(() =>
        {
            Assert.That(read.Name, Is.EqualTo("My App"));
            Assert.That(read.Subtitle, Is.EqualTo("Track things"));
            Assert.That(read.PromotionalText, Is.EqualTo("Now faster"));
            Assert.That(read.Keywords, Is.EqualTo("a,b,c"));
            Assert.That(read.FullDescription, Is.EqualTo("A full description."));
            Assert.That(read.ReleaseNotes, Is.EqualTo("Bug fixes"));
            Assert.That(read.MarketingUrl, Is.EqualTo("https://example.com"));
            Assert.That(read.SupportUrl, Is.EqualTo("https://example.com/support"));
            Assert.That(read.PrivacyUrl, Is.EqualTo("https://example.com/privacy"));
        });
    }

    [Test]
    public void WriteListing_android_round_trips_through_reader()
    {
        var listing = new StoreListing(
            Platform.Android, "en-US",
            Name: "Play Title",
            Subtitle: null,
            ShortDescription: "Short one",
            PromotionalText: null,
            Keywords: null,
            FullDescription: "Full one",
            ReleaseNotes: null,
            MarketingUrl: null,
            SupportUrl: null,
            PrivacyUrl: null,
            VideoUrl: "https://youtu.be/x",
            ScreenshotPaths: Array.Empty<string>());

        StoreMetadataWriter.WriteListing(_project, Platform.Android, "en-US", listing);

        var read = StoreMetadataReader.ReadListing(_project, Platform.Android, "en-US");
        Assert.Multiple(() =>
        {
            Assert.That(read.Name, Is.EqualTo("Play Title"));
            Assert.That(read.ShortDescription, Is.EqualTo("Short one"));
            Assert.That(read.FullDescription, Is.EqualTo("Full one"));
            Assert.That(read.VideoUrl, Is.EqualTo("https://youtu.be/x"));
        });
    }

    [Test]
    public void WriteListing_creates_new_locale_dir()
    {
        var dir = Path.Combine(_root, "ios", "fastlane", "metadata", "fr-FR");
        Assert.That(Directory.Exists(dir), Is.False);

        var listing = StoreListing.Empty(Platform.Ios, "fr-FR") with { Name = "Bonjour" };
        StoreMetadataWriter.WriteListing(_project, Platform.Ios, "fr-FR", listing);

        Assert.That(Directory.Exists(dir), Is.True);
        Assert.That(File.ReadAllText(Path.Combine(dir, "name.txt")), Is.EqualTo("Bonjour"));
    }

    [Test]
    public void WriteListing_overwrites_existing_file()
    {
        var dir = Path.Combine(_root, "ios", "fastlane", "metadata", "en-US");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "name.txt"), "Old name");

        var listing = StoreListing.Empty(Platform.Ios, "en-US") with { Name = "New name" };
        StoreMetadataWriter.WriteListing(_project, Platform.Ios, "en-US", listing);

        Assert.That(File.ReadAllText(Path.Combine(dir, "name.txt")), Is.EqualTo("New name"));
    }

    [Test]
    public void WriteListing_null_field_leaves_file_untouched()
    {
        var dir = Path.Combine(_root, "ios", "fastlane", "metadata", "en-US");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "subtitle.txt"), "Keep me");

        // Name set, Subtitle null → subtitle.txt must not change.
        var listing = StoreListing.Empty(Platform.Ios, "en-US") with { Name = "Title" };
        StoreMetadataWriter.WriteListing(_project, Platform.Ios, "en-US", listing);

        Assert.That(File.ReadAllText(Path.Combine(dir, "subtitle.txt")), Is.EqualTo("Keep me"));
    }

    [Test]
    public void WriteListing_throws_when_no_fastlane_dir()
    {
        var project = _project with { IosFastlaneDir = null };
        var listing = StoreListing.Empty(Platform.Ios, "en-US") with { Name = "x" };

        Assert.Throws<InvalidOperationException>(
            () => StoreMetadataWriter.WriteListing(project, Platform.Ios, "en-US", listing));
    }

    // ---- release notes -------------------------------------------------------

    [Test]
    public void WriteReleaseNotes_ios_writes_release_notes_file()
    {
        StoreMetadataWriter.WriteReleaseNotes(
            _project, Platform.Ios, "en-US", androidVersionCode: null, "Fresh iOS notes");

        var path = Path.Combine(_root, "ios", "fastlane", "metadata", "en-US", "release_notes.txt");
        Assert.That(File.ReadAllText(path), Is.EqualTo("Fresh iOS notes"));

        var read = StoreMetadataReader.ReadListing(_project, Platform.Ios, "en-US");
        Assert.That(read.ReleaseNotes, Is.EqualTo("Fresh iOS notes"));
    }

    [Test]
    public void WriteReleaseNotes_android_writes_changelog_for_version_code()
    {
        StoreMetadataWriter.WriteReleaseNotes(
            _project, Platform.Android, "en-US", androidVersionCode: "42", "Android build 42 notes");

        var path = Path.Combine(
            _root, "android", "fastlane", "metadata", "android", "en-US", "changelogs", "42.txt");
        Assert.That(File.Exists(path), Is.True);
        Assert.That(File.ReadAllText(path), Is.EqualTo("Android build 42 notes"));

        // Reader surfaces the latest changelog as ReleaseNotes.
        var read = StoreMetadataReader.ReadListing(_project, Platform.Android, "en-US");
        Assert.That(read.ReleaseNotes, Is.EqualTo("Android build 42 notes"));
        Assert.That(StoreMetadataReader.ChangelogVersionCodes(_project, "en-US"), Does.Contain("42"));
    }

    [Test]
    public void WriteReleaseNotes_android_creates_changelogs_dir()
    {
        var changelogs = Path.Combine(
            _root, "android", "fastlane", "metadata", "android", "fr-FR", "changelogs");
        Assert.That(Directory.Exists(changelogs), Is.False);

        StoreMetadataWriter.WriteReleaseNotes(
            _project, Platform.Android, "fr-FR", androidVersionCode: "7", "notes");

        Assert.That(Directory.Exists(changelogs), Is.True);
    }

    [Test]
    public void WriteReleaseNotes_android_requires_version_code()
    {
        Assert.Throws<ArgumentException>(() => StoreMetadataWriter.WriteReleaseNotes(
            _project, Platform.Android, "en-US", androidVersionCode: null, "notes"));
    }
}
