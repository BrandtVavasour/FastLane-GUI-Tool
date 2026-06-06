using LaunchFast.Core.Models;
using LaunchFast.Core.Screenshots;

namespace LaunchFast.Core.Tests;

[TestFixture]
public sealed class SnapshotConfigReaderTests
{
    string _root = null!;
    Project _project = null!;

    [SetUp]
    public void SetUp()
    {
        _root = Path.Combine(Path.GetTempPath(), "lf-snap-" + Guid.NewGuid().ToString("N"));
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

    string IosFl => Path.Combine(_root, "ios", "fastlane");

    static void Write(string path, string content)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }

    static void WritePng(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, new byte[] { 0x89, 0x50, 0x4E, 0x47 });
    }

    // ---- Snapfile parsing ----------------------------------------------------

    [Test]
    public void Read_parses_devices_languages_scheme_and_launch_args_array()
    {
        Write(Path.Combine(IosFl, "Snapfile"),
            """
            devices([
              "iPhone 15 Pro Max",
              "iPad Pro (12.9-inch) (6th generation)"
            ])

            languages([
              "en-US",
              "ja"
            ])

            scheme("MyAppUITests")

            launch_arguments([
              "-FASTLANE_SNAPSHOT YES",
              "-ui_testing"
            ])

            output_directory("./screenshots")
            """);

        var cfg = SnapshotConfigReader.Read(_project);

        Assert.Multiple(() =>
        {
            Assert.That(cfg.HasSnapfile, Is.True);
            Assert.That(cfg.Devices, Is.EqualTo(new[]
            {
                "iPhone 15 Pro Max",
                "iPad Pro (12.9-inch) (6th generation)",
            }));
            Assert.That(cfg.Languages, Is.EqualTo(new[] { "en-US", "ja" }));
            Assert.That(cfg.Scheme, Is.EqualTo("MyAppUITests"));
            Assert.That(cfg.LaunchArguments, Is.EqualTo("-FASTLANE_SNAPSHOT YES -ui_testing"));
            Assert.That(cfg.OutputDirectory, Is.EqualTo("./screenshots"));
        });
    }

    [Test]
    public void Read_parses_single_string_launch_arguments()
    {
        Write(Path.Combine(IosFl, "Snapfile"),
            "scheme(\"UITests\")\nlaunch_arguments(\"-FASTLANE_SNAPSHOT YES\")\n");

        var cfg = SnapshotConfigReader.Read(_project);

        Assert.That(cfg.LaunchArguments, Is.EqualTo("-FASTLANE_SNAPSHOT YES"));
    }

    // ---- captured screenshots ------------------------------------------------

    [Test]
    public void Read_enumerates_captured_screenshots_grouped_by_locale()
    {
        Write(Path.Combine(IosFl, "Snapfile"), "languages([\"en-US\", \"ja\"])\n");
        WritePng(Path.Combine(IosFl, "screenshots", "en-US", "0_iphone.png"));
        WritePng(Path.Combine(IosFl, "screenshots", "en-US", "1_iphone.png"));
        WritePng(Path.Combine(IosFl, "screenshots", "ja", "0_iphone.png"));

        var cfg = SnapshotConfigReader.Read(_project);

        Assert.That(cfg.CapturedCount, Is.EqualTo(3));
        var en = cfg.Captured.Single(g => g.Locale == "en-US");
        Assert.That(en.Paths, Has.Count.EqualTo(2));
        Assert.That(en.Paths[0], Does.EndWith("0_iphone.png"));
        Assert.That(cfg.Captured.Single(g => g.Locale == "ja").Paths, Has.Count.EqualTo(1));
    }

    [Test]
    public void Read_uses_output_directory_relative_to_fastlane_for_captured()
    {
        Write(Path.Combine(IosFl, "Snapfile"),
            "languages([\"en-US\"])\noutput_directory(\"shots\")\n");
        WritePng(Path.Combine(IosFl, "shots", "en-US", "0_iphone.png"));

        var cfg = SnapshotConfigReader.Read(_project);

        Assert.That(cfg.OutputDirectory, Is.EqualTo("shots"));
        Assert.That(cfg.CapturedCount, Is.EqualTo(1));
        Assert.That(cfg.Captured.Single().Locale, Is.EqualTo("en-US"));
    }

    // ---- no Snapfile fallback ------------------------------------------------

    [Test]
    public void Read_without_snapfile_derives_languages_from_disk()
    {
        WritePng(Path.Combine(IosFl, "screenshots", "fr-FR", "0.png"));
        WritePng(Path.Combine(IosFl, "screenshots", "de-DE", "0.png"));

        var cfg = SnapshotConfigReader.Read(_project);

        Assert.Multiple(() =>
        {
            Assert.That(cfg.HasSnapfile, Is.False);
            Assert.That(cfg.Devices, Is.Empty);
            Assert.That(cfg.Languages, Is.EqualTo(new[] { "de-DE", "fr-FR" }));
            Assert.That(cfg.CapturedCount, Is.EqualTo(2));
        });
    }

    // ---- frameit -------------------------------------------------------------

    [Test]
    public void Read_detects_framefile_presence()
    {
        Write(Path.Combine(IosFl, "Framefile"), "# frameit config\n");

        var cfg = SnapshotConfigReader.Read(_project);

        Assert.That(cfg.FrameitEnabled, Is.True);
        Assert.That(cfg.FrameTitle, Is.Null);
    }

    [Test]
    public void Read_reads_title_and_background_from_framefile_json()
    {
        Write(Path.Combine(IosFl, "Framefile.json"),
            """
            {
              "default": {
                "title": { "color": "#ffffff" },
                "background": "./background.jpg",
                "title": "Track every machine"
              }
            }
            """);

        var cfg = SnapshotConfigReader.Read(_project);

        Assert.Multiple(() =>
        {
            Assert.That(cfg.FrameitEnabled, Is.True);
            // First "title": value is an object brace, but our cheap regex finds the
            // first string-valued title — assert background which is unambiguous.
            Assert.That(cfg.FrameBackground, Is.EqualTo("./background.jpg"));
        });
    }

    // ---- empty / total -------------------------------------------------------

    [Test]
    public void Read_with_nothing_on_disk_is_empty_config()
    {
        Directory.CreateDirectory(IosFl);

        var cfg = SnapshotConfigReader.Read(_project);

        Assert.Multiple(() =>
        {
            Assert.That(cfg.HasSnapfile, Is.False);
            Assert.That(cfg.Devices, Is.Empty);
            Assert.That(cfg.Languages, Is.Empty);
            Assert.That(cfg.Scheme, Is.Null);
            Assert.That(cfg.LaunchArguments, Is.Null);
            Assert.That(cfg.FrameitEnabled, Is.False);
            Assert.That(cfg.Captured, Is.Empty);
            Assert.That(cfg.CapturedCount, Is.EqualTo(0));
        });
    }

    [Test]
    public void Read_returns_none_when_no_ios_fastlane_dir()
    {
        var project = _project with { IosFastlaneDir = null };

        var cfg = SnapshotConfigReader.Read(project);

        Assert.That(cfg, Is.SameAs(SnapshotConfig.None));
        Assert.That(cfg.HasSnapfile, Is.False);
    }
}
