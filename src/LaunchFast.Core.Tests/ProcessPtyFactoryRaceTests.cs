using LaunchFast.Core.Running;

namespace LaunchFast.Core.Tests;

/// <summary>
/// Guards the subscribe-after-Start contract of <see cref="ProcessPtyFactory"/>:
/// callers wire <c>OutputReceived</c>/<c>Exited</c> on the returned process AFTER it
/// has started, so a fast child must not drop its output or exit before the handlers
/// attach. This is the deterministic form of a flake that only surfaced under CI's
/// contended scheduling.
/// </summary>
public class ProcessPtyFactoryRaceTests
{
    [Test]
    public void Late_subscriber_still_receives_buffered_output_and_exit()
    {
        var factory = new ProcessPtyFactory();
        var p = factory.Start("/bin/echo", ["hello-late"], "/tmp",
            new Dictionary<string, string>());

        // Let the (very fast) process run to completion BEFORE subscribing, so the
        // output + Exited would already have fired with no handlers attached.
        Thread.Sleep(500);

        var lines = new List<string>();
        var done = new ManualResetEventSlim();
        int exit = -999;
        p.OutputReceived += s => { lock (lines) lines.Add(s); };
        p.Exited += c => { exit = c; done.Set(); };

        Assert.That(done.Wait(TimeSpan.FromSeconds(5)), Is.True,
            "late subscriber missed Exited");
        Assert.That(exit, Is.EqualTo(0));
        lock (lines) Assert.That(lines, Does.Contain("hello-late"));
        p.Dispose();
    }
}
