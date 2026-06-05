using System.Collections.Concurrent;
using LaunchFast.Core.Running;
using NUnit.Framework;

namespace IntegrationTests;

// Exercises the real ProcessPtyFactory end-to-end (not via fakes): spawn a shell,
// stream its output through the events, and observe the exit code.
[TestFixture]
public sealed class PtyIntegrationTests
{
    [Test]
    public void ProcessPty_runs_real_command_streams_and_exits()
    {
        var factory = new ProcessPtyFactory();
        var lines = new ConcurrentQueue<string>();
        var exited = new ManualResetEventSlim(false);
        var exitCode = -1;

        using var proc = factory.Start(
            "/bin/sh",
            ["-c", "echo line1; echo line2; exit 0"],
            cwd: Path.GetTempPath(),
            env: new Dictionary<string, string>());

        proc.OutputReceived += line => lines.Enqueue(line);
        proc.Exited += code =>
        {
            exitCode = code;
            exited.Set();
        };

        Assert.That(
            exited.Wait(TimeSpan.FromSeconds(10)),
            Is.True,
            "Process did not raise Exited within 10s.");

        Assert.That(exitCode, Is.EqualTo(0));

        var captured = lines.ToArray();
        Assert.That(captured, Does.Contain("line1"));
        Assert.That(captured, Does.Contain("line2"));
    }
}
