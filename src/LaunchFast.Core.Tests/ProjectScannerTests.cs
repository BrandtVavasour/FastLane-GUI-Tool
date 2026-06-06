using LaunchFast.Core.Models;
using LaunchFast.Core.Scanning;

namespace LaunchFast.Core.Tests;

public class ProjectScannerTests
{
    [Test]
    public void HasFastlane_true_when_a_platform_dir_present()
    {
        var p = new Project("n", "/p", null, "/p/ios/fastlane", null, false, null);
        Assert.That(p.HasFastlane, Is.True);
        var none = p with { IosFastlaneDir = null };
        Assert.That(none.HasFastlane, Is.False);
    }

    static string MakeProject(string name, bool ios = true, bool android = true, bool match = true)
    {
        var root = Path.Combine(Path.GetTempPath(), "lf-" + Guid.NewGuid().ToString("N"), name);
        Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root, "pubspec.yaml"), "name: demo\nversion: 1.2.3+9\n");
        if (ios) Directory.CreateDirectory(Path.Combine(root, "ios", "fastlane"));
        if (android) Directory.CreateDirectory(Path.Combine(root, "android", "fastlane"));
        if (ios && match)
            File.WriteAllText(Path.Combine(root, "ios", "fastlane", "Matchfile"), "type(\"appstore\")");
        return root;
    }

    [Test]
    public void Detects_flutter_project_with_both_platforms()
    {
        var root = MakeProject("demo");
        var project = ProjectScanner.TryScanRoot(root);
        Assert.That(project, Is.Not.Null);
        Assert.That(project!.Version, Is.EqualTo("1.2.3+9"));
        Assert.That(project.IosFastlaneDir, Is.Not.Null);
        Assert.That(project.AndroidFastlaneDir, Is.Not.Null);
        Assert.That(project.HasMatchfile, Is.True);
    }

    [Test]
    public void Returns_null_when_no_fastlane()
    {
        var root = MakeProject("bare", ios: false, android: false);
        Assert.That(ProjectScanner.TryScanRoot(root), Is.Null);
    }

    [Test]
    public void Flutter_project_without_fastlane_is_a_setup_candidate()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "ios"));
        Directory.CreateDirectory(Path.Combine(root, "android"));
        File.WriteAllText(Path.Combine(root, "pubspec.yaml"), "name: demo\nversion: 1.0.0+1\n");
        var p = ProjectScanner.TryScanRoot(root);
        Assert.That(p, Is.Not.Null);
        Assert.That(p!.HasFastlane, Is.False);
        Assert.That(p.Version, Is.EqualTo("1.0.0+1"));
    }

    [Test]
    public void Non_flutter_non_fastlane_dir_is_null()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        Assert.That(ProjectScanner.TryScanRoot(root), Is.Null);
    }

    [Test]
    public void ScanWorkspace_returns_only_flutter_projects()
    {
        var workspace = Path.Combine(Path.GetTempPath(), "lf-ws-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workspace);

        var proj1 = Path.Combine(workspace, "proj1");
        Directory.CreateDirectory(proj1);
        File.WriteAllText(Path.Combine(proj1, "pubspec.yaml"), "name: a\nversion: 1.0.0\n");
        Directory.CreateDirectory(Path.Combine(proj1, "ios", "fastlane"));

        var proj2 = Path.Combine(workspace, "proj2");
        Directory.CreateDirectory(proj2);
        File.WriteAllText(Path.Combine(proj2, "pubspec.yaml"), "name: b\nversion: 2.0.0\n");
        Directory.CreateDirectory(Path.Combine(proj2, "android", "fastlane"));

        var notAProject = Path.Combine(workspace, "notes");
        Directory.CreateDirectory(notAProject);
        File.WriteAllText(Path.Combine(notAProject, "readme.txt"), "hello");

        var projects = ProjectScanner.ScanWorkspace(workspace);

        Assert.That(projects, Has.Count.EqualTo(2));
        Assert.That(projects.Select(p => p.Name), Is.EquivalentTo(new[] { "proj1", "proj2" }));
    }
}
