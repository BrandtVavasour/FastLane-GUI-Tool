using LaunchFast.Core.Models;
using LaunchFast.Core.Scanning;

namespace LaunchFast.App.Tests;

public static class TestProjects
{
    /// <summary>
    /// Creates a temp Flutter project whose ios/android fastlane dirs contain
    /// COPIES of the vendored Core.Tests Fastfile fixtures, plus Appfile/Matchfile
    /// referencing ENV[...] vars (so MissingSecrets is non-empty), a Gemfile in
    /// each platform dir, and a root .env.production with API_URL/API_TOKEN.
    /// </summary>
    public static Project MakeFlutterProjectWithRealFastfiles(string name = "demo")
    {
        var root = Path.Combine(Path.GetTempPath(), "lf-detail-" + Guid.NewGuid().ToString("N"), name);
        var iosFl = Path.Combine(root, "ios", "fastlane");
        var androidFl = Path.Combine(root, "android", "fastlane");
        Directory.CreateDirectory(iosFl);
        Directory.CreateDirectory(androidFl);

        File.WriteAllText(Path.Combine(root, "pubspec.yaml"), "name: demo\nversion: 1.2.3+9\n");

        File.WriteAllText(Path.Combine(iosFl, "Fastfile"), ReadFixture("ios.Fastfile"));
        File.WriteAllText(Path.Combine(androidFl, "Fastfile"), ReadFixture("android.Fastfile"));

        // Appfile/Matchfile referencing ENV[...] so the required set is non-empty
        // even beyond what the Fastfiles reference.
        File.WriteAllText(Path.Combine(iosFl, "Appfile"),
            "apple_id(ENV[\"APPLE_ID\"])\nteam_id(ENV[\"FASTLANE_TEAM_ID\"])\n");
        File.WriteAllText(Path.Combine(iosFl, "Matchfile"),
            "type(\"appstore\")\ngit_url(ENV[\"MATCH_GIT_URL\"])\n");

        File.WriteAllText(Path.Combine(root, "ios", "Gemfile"), "source 'https://rubygems.org'\ngem 'fastlane'\n");
        File.WriteAllText(Path.Combine(root, "android", "Gemfile"), "source 'https://rubygems.org'\ngem 'fastlane'\n");

        File.WriteAllText(Path.Combine(root, ".env.production"), "API_URL=https://api.example.com\nAPI_TOKEN=tok123\n");

        return ProjectScanner.TryScanRoot(root)!;
    }

    /// <summary>
    /// Creates a temp Flutter project with a real fastlane store-metadata tree:
    /// iOS deliver metadata + screenshots for en-US/ja, and Android supply metadata
    /// + a phone screenshot for en-US. Returns the scanned Project. Used by the
    /// Store Listing tests so the reader surfaces real on-disk content.
    /// </summary>
    public static Project MakeProjectWithStoreMetadata(string name = "store")
    {
        var root = Path.Combine(Path.GetTempPath(), "lf-store-" + Guid.NewGuid().ToString("N"), name);
        var iosFl = Path.Combine(root, "ios", "fastlane");
        var androidFl = Path.Combine(root, "android", "fastlane");
        Directory.CreateDirectory(iosFl);
        Directory.CreateDirectory(androidFl);
        File.WriteAllText(Path.Combine(root, "pubspec.yaml"), "name: demo\nversion: 1.2.3+9\n");

        // ---- iOS deliver metadata (en-US, ja) ----
        var iosEn = Path.Combine(iosFl, "metadata", "en-US");
        Directory.CreateDirectory(iosEn);
        File.WriteAllText(Path.Combine(iosEn, "name.txt"), "Demo App\n");
        File.WriteAllText(Path.Combine(iosEn, "subtitle.txt"), "Track everything\n");
        File.WriteAllText(Path.Combine(iosEn, "promotional_text.txt"), "Now faster than ever.\n");
        File.WriteAllText(Path.Combine(iosEn, "keywords.txt"), "demo,track,fast\n");
        File.WriteAllText(Path.Combine(iosEn, "description.txt"), "A long full description of the demo app.\n");
        File.WriteAllText(Path.Combine(iosEn, "marketing_url.txt"), "https://example.com\n");
        File.WriteAllText(Path.Combine(iosEn, "release_notes.txt"),
            "• Faster sync.\n• New offline mode.\n• Bug fixes.\n");

        Directory.CreateDirectory(Path.Combine(iosFl, "metadata", "ja"));
        File.WriteAllText(Path.Combine(iosFl, "metadata", "ja", "name.txt"), "デモアプリ\n");
        // ja has a name but NO release notes → an "empty" locale tab.

        var iosShots = Path.Combine(iosFl, "screenshots", "en-US");
        Directory.CreateDirectory(iosShots);
        WriteFakePng(Path.Combine(iosShots, "0_iphone.png"));
        WriteFakePng(Path.Combine(iosShots, "1_iphone.png"));

        // ---- Android supply metadata (en-US) ----
        var aEn = Path.Combine(androidFl, "metadata", "android", "en-US");
        Directory.CreateDirectory(aEn);
        File.WriteAllText(Path.Combine(aEn, "title.txt"), "Demo Play\n");
        File.WriteAllText(Path.Combine(aEn, "short_description.txt"), "Short blurb\n");
        File.WriteAllText(Path.Combine(aEn, "full_description.txt"), "Full Android description.\n");
        var aChangelogs = Path.Combine(aEn, "changelogs");
        Directory.CreateDirectory(aChangelogs);
        File.WriteAllText(Path.Combine(aChangelogs, "9.txt"), "• Android changelog for build 9.\n");
        File.WriteAllText(Path.Combine(aChangelogs, "8.txt"), "• Older Android changelog.\n");
        WriteFakePng(Path.Combine(aEn, "images", "phoneScreenshots", "1.png"));

        return ProjectScanner.TryScanRoot(root)!;
    }

    static void WriteFakePng(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, new byte[] { 0x89, 0x50, 0x4E, 0x47 });
    }

    static string ReadFixture(string fileName)
    {
        // Walk up from the test assembly to the repo, then into Core.Tests/fixtures.
        var dir = AppContext.BaseDirectory;
        while (dir is not null)
        {
            var candidate = Path.Combine(dir, "src", "LaunchFast.Core.Tests", "fixtures", fileName);
            if (File.Exists(candidate)) return File.ReadAllText(candidate);
            dir = Directory.GetParent(dir)?.FullName;
        }
        throw new FileNotFoundException($"Could not locate fixture {fileName}");
    }

    /// <summary>
    /// Creates a temp Flutter project directory (pubspec.yaml with version,
    /// ios/fastlane + android/fastlane dirs, and an iOS Matchfile) and returns the root path.
    /// </summary>
    public static string MakeFlutterProject(string name = "demo")
    {
        var root = Path.Combine(Path.GetTempPath(), "lf-app-" + Guid.NewGuid().ToString("N"), name);
        Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root, "pubspec.yaml"), "name: demo\nversion: 1.2.3+9\n");
        Directory.CreateDirectory(Path.Combine(root, "ios", "fastlane"));
        Directory.CreateDirectory(Path.Combine(root, "android", "fastlane"));
        File.WriteAllText(Path.Combine(root, "ios", "fastlane", "Matchfile"), "type(\"appstore\")");
        return root;
    }
}
