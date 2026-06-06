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

    /// <summary>
    /// Creates a temp Flutter project with a real Android module on disk: an
    /// <c>android/app/build.gradle</c> declaring a release signingConfig (applied to
    /// the release buildType), a <c>key.properties</c>, and an Android Appfile with a
    /// package_name. Used by the Android Signing tests so the gradle reader surfaces
    /// real values. Adds an android `build` lane Fastfile when
    /// <paramref name="withBuildLane"/> is true.
    /// </summary>
    public static Project MakeProjectWithAndroidSigning(
        string name = "android", bool withBuildLane = true)
    {
        var root = Path.Combine(Path.GetTempPath(), "lf-androidsign-" + Guid.NewGuid().ToString("N"), name);
        var androidFl = Path.Combine(root, "android", "fastlane");
        var androidApp = Path.Combine(root, "android", "app");
        Directory.CreateDirectory(androidFl);
        Directory.CreateDirectory(androidApp);
        File.WriteAllText(Path.Combine(root, "pubspec.yaml"), "name: demo\nversion: 1.2.3+9\n");

        File.WriteAllText(Path.Combine(androidApp, "build.gradle"),
            """
            android {
                signingConfigs {
                    release {
                        storeFile file(keystoreProperties['storeFile'])
                        keyAlias keystoreProperties['keyAlias']
                        storeType "PKCS12"
                    }
                }
                buildTypes {
                    release {
                        signingConfig signingConfigs.release
                    }
                }
            }
            """);

        File.WriteAllText(Path.Combine(root, "android", "key.properties"),
            "storeFile=upload-keystore.jks\nstorePassword=x\nkeyAlias=upload\nkeyPassword=y\n");

        File.WriteAllText(Path.Combine(androidFl, "Appfile"),
            "package_name(\"com.jabtech.vmt\")\njson_key_file(ENV[\"PLAY_JSON_KEY\"])\n");

        var fastfile = withBuildLane
            ? "platform :android do\n  lane :build do\n    gradle(task: \"bundleRelease\")\n  end\nend\n"
            : "platform :android do\n  lane :beta do\n  end\nend\n";
        File.WriteAllText(Path.Combine(androidFl, "Fastfile"), fastfile);

        return ProjectScanner.TryScanRoot(root)!;
    }

    /// <summary>
    /// Creates a temp Flutter project with a real iOS fastlane snapshot setup: a
    /// <c>Snapfile</c> declaring devices/languages/scheme/launch_arguments, a
    /// <c>Framefile.json</c> (frameit enabled), and captured screenshots on disk for
    /// en-US and ja. Used by the Screenshots tests so the reader surfaces real config
    /// + captured shots.
    /// </summary>
    public static Project MakeProjectWithSnapshotConfig(string name = "snap")
    {
        var root = Path.Combine(Path.GetTempPath(), "lf-snap-" + Guid.NewGuid().ToString("N"), name);
        var iosFl = Path.Combine(root, "ios", "fastlane");
        Directory.CreateDirectory(iosFl);
        Directory.CreateDirectory(Path.Combine(root, "android", "fastlane"));
        File.WriteAllText(Path.Combine(root, "pubspec.yaml"), "name: demo\nversion: 1.2.3+9\n");

        File.WriteAllText(Path.Combine(iosFl, "Snapfile"),
            """
            devices([
              "iPhone 15 Pro Max",
              "iPad Pro (12.9-inch) (6th generation)"
            ])

            languages([
              "en-US",
              "ja"
            ])

            scheme("DemoAppUITests")

            launch_arguments([
              "-FASTLANE_SNAPSHOT YES",
              "-ui_testing"
            ])
            """);

        File.WriteAllText(Path.Combine(iosFl, "Framefile.json"),
            """
            {
              "default": {
                "title": "Track every machine",
                "background": "./background.jpg"
              }
            }
            """);

        var shots = Path.Combine(iosFl, "screenshots");
        WriteFakePng(Path.Combine(shots, "en-US", "0_iphone.png"));
        WriteFakePng(Path.Combine(shots, "en-US", "1_iphone.png"));
        WriteFakePng(Path.Combine(shots, "ja", "0_iphone.png"));

        return ProjectScanner.TryScanRoot(root)!;
    }

    /// <summary>
    /// Creates a temp Flutter project with a real iOS fastlane gym/scan setup: a
    /// <c>Gymfile</c> (scheme/configuration/export_method/clean/output), a
    /// <c>Scanfile</c> (scheme/test_plan/devices) and a JUnit
    /// <c>test_output/report.junit</c> with two suites (one all-pass, one with a
    /// failure + a skip). Used by the Build &amp; Test tests so the reader surfaces
    /// real config + parsed results.
    /// </summary>
    public static Project MakeProjectWithBuildTestConfig(string name = "buildtest")
    {
        var root = Path.Combine(Path.GetTempPath(), "lf-buildtest-" + Guid.NewGuid().ToString("N"), name);
        var iosFl = Path.Combine(root, "ios", "fastlane");
        Directory.CreateDirectory(iosFl);
        Directory.CreateDirectory(Path.Combine(root, "android", "fastlane"));
        File.WriteAllText(Path.Combine(root, "pubspec.yaml"), "name: demo\nversion: 1.2.3+9\n");

        File.WriteAllText(Path.Combine(iosFl, "Gymfile"),
            """
            scheme("Runner")
            configuration("Release")
            export_method("app-store")
            clean(true)
            include_bitcode(false)
            output_directory("./build")
            output_name("VendingTracker.ipa")
            """);

        File.WriteAllText(Path.Combine(iosFl, "Scanfile"),
            """
            scheme("RunnerTests")
            test_plan("FullSuite")
            devices([
              "iPhone 15 Pro",
              "iPhone SE (3rd generation)"
            ])
            """);

        var testOut = Path.Combine(iosFl, "test_output");
        Directory.CreateDirectory(testOut);
        File.WriteAllText(Path.Combine(testOut, "report.junit"),
            """
            <?xml version="1.0" encoding="UTF-8"?>
            <testsuites name="Tests" tests="6" failures="1" skipped="1" time="72.0">
              <testsuite name="UnitTests" tests="3" failures="0" skipped="0" time="20.0">
                <testcase classname="UnitTests" name="a" time="6.0"/>
                <testcase classname="UnitTests" name="b" time="7.0"/>
                <testcase classname="UnitTests" name="c" time="7.0"/>
              </testsuite>
              <testsuite name="UITests" tests="3" failures="1" skipped="1" time="52.0">
                <testcase classname="UITests" name="d" time="20.0"/>
                <testcase classname="UITests" name="e" time="32.0">
                  <failure message="boom">stack</failure>
                </testcase>
                <testcase classname="UITests" name="f" time="0.0">
                  <skipped/>
                </testcase>
              </testsuite>
            </testsuites>
            """);

        return ProjectScanner.TryScanRoot(root)!;
    }

    /// <summary>
    /// Creates a temp Flutter project with a real iOS fastlane Matchfile (git-backed,
    /// appstore) and an iOS Appfile declaring a literal bundle id, plus a sibling temp
    /// "Provisioning Profiles" dir holding one fixture <c>.mobileprovision</c> for that
    /// bundle id. Returns the scanned Project and the profiles-dir path so the Signing
    /// tests/snapshot can surface real match config + a real parsed profile without
    /// touching the user's ~/Library. The profile expires in the far future so it is
    /// "Valid". When <paramref name="expiringSoon"/> is true a second profile expiring
    /// in 11 days is added (a "warn" row).
    /// </summary>
    public static (Project Project, string ProfilesDir) MakeProjectWithIosSigning(
        string name = "iossign", bool expiringSoon = false)
    {
        var baseDir = Path.Combine(Path.GetTempPath(), "lf-iossign-" + Guid.NewGuid().ToString("N"));
        var root = Path.Combine(baseDir, name);
        var iosFl = Path.Combine(root, "ios", "fastlane");
        Directory.CreateDirectory(iosFl);
        Directory.CreateDirectory(Path.Combine(root, "android", "fastlane"));
        File.WriteAllText(Path.Combine(root, "pubspec.yaml"), "name: demo\nversion: 1.2.3+9\n");

        File.WriteAllText(Path.Combine(iosFl, "Appfile"),
            "app_identifier(\"com.jabtech.vmt\")\napple_id(ENV[\"APPLE_ID\"])\n");

        File.WriteAllText(Path.Combine(iosFl, "Matchfile"),
            """
            git_url("git@github.com:jabtech/certificates.git")
            storage_mode("git")
            type("appstore")
            app_identifier("com.jabtech.vmt")
            git_branch("main")
            readonly(true)
            """);

        var profilesDir = Path.Combine(baseDir, "Provisioning Profiles");
        Directory.CreateDirectory(profilesDir);
        File.WriteAllText(Path.Combine(profilesDir, "appstore.mobileprovision"),
            MakeMobileProvision("match AppStore com.jabtech.vmt", "com.jabtech.vmt",
                expiration: "2099-01-01T00:00:00Z", devices: 0));
        if (expiringSoon)
        {
            var soon = DateTimeOffset.UtcNow.AddDays(11).ToString("yyyy-MM-ddTHH:mm:ssZ");
            File.WriteAllText(Path.Combine(profilesDir, "adhoc.mobileprovision"),
                MakeMobileProvision("match AdHoc com.jabtech.vmt", "com.jabtech.vmt",
                    expiration: soon, devices: 24));
        }

        return (ProjectScanner.TryScanRoot(root)!, profilesDir);
    }

    /// <summary>A .mobileprovision-shaped fixture: CMS preamble + embedded XML plist.</summary>
    static string MakeMobileProvision(
        string profileName, string bundleId, string expiration, int devices)
    {
        var deviceXml = devices > 0
            ? "<key>ProvisionedDevices</key><array>" +
              string.Concat(Enumerable.Range(0, devices).Select(i => $"<string>UDID{i:D4}</string>")) +
              "</array>"
            : "";

        var plist =
            $"""
            <?xml version="1.0" encoding="UTF-8"?>
            <plist version="1.0">
            <dict>
                <key>AppIDName</key><string>Vending Tracker</string>
                <key>Name</key><string>{profileName}</string>
                <key>TeamName</key><string>JAB Technologies</string>
                <key>Entitlements</key>
                <dict>
                    <key>application-identifier</key><string>ABCDE12345.{bundleId}</string>
                </dict>
                <key>ExpirationDate</key><date>{expiration}</date>
                {deviceXml}
                <key>ProvisionsAllDevices</key><false/>
            </dict>
            </plist>
            """;

        return "CMS-PREAMBLE\n" + plist + "\nTRAILING-BYTES";
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
