using LaunchFast.App.Services;
using LaunchFast.Core.Scaffolding;

namespace LaunchFast.App.Tests;

public class ProjectScaffoldServiceTests
{
    [Test]
    public async Task Writes_files_stores_secrets_and_runs_bundle_install()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var plan = new ScaffoldPlan(
            Files: [new FileChange(Path.Combine(root, "ios", "fastlane", "Fastfile"), "", "FF", FileChangeKind.Create)],
            Secrets: [new SecretToStore("MATCH_PASSWORD", "hunter2")]);

        var secrets = new FakeSecretStore();
        var pty = new RecordingPtyFactory { AutoComplete = true };
        var svc = new ProjectScaffoldService(secrets, pty, projectId: root);

        await svc.ApplyAsync(plan, root);

        Assert.That(File.ReadAllText(Path.Combine(root, "ios", "fastlane", "Fastfile")), Is.EqualTo("FF"));
        Assert.That(secrets.Get(root, "MATCH_PASSWORD"), Is.EqualTo("hunter2"));
        Assert.That(pty.Command, Is.EqualTo("bundle"));
        Assert.That(pty.LastCwd, Is.EqualTo(Path.Combine(root, "ios")));
    }

    [Test]
    public async Task Runs_bundle_install_for_each_platform_with_files()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var plan = new ScaffoldPlan(
            Files: [
                new FileChange(Path.Combine(root, "ios", "fastlane", "Fastfile"), "", "FF", FileChangeKind.Create),
                new FileChange(Path.Combine(root, "android", "fastlane", "Fastfile"), "", "FF", FileChangeKind.Create),
            ],
            Secrets: []);

        var pty = new RecordingPtyFactory { AutoComplete = true };
        var svc = new ProjectScaffoldService(new FakeSecretStore(), pty, root);

        await svc.ApplyAsync(plan, root);

        Assert.That(pty.StartCount, Is.EqualTo(2));
    }
}
