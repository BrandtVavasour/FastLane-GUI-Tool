using LaunchFast.Core.Running;

namespace LaunchFast.Core.Tests;

public class PreflightTests
{
    static string NewTempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    [Test]
    public void Reports_missing_gemfile()
    {
        var dir = NewTempDir();
        var result = Preflight.CheckGemfile(dir);
        Assert.That(result.Ok, Is.False);
        Assert.That(result.Message, Does.Contain("Gemfile"));
    }

    [Test]
    public void Passes_when_gemfile_present()
    {
        var dir = NewTempDir();
        File.WriteAllText(Path.Combine(dir, "Gemfile"), "gem 'fastlane'");
        Assert.That(Preflight.CheckGemfile(dir).Ok, Is.True);
    }

    [Test]
    public void Passes_when_gemfile_in_parent_within_stop_at()
    {
        // The bug case: workingDir = <root>/ios, Gemfile at <root>/Gemfile, stopAt = <root>.
        var root = NewTempDir();
        var ios = Path.Combine(root, "ios");
        Directory.CreateDirectory(ios);
        File.WriteAllText(Path.Combine(root, "Gemfile"), "gem 'fastlane'");

        Assert.That(Preflight.CheckGemfile(ios, root).Ok, Is.True);
    }

    [Test]
    public void Fails_when_gemfile_only_above_stop_at()
    {
        // Gemfile lives above the project root — the walk must not escape stopAt.
        var grandparent = NewTempDir();
        var root = Path.Combine(grandparent, "project");
        var ios = Path.Combine(root, "ios");
        Directory.CreateDirectory(ios);
        File.WriteAllText(Path.Combine(grandparent, "Gemfile"), "gem 'fastlane'");

        var result = Preflight.CheckGemfile(ios, root);
        Assert.That(result.Ok, Is.False);
        Assert.That(result.Message, Does.Contain("Gemfile"));
    }

    [Test]
    public void Fails_when_no_gemfile_anywhere()
    {
        var root = NewTempDir();
        var ios = Path.Combine(root, "ios");
        Directory.CreateDirectory(ios);

        Assert.That(Preflight.CheckGemfile(ios, root).Ok, Is.False);
    }

    [Test]
    public void Null_stop_at_walks_to_root_and_finds_gemfile_in_working_dir()
    {
        var dir = NewTempDir();
        File.WriteAllText(Path.Combine(dir, "Gemfile"), "gem 'fastlane'");

        Assert.That(Preflight.CheckGemfile(dir, null).Ok, Is.True);
    }
}
