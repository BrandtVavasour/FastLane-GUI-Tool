using System.Text;
using LaunchFast.Core.Models;

namespace LaunchFast.Core.Scaffolding;

/// <summary>
/// Renders the full fastlane file set from <see cref="WizardAnswers"/> as a
/// <see cref="ScaffoldPlan"/> of <see cref="FileChangeKind.Create"/> changes,
/// modelled on the proven VendingMachine fastlane. The generated iOS Fastfile
/// parses back to its lanes via <c>FastfileParser</c> (round-trip).
/// </summary>
public static class FastlaneScaffolder
{
    public static ScaffoldPlan Render(WizardAnswers a, string root)
    {
        var files = new List<FileChange>();

        if (a.Ios)
        {
            files.Add(Create(Combine(root, "ios/fastlane/Fastfile"), IosFastfile(a)));
            files.Add(Create(Combine(root, "ios/fastlane/Appfile"), IosAppfile(a)));
            if (a.IosLanes.Contains("sync_certificates"))
                files.Add(Create(Combine(root, "ios/fastlane/Matchfile"), Matchfile(a)));
            files.Add(Create(Combine(root, "ios/Gemfile"), Gemfile()));
        }

        if (a.Android)
        {
            files.Add(Create(Combine(root, "android/fastlane/Fastfile"), AndroidFastfile(a)));
            files.Add(Create(Combine(root, "android/fastlane/Appfile"), AndroidAppfile(a)));
            files.Add(Create(Combine(root, "android/Gemfile"), Gemfile()));
        }

        files.Add(Create(Combine(root, ".env.example"), EnvExample(a)));

        var secrets = a.Secrets.Select(s => new SecretToStore(s.Key, s.Value)).ToList();
        return new ScaffoldPlan(files, secrets);
    }

    static string Combine(string root, string relative) =>
        root.TrimEnd('/') + "/" + relative;

    static FileChange Create(string path, string content) =>
        new(path, OldContent: "", NewContent: content, FileChangeKind.Create);

    // ---- iOS -----------------------------------------------------------------

    static string IosFastfile(WizardAnswers a)
    {
        var sb = new StringBuilder();
        sb.Append("require 'dotenv'\n\n");
        sb.Append("default_platform(:ios)\n\n");
        sb.Append("# Flutter project root: ios/fastlane/ -> up two levels.\n");
        sb.Append("def flutter_root\n");
        sb.Append("  File.expand_path('../..', __dir__)\n");
        sb.Append("end\n\n");
        sb.Append("platform :ios do\n");
        sb.Append("  before_all do\n");
        sb.Append("    setup_ci if ENV['CI']\n");
        sb.Append("    env_file = ENV['FASTLANE_ENV'] || '.env.production'\n");
        sb.Append("    env_path = File.join(flutter_root, env_file)\n");
        sb.Append("    Dotenv.load(env_path) if File.exist?(env_path)\n");
        sb.Append("  end\n\n");
        foreach (var lane in a.IosLanes)
            sb.Append(LaneTemplate.Render(Platform.Ios, lane, a)).Append("\n\n");
        sb.Append("end\n");
        return sb.ToString();
    }

    static string IosAppfile(WizardAnswers a) =>
        $"app_identifier(\"{a.IosBundleId}\")\n" +
        "apple_id(ENV[\"APPLE_ID\"])\n" +
        "itc_team_id(ENV[\"ITC_TEAM_ID\"])\n" +
        $"team_id(\"{a.TeamId}\")\n";

    static string Matchfile(WizardAnswers a) =>
        "git_url(ENV[\"MATCH_GIT_URL\"])\n" +
        "storage_mode(\"git\")\n" +
        "type(\"appstore\")\n" +
        $"app_identifier([\"{a.IosBundleId}\"])\n" +
        "username(ENV[\"APPLE_ID\"])\n" +
        $"team_id(\"{a.TeamId}\")\n" +
        "readonly(true)\n";

    // ---- Android -------------------------------------------------------------

    static string AndroidFastfile(WizardAnswers a)
    {
        var sb = new StringBuilder();
        sb.Append("require 'dotenv'\n\n");
        sb.Append("default_platform(:android)\n\n");
        sb.Append("# Flutter project root: android/fastlane/ -> up two levels.\n");
        sb.Append("def flutter_root\n");
        sb.Append("  File.expand_path('../..', __dir__)\n");
        sb.Append("end\n\n");
        sb.Append("platform :android do\n");
        sb.Append("  before_all do\n");
        sb.Append("    env_file = ENV['FASTLANE_ENV'] || '.env.production'\n");
        sb.Append("    env_path = File.join(flutter_root, env_file)\n");
        sb.Append("    Dotenv.load(env_path) if File.exist?(env_path)\n");
        sb.Append("  end\n\n");
        foreach (var lane in a.AndroidLanes)
            sb.Append(LaneTemplate.Render(Platform.Android, lane, a)).Append("\n\n");
        sb.Append("end\n");
        return sb.ToString();
    }

    static string AndroidAppfile(WizardAnswers a) =>
        "json_key_file(ENV[\"SUPPLY_JSON_KEY\"])\n" +
        $"package_name(\"{a.AndroidPackage}\")\n";

    // ---- Shared --------------------------------------------------------------

    static string Gemfile() =>
        "source \"https://rubygems.org\"\n\n" +
        "gem \"fastlane\"\n" +
        "gem \"dotenv\"\n";

    static string EnvExample(WizardAnswers a)
    {
        var keys = new List<string>();
        foreach (var v in a.DartDefines.Values) keys.Add(v);
        if (a.Ios)
            keys.AddRange(["MATCH_GIT_URL", "MATCH_PASSWORD", "APPLE_ID", "ITC_TEAM_ID", "APP_STORE_CONNECT_API_KEY_PATH"]);
        if (a.Android)
            keys.Add("SUPPLY_JSON_KEY");

        // Placeholder names only — never a real secret value.
        return string.Concat(keys.Distinct().Select(k => $"{k}=\n"));
    }
}
