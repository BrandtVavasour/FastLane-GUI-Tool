using System.Collections.Generic;
using System.Threading;

using LaunchFast.Core.Running;

namespace LaunchFast.Core.Tests;

public class ProcessPtyFactoryTests
{
    [Test]
    public void Streams_output_and_reports_exit_code()
    {
        var factory = new ProcessPtyFactory();
        var lines = new List<string>();
        var done = new ManualResetEventSlim();
        int exit = -999;

        var p = factory.Start("/bin/echo", ["hello-pty"], "/tmp",
            new Dictionary<string, string>());
        p.OutputReceived += s => { lock (lines) lines.Add(s); };
        p.Exited += c => { exit = c; done.Set(); };

        Assert.That(done.Wait(TimeSpan.FromSeconds(10)), Is.True, "process did not exit in time");
        Assert.That(exit, Is.EqualTo(0));
        lock (lines) Assert.That(lines, Does.Contain("hello-pty"));
        p.Dispose();
    }

    [Test]
    public void Nonzero_exit_is_reported()
    {
        var factory = new ProcessPtyFactory();
        var done = new ManualResetEventSlim();
        int exit = -999;
        var p = factory.Start("/bin/sh", ["-c", "exit 7"], "/tmp",
            new Dictionary<string, string>());
        p.Exited += c => { exit = c; done.Set(); };
        Assert.That(done.Wait(TimeSpan.FromSeconds(10)), Is.True);
        Assert.That(exit, Is.EqualTo(7));
        p.Dispose();
    }

    [Test]
    public void Env_is_passed_to_child()
    {
        var factory = new ProcessPtyFactory();
        var lines = new List<string>();
        var done = new ManualResetEventSlim();
        var p = factory.Start("/bin/sh", ["-c", "echo VAL=$MYVAR"], "/tmp",
            new Dictionary<string, string> { ["MYVAR"] = "xyz" });
        p.OutputReceived += s => { lock (lines) lines.Add(s); };
        p.Exited += _ => done.Set();
        Assert.That(done.Wait(TimeSpan.FromSeconds(10)), Is.True);
        lock (lines) Assert.That(lines, Does.Contain("VAL=xyz"));
        p.Dispose();
    }
}
