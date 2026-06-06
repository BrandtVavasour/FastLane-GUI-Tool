using LaunchFast.Core.Models;
using LaunchFast.Core.Stores;

namespace LaunchFast.Core.Tests;

[TestFixture]
public sealed class StoreMetadataReaderTests
{
    string _root = null!;
    Project _project = null!;

    [SetUp]
    public void SetUp()
    {
        _root = Path.Combine(Path.GetTempPath(), "lf-meta-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        _project = MakeProject(_root);
    }

    [TearDown]
    public void TearDown()
    {
        try { Directory.Delete(_root, recursive: true); }
        catch (IOException) { /* best effort */ }
    }

    static Project MakeProject(string root) => new(
        Name: "demo",
        Path: root,
        Version: "1.0.0",
        IosFastlaneDir: Path.Combine(root, "ios", "fastlane"),
        AndroidFastlaneDir: Path.Combine(root, "android", "fastlane"),
        HasMatchfile: false,
        IconPath: null);

    static void Write(string path, string content)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }

    static void WritePng(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        // 1x1 PNG header bytes are enough; the reader only enumerates by extension.
        File.WriteAllBytes(path, new byte[] { 0x89, 0x50, 0x4E, 0x47 });
    }

    string IosMeta(string locale) =>
        Path.Combine(_root, "ios", "fastlane", "metadata", locale);

    string AndroidMeta(string locale) =>
        Path.Combine(_root, "android", "fastlane", "metadata", "android", locale);

    // ---- locale discovery ----------------------------------------------------

    [Test]
    public void Locales_discovers_ios_locale_folders_sorted()
    {
        Directory.CreateDirectory(IosMeta("ja"));
        Directory.CreateDirectory(IosMeta("en-US"));
        Directory.CreateDirectory(IosMeta("de-DE"));

        var locales = StoreMetadataReader.Locales(_project, Platform.Ios);

        Assert.That(locales, Is.EqualTo(new[] { "de-DE", "en-US", "ja" }));
    }

    [Test]
    public void Locales_discovers_android_locale_folders()
    {
        Directory.CreateDirectory(AndroidMeta("en-US"));
        Directory.CreateDirectory(AndroidMeta("fr-FR"));

        var locales = StoreMetadataReader.Locales(_project, Platform.Android);

        Assert.That(locales, Is.EqualTo(new[] { "en-US", "fr-FR" }));
    }

    [Test]
    public void Locales_is_empty_when_no_metadata()
    {
        Assert.That(StoreMetadataReader.Locales(_project, Platform.Ios), Is.Empty);
        Assert.That(StoreMetadataReader.Locales(_project, Platform.Android), Is.Empty);
    }

    [Test]
    public void Locales_is_empty_when_platform_has_no_fastlane_dir()
    {
        var project = _project with { IosFastlaneDir = null };
        Assert.That(StoreMetadataReader.Locales(project, Platform.Ios), Is.Empty);
    }

    // ---- iOS fields ----------------------------------------------------------

    [Test]
    public void ReadListing_ios_reads_all_text_fields()
    {
        var dir = IosMeta("en-US");
        Write(Path.Combine(dir, "name.txt"), "My App\n");
        Write(Path.Combine(dir, "subtitle.txt"), "Track things");
        Write(Path.Combine(dir, "promotional_text.txt"), "Now faster");
        Write(Path.Combine(dir, "keywords.txt"), "a,b,c");
        Write(Path.Combine(dir, "description.txt"), "Full description here.");
        Write(Path.Combine(dir, "release_notes.txt"), "Bug fixes");
        Write(Path.Combine(dir, "marketing_url.txt"), "https://example.com");
        Write(Path.Combine(dir, "support_url.txt"), "https://example.com/support");
        Write(Path.Combine(dir, "privacy_url.txt"), "https://example.com/privacy");

        var listing = StoreMetadataReader.ReadListing(_project, Platform.Ios, "en-US");

        Assert.Multiple(() =>
        {
            Assert.That(listing.Platform, Is.EqualTo(Platform.Ios));
            Assert.That(listing.Name, Is.EqualTo("My App")); // trimmed
            Assert.That(listing.Subtitle, Is.EqualTo("Track things"));
            Assert.That(listing.PromotionalText, Is.EqualTo("Now faster"));
            Assert.That(listing.Keywords, Is.EqualTo("a,b,c"));
            Assert.That(listing.FullDescription, Is.EqualTo("Full description here."));
            Assert.That(listing.ReleaseNotes, Is.EqualTo("Bug fixes"));
            Assert.That(listing.MarketingUrl, Is.EqualTo("https://example.com"));
            Assert.That(listing.SupportUrl, Is.EqualTo("https://example.com/support"));
            Assert.That(listing.PrivacyUrl, Is.EqualTo("https://example.com/privacy"));
            // Android-only fields stay null on iOS.
            Assert.That(listing.ShortDescription, Is.Null);
            Assert.That(listing.VideoUrl, Is.Null);
        });
    }

    [Test]
    public void ReadListing_ios_missing_files_yield_nulls()
    {
        Directory.CreateDirectory(IosMeta("en-US"));
        Write(Path.Combine(IosMeta("en-US"), "name.txt"), "Only name");

        var listing = StoreMetadataReader.ReadListing(_project, Platform.Ios, "en-US");

        Assert.That(listing.Name, Is.EqualTo("Only name"));
        Assert.That(listing.Subtitle, Is.Null);
        Assert.That(listing.Keywords, Is.Null);
        Assert.That(listing.ScreenshotPaths, Is.Empty);
    }

    [Test]
    public void ReadListing_blank_file_is_null()
    {
        Write(Path.Combine(IosMeta("en-US"), "subtitle.txt"), "   \n\t ");

        var listing = StoreMetadataReader.ReadListing(_project, Platform.Ios, "en-US");

        Assert.That(listing.Subtitle, Is.Null);
    }

    [Test]
    public void ReadListing_absent_locale_is_empty()
    {
        var listing = StoreMetadataReader.ReadListing(_project, Platform.Ios, "zz");
        Assert.That(listing.IsEmpty, Is.True);
    }

    // ---- iOS screenshots -----------------------------------------------------

    [Test]
    public void ReadListing_ios_enumerates_screenshots_from_screenshots_dir()
    {
        WritePng(Path.Combine(_root, "ios", "fastlane", "screenshots", "en-US", "0_iphone.png"));
        WritePng(Path.Combine(_root, "ios", "fastlane", "screenshots", "en-US", "1_iphone.png"));
        Directory.CreateDirectory(IosMeta("en-US"));

        var listing = StoreMetadataReader.ReadListing(_project, Platform.Ios, "en-US");

        Assert.That(listing.ScreenshotPaths, Has.Count.EqualTo(2));
        Assert.That(listing.ScreenshotPaths[0], Does.EndWith("0_iphone.png"));
    }

    [Test]
    public void ReadListing_ios_falls_back_to_pngs_beside_metadata()
    {
        WritePng(Path.Combine(IosMeta("en-US"), "shot.png"));

        var listing = StoreMetadataReader.ReadListing(_project, Platform.Ios, "en-US");

        Assert.That(listing.ScreenshotPaths, Has.Count.EqualTo(1));
        Assert.That(listing.ScreenshotPaths[0], Does.EndWith("shot.png"));
    }

    // ---- Android fields ------------------------------------------------------

    [Test]
    public void ReadListing_android_reads_fields_and_latest_changelog()
    {
        var dir = AndroidMeta("en-US");
        Write(Path.Combine(dir, "title.txt"), "Play Title");
        Write(Path.Combine(dir, "short_description.txt"), "Short one");
        Write(Path.Combine(dir, "full_description.txt"), "Full one");
        Write(Path.Combine(dir, "video.txt"), "https://youtu.be/x");
        Write(Path.Combine(dir, "changelogs", "15.txt"), "Old notes");
        Write(Path.Combine(dir, "changelogs", "18.txt"), "New notes");

        var listing = StoreMetadataReader.ReadListing(_project, Platform.Android, "en-US");

        Assert.Multiple(() =>
        {
            Assert.That(listing.Platform, Is.EqualTo(Platform.Android));
            Assert.That(listing.Name, Is.EqualTo("Play Title"));
            Assert.That(listing.ShortDescription, Is.EqualTo("Short one"));
            Assert.That(listing.FullDescription, Is.EqualTo("Full one"));
            Assert.That(listing.VideoUrl, Is.EqualTo("https://youtu.be/x"));
            Assert.That(listing.ReleaseNotes, Is.EqualTo("New notes")); // highest version code
            // iOS-only fields stay null on Android.
            Assert.That(listing.Subtitle, Is.Null);
            Assert.That(listing.Keywords, Is.Null);
            Assert.That(listing.PromotionalText, Is.Null);
        });
    }

    [Test]
    public void ChangelogVersionCodes_lists_codes_sorted_descending()
    {
        var dir = AndroidMeta("en-US");
        Write(Path.Combine(dir, "changelogs", "15.txt"), "Old notes");
        Write(Path.Combine(dir, "changelogs", "18.txt"), "New notes");
        Write(Path.Combine(dir, "changelogs", "16.txt"), "Mid notes");

        var codes = StoreMetadataReader.ChangelogVersionCodes(_project, "en-US");

        Assert.That(codes, Is.EqualTo(new[] { "18", "16", "15" }));
    }

    [Test]
    public void ChangelogVersionCodes_empty_when_no_changelogs()
    {
        Directory.CreateDirectory(AndroidMeta("en-US"));
        var codes = StoreMetadataReader.ChangelogVersionCodes(_project, "en-US");
        Assert.That(codes, Is.Empty);
    }

    [Test]
    public void ReadListing_android_enumerates_phone_and_tablet_screenshots()
    {
        var images = Path.Combine(AndroidMeta("en-US"), "images");
        WritePng(Path.Combine(images, "phoneScreenshots", "1.png"));
        WritePng(Path.Combine(images, "phoneScreenshots", "2.png"));
        WritePng(Path.Combine(images, "tenInchScreenshots", "1.png"));

        var listing = StoreMetadataReader.ReadListing(_project, Platform.Android, "en-US");

        Assert.That(listing.ScreenshotPaths, Has.Count.EqualTo(3));
    }

    // ---- limits constants ----------------------------------------------------

    [Test]
    public void Field_limits_match_store_rules()
    {
        Assert.Multiple(() =>
        {
            Assert.That(StoreFieldLimits.AppStoreName, Is.EqualTo(30));
            Assert.That(StoreFieldLimits.AppStoreSubtitle, Is.EqualTo(30));
            Assert.That(StoreFieldLimits.AppStorePromotionalText, Is.EqualTo(170));
            Assert.That(StoreFieldLimits.AppStoreKeywords, Is.EqualTo(100));
            Assert.That(StoreFieldLimits.AppStoreDescription, Is.EqualTo(4000));
            Assert.That(StoreFieldLimits.AppStoreReleaseNotes, Is.EqualTo(4000));
            Assert.That(StoreFieldLimits.PlayTitle, Is.EqualTo(30));
            Assert.That(StoreFieldLimits.PlayShortDescription, Is.EqualTo(80));
            Assert.That(StoreFieldLimits.PlayFullDescription, Is.EqualTo(4000));
            Assert.That(StoreFieldLimits.PlayWhatsNew, Is.EqualTo(500));
        });
    }
}
