using LaunchFast.Core.Models;
using LaunchFast.Core.Scaffolding;

namespace LaunchFast.Core.Tests;

public class LaneTemplateTests
{
    static WizardAnswers Answers() => new(
        Ios: true, Android: true,
        IosBundleId: "com.acme.demo", AppleId: null, TeamId: "ABCDE12345", ItcTeamId: null,
        MatchGitUrl: null, AndroidPackage: "com.acme.demo", PlayJsonKeyPath: null,
        IosLanes: ["sync_certificates", "beta", "release", "screenshots"],
        AndroidLanes: ["build", "internal", "beta", "production"],
        DartDefines: new Dictionary<string, string> { ["API_URL"] = "API_URL", ["API_TOKEN"] = "API_TOKEN" },
        Secrets: []);

    [Test]
    public Task Renders_ios_beta() => Verify(LaneTemplate.Render(Platform.Ios, "beta", Answers()));

    [Test]
    public Task Renders_android_build() => Verify(LaneTemplate.Render(Platform.Android, "build", Answers()));

    [Test]
    public Task Renders_android_production() => Verify(LaneTemplate.Render(Platform.Android, "production", Answers()));

    [Test]
    public void Lists_available_lanes_per_platform()
    {
        Assert.That(LaneTemplate.Available(Platform.Ios),
            Is.EquivalentTo(new[] { "sync_certificates", "beta", "release", "screenshots" }));
        Assert.That(LaneTemplate.Available(Platform.Android),
            Is.EquivalentTo(new[] { "build", "internal", "beta", "production" }));
    }
}
