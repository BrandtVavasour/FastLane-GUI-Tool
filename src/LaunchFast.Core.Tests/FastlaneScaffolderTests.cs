using LaunchFast.Core.Models;
using LaunchFast.Core.Parsing;
using LaunchFast.Core.Scaffolding;

namespace LaunchFast.Core.Tests;

public class FastlaneScaffolderTests
{
    static WizardAnswers Answers() => new(
        Ios: true, Android: true,
        IosBundleId: "com.acme.demo", AppleId: null, TeamId: "ABCDE12345", ItcTeamId: null,
        MatchGitUrl: null, AndroidPackage: "com.acme.demo", PlayJsonKeyPath: null,
        IosLanes: ["sync_certificates", "beta", "release", "screenshots"],
        AndroidLanes: ["build", "internal", "beta", "production"],
        DartDefines: new Dictionary<string, string> { ["API_URL"] = "API_URL", ["API_TOKEN"] = "API_TOKEN" },
        Secrets: [new SecretInput("MATCH_PASSWORD", "supersecret")]);

    [Test]
    public Task Generates_ios_only_file_set()
    {
        var a = Answers() with { Android = false, AndroidLanes = [] };
        var plan = FastlaneScaffolder.Render(a, "/proj");
        return Verify(plan.Files.Select(f => new { f.Path, f.Kind, f.NewContent }));
    }

    [Test]
    public void Env_example_has_placeholders_not_secret_values()
    {
        var plan = FastlaneScaffolder.Render(Answers(), "/proj");
        var env = plan.Files.Single(f => f.Path.EndsWith(".env.example")).NewContent;
        Assert.That(env, Does.Contain("MATCH_PASSWORD="));
        Assert.That(env, Does.Not.Contain("supersecret"));
    }

    [Test]
    public void Generated_ios_fastfile_parses_back_to_its_lanes()
    {
        var a = Answers() with { Android = false, AndroidLanes = [] };
        var plan = FastlaneScaffolder.Render(a, "/proj");
        var ff = plan.Files.Single(f => f.Path.EndsWith("ios/fastlane/Fastfile")).NewContent;
        var lanes = FastfileParser.Parse(ff, Platform.Ios).Select(l => l.Name);
        Assert.That(lanes, Is.SupersetOf(new[] { "sync_certificates", "beta", "release", "screenshots" }));
    }
}
