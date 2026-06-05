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
