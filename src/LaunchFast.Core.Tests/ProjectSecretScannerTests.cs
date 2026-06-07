using LaunchFast.Core.Env;
using LaunchFast.Core.Models;

namespace LaunchFast.Core.Tests;

public class ProjectSecretScannerTests
{
    static Project MakeProject(out string root)
    {
        root = Path.Combine(Path.GetTempPath(), "lf-scan-" + Guid.NewGuid().ToString("N"));
        var iosFl = Path.Combine(root, "ios", "fastlane");
        var androidFl = Path.Combine(root, "android", "fastlane");
        Directory.CreateDirectory(iosFl);
        Directory.CreateDirectory(androidFl);

        File.WriteAllText(Path.Combine(iosFl, "Fastfile"), ReadFixture("ios.Fastfile"));
        File.WriteAllText(Path.Combine(androidFl, "Fastfile"), ReadFixture("android.Fastfile"));
        File.WriteAllText(Path.Combine(iosFl, "Appfile"),
            "apple_id(ENV[\"APPLE_ID\"])\nteam_id(ENV[\"FASTLANE_TEAM_ID\"])\n");
        File.WriteAllText(Path.Combine(iosFl, "Matchfile"),
            "type(\"appstore\")\ngit_url(ENV[\"MATCH_GIT_URL\"])\n");

        File.WriteAllText(Path.Combine(root, ".env.production"),
            "API_URL=https://api.example.com\nAPI_TOKEN=tok123\n");

        return new Project("demo", root, "1.0.0", iosFl, androidFl, true, null);
    }

    static string ReadFixture(string fileName)
    {
        var dir = AppContext.BaseDirectory;
        while (dir is not null)
        {
            var candidate = Path.Combine(dir, "src", "LaunchFast.Core.Tests", "fixtures", fileName);
            if (File.Exists(candidate)) return File.ReadAllText(candidate);
            dir = Directory.GetParent(dir)?.FullName;
        }
        throw new FileNotFoundException($"Could not locate fixture {fileName}");
    }

    [Test]
    public void Required_secrets_are_referenced_genuine_secrets_only()
    {
        var project = MakeProject(out _);

        var scan = ProjectSecretScanner.Scan(project);

        // Genuine secrets referenced by the fastlane config.
        Assert.That(scan.RequiredSecrets, Does.Contain("MATCH_PASSWORD").Or.Contain("APPLE_ID"));
        Assert.That(scan.RequiredSecrets, Does.Contain("APPLE_ID"));
        Assert.That(scan.RequiredSecrets, Does.Contain("MATCH_GIT_URL"));
        Assert.That(scan.RequiredSecrets, Does.Contain("API_TOKEN"));

        // Control / config vars must never be required.
        Assert.That(scan.RequiredSecrets, Does.Not.Contain("CI"));
        Assert.That(scan.RequiredSecrets, Does.Not.Contain("FASTLANE_ENV"));
        Assert.That(scan.RequiredSecrets, Does.Not.Contain("FLUTTER_LOCALE"));
        Assert.That(scan.RequiredSecrets, Does.Not.Contain("MATCH_KEYCHAIN_NAME"));
    }

    [Test]
    public void File_sourced_values_come_from_env_files()
    {
        var project = MakeProject(out _);

        var scan = ProjectSecretScanner.Scan(project);

        Assert.That(scan.FromFiles["API_TOKEN"], Is.EqualTo("tok123"));
        Assert.That(scan.FromFiles["API_URL"], Is.EqualTo("https://api.example.com"));
        Assert.That(scan.FromFiles.ContainsKey("MATCH_PASSWORD"), Is.False);
    }

    [Test]
    public void ReadEnvFiles_expands_dollar_and_tilde_paths_for_credential_discovery()
    {
        var root = Path.Combine(Path.GetTempPath(), "lf-env-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            File.WriteAllText(Path.Combine(root, ".env.production"),
                "APP_STORE_CONNECT_API_KEY_PATH=$HOME/.appstoreconnect/api_key.json\n" +
                "PLAY_KEY=~/keys/play.json\n" +
                "API_URL=https://api.example.com\n");

            // Injected lookup keeps the test deterministic (no real $HOME dependency).
            var env = ProjectSecretScanner.ReadEnvFiles(root,
                name => name == "HOME" ? "/Users/dev" : null);

            Assert.That(env["APP_STORE_CONNECT_API_KEY_PATH"],
                Is.EqualTo("/Users/dev/.appstoreconnect/api_key.json"));
            Assert.That(env["PLAY_KEY"], Is.EqualTo("/Users/dev/keys/play.json"));
            // Plain values are untouched.
            Assert.That(env["API_URL"], Is.EqualTo("https://api.example.com"));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
