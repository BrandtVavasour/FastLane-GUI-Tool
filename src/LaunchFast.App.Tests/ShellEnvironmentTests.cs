using LaunchFast.App.Services;

namespace LaunchFast.App.Tests;

public class ShellEnvironmentTests
{
    [Test]
    public void ExtractPath_returns_path_when_marker_present()
    {
        var output = "__LF_PATH__:/opt/homebrew/opt/ruby/bin:/usr/bin:/bin";
        Assert.That(ShellEnvironment.ExtractPath(output),
            Is.EqualTo("/opt/homebrew/opt/ruby/bin:/usr/bin:/bin"));
    }

    [Test]
    public void ExtractPath_returns_null_when_marker_absent()
    {
        Assert.That(ShellEnvironment.ExtractPath("some shell noise\nno marker here"), Is.Null);
    }

    [Test]
    public void ExtractPath_finds_marker_among_surrounding_lines()
    {
        var output = "login banner\nwarning: something\n__LF_PATH__:/usr/local/bin:/usr/bin\nbye";
        Assert.That(ShellEnvironment.ExtractPath(output), Is.EqualTo("/usr/local/bin:/usr/bin"));
    }
}
