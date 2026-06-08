using LaunchFast.App.Services;
using LaunchFast.Core.Running;
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
        // bundle is resolved to an absolute path against the run PATH (or left bare when
        // not found), so assert on the executable name rather than the full path.
        Assert.That(Path.GetFileName(pty.Command), Is.EqualTo("bundle"));
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

    [Test]
    public async Task BundleInstall_failure_emits_error_line_and_does_not_throw()
    {
        // When bundle install exits with a non-zero code, the service must emit a
        // failure line via Output and must NOT throw (so the wizard stays up).
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var plan = new ScaffoldPlan(
            Files: [new FileChange(Path.Combine(root, "ios", "fastlane", "Fastfile"), "", "FF", FileChangeKind.Create)],
            Secrets: []);

        var pty = new FailingPtyFactory(exitCode: 1);
        var svc = new ProjectScaffoldService(new FakeSecretStore(), pty, root);

        var lines = new List<string>();
        svc.Output += lines.Add;

        await svc.ApplyAsync(plan, root);   // must NOT throw

        Assert.That(lines, Has.Some.Contains("bundle install failed (exit 1)"),
            "A failure line naming the exit code must be emitted.");
    }

    [Test]
    public async Task BundleInstall_passes_non_empty_env_to_pty()
    {
        // BundleInstall must pass a non-empty base environment (inheriting PATH etc.)
        // so bundle is locatable via rbenv/asdf/system PATH.
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var plan = new ScaffoldPlan(
            Files: [new FileChange(Path.Combine(root, "ios", "fastlane", "Fastfile"), "", "FF", FileChangeKind.Create)],
            Secrets: []);

        var pty = new RecordingPtyFactory { AutoComplete = true };
        var svc = new ProjectScaffoldService(new FakeSecretStore(), pty, root);

        await svc.ApplyAsync(plan, root);

        Assert.That(pty.Env, Is.Not.Null.And.Not.Empty,
            "BundleInstall must pass a non-empty environment so PATH/rbenv are available.");
    }
}

/// <summary>
/// An <see cref="IPtyFactory"/> whose process immediately exits with a configurable
/// non-zero code, used to test failure surfacing in <see cref="ProjectScaffoldService"/>.
/// </summary>
sealed class FailingPtyFactory(int exitCode) : IPtyFactory
{
    public IPtyProcess Start(string command, string[] args, string cwd,
        IReadOnlyDictionary<string, string> env) =>
        new FailingProcess(exitCode);
}

sealed class FailingProcess(int exitCode) : IPtyProcess
{
    // OutputReceived is required by the interface but never raised by this test double.
#pragma warning disable CS0067
    public event Action<string>? OutputReceived;
#pragma warning restore CS0067

    public event Action<int>? Exited
    {
        add => value?.Invoke(exitCode);
        remove { }
    }

    public void Write(string input) { }
    public void Kill() { }
    public void Dispose() { }
}
