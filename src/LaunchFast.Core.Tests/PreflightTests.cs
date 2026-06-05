using LaunchFast.Core.Running;

namespace LaunchFast.Core.Tests;

public class PreflightTests
{
    [Test]
    public void Reports_missing_gemfile()
    {
        var dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var result = Preflight.CheckGemfile(dir);
        Assert.That(result.Ok, Is.False);
        Assert.That(result.Message, Does.Contain("Gemfile"));
    }

    [Test]
    public void Passes_when_gemfile_present()
    {
        var dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "Gemfile"), "gem 'fastlane'");
        Assert.That(Preflight.CheckGemfile(dir).Ok, Is.True);
    }
}
