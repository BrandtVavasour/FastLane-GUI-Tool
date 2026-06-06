using System.Collections.Concurrent;
using System.Text;
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

    // --- Real macOS pseudo-terminal backend (openpty + posix_spawn) ---
    //
    // These exercise MacPtyFactory directly. They are macOS-only; on other platforms or in
    // a sandbox that blocks pty allocation / posix_spawn they Assert.Ignore rather than fail.

    [Test]
    public void Real_pty_provides_a_tty()
    {
        SkipIfNotMac();

        var output = new StringBuilder();
        var exited = new ManualResetEventSlim(false);
        var exitCode = -1;

        try
        {
            using var proc = StartReal(
                "/bin/sh",
                ["-c", "test -t 1 && printf IS_A_TTY; exit 7"],
                cwd: "/tmp",
                env: new Dictionary<string, string>(),
                output,
                exited,
                code => exitCode = code);

            Assert.That(
                exited.Wait(TimeSpan.FromSeconds(10)),
                Is.True,
                "Real pty did not raise Exited within 10s.");

            var text = output.ToString();
            Assert.That(text, Does.Contain("IS_A_TTY"),
                "stdout was not a tty (isatty was false).");
            Assert.That(exitCode, Is.EqualTo(7), "exit code did not propagate.");
        }
        catch (PtyStartException ex)
        {
            Assert.Ignore($"Sandbox blocked real pty: {ex.Message}");
        }
    }

    [Test]
    public void Real_pty_streams_and_passes_env()
    {
        SkipIfNotMac();

        var output = new StringBuilder();
        var exited = new ManualResetEventSlim(false);

        try
        {
            using var proc = StartReal(
                "/bin/sh",
                ["-c", "echo $LF_PROBE"],
                cwd: "/tmp",
                env: new Dictionary<string, string> { ["LF_PROBE"] = "hello-pty" },
                output,
                exited,
                _ => { });

            Assert.That(exited.Wait(TimeSpan.FromSeconds(10)), Is.True,
                "Real pty did not raise Exited within 10s.");
            Assert.That(output.ToString(), Does.Contain("hello-pty"),
                "environment variable was not passed to the child.");
        }
        catch (PtyStartException ex)
        {
            Assert.Ignore($"Sandbox blocked real pty: {ex.Message}");
        }
    }

    [Test]
    public void Real_pty_runs_in_cwd()
    {
        SkipIfNotMac();

        var output = new StringBuilder();
        var exited = new ManualResetEventSlim(false);

        try
        {
            using var proc = StartReal(
                "/bin/sh",
                ["-c", "pwd"],
                cwd: "/tmp",
                env: new Dictionary<string, string>(),
                output,
                exited,
                _ => { });

            Assert.That(exited.Wait(TimeSpan.FromSeconds(10)), Is.True,
                "Real pty did not raise Exited within 10s.");

            var text = output.ToString();
            Assert.That(
                text.Contains("/tmp") || text.Contains("/private/tmp"),
                Is.True,
                $"child did not run in the requested cwd; pwd output was: {text}");
        }
        catch (PtyStartException ex)
        {
            Assert.Ignore($"Sandbox blocked real pty: {ex.Message}");
        }
    }

    static IPtyProcess StartReal(
        string command,
        string[] args,
        string cwd,
        IReadOnlyDictionary<string, string> env,
        StringBuilder output,
        ManualResetEventSlim exited,
        Action<int> onExit)
    {
        var factory = new MacPtyFactory();
        IPtyProcess proc;
        try
        {
            proc = factory.Start(command, args, cwd, env);
        }
        catch (DllNotFoundException ex)
        {
            throw new PtyStartException($"libc unavailable: {ex.Message}");
        }
        catch (EntryPointNotFoundException ex)
        {
            throw new PtyStartException($"libc symbol unavailable: {ex.Message}");
        }

        proc.OutputReceived += chunk =>
        {
            lock (output)
            {
                output.Append(chunk);
            }
        };
        proc.Exited += code =>
        {
            onExit(code);
            exited.Set();
        };

        return proc;
    }

    static void SkipIfNotMac()
    {
        if (!OperatingSystem.IsMacOS())
        {
            Assert.Ignore("Real pty backend is macOS-only.");
        }
    }
}
