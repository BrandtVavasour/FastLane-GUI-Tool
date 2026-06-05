using LaunchFast.Core.Models;
using LaunchFast.Core.Parsing;

namespace LaunchFast.Core.Tests;

public class FastfileParserTests
{
    [Test]
    public Task Parses_ios_public_lanes_with_descriptions()
    {
        var text = File.ReadAllText("fixtures/ios.Fastfile");
        var lanes = FastfileParser.Parse(text, Platform.Ios);
        return Verify(lanes);
    }

    [Test]
    public Task Parses_android_public_lanes()
    {
        var text = File.ReadAllText("fixtures/android.Fastfile");
        var lanes = FastfileParser.Parse(text, Platform.Android);
        return Verify(lanes);
    }

    [Test]
    public void Skips_private_lanes()
    {
        var text = File.ReadAllText("fixtures/ios.Fastfile");
        var lanes = FastfileParser.Parse(text, Platform.Ios);
        Assert.That(lanes.Select(l => l.Name),
            Does.Not.Contain("capture_screenshots_for_device"));
        Assert.That(lanes.Select(l => l.Name), Does.Contain("beta"));
    }
}
