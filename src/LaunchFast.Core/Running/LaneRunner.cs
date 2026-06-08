using LaunchFast.Core.Models;

namespace LaunchFast.Core.Running;

public sealed class RunHandle
{
    readonly IPtyProcess _process;
    public event Action<int>? Completed;

    public RunHandle(IPtyProcess process)
    {
        _process = process;
        _process.Exited += code => Completed?.Invoke(code);
    }

    public void Stop() => _process.Kill();
    public void SendInput(string text) => _process.Write(text);
}

public sealed class LaneRunner(IPtyFactory factory)
{
    public RunHandle Run(Lane lane, string platformDir,
        IReadOnlyDictionary<string, string> env, Action<string> onOutput)
    {
        var platform = lane.Platform == Platform.Ios ? "ios" : "android";
        // When the run env carries an explicit PATH (the app injects the user's
        // interactive-login-shell PATH), resolve `bundle` to an absolute path against it:
        // the real pty backend launches via posix_spawn (which does NOT search PATH), and a
        // Finder-launched app has only a minimal host PATH — so a bare "bundle" would miss
        // the user's Ruby. With no explicit PATH, run the bare name (legacy behaviour).
        var bundle = env.TryGetValue("PATH", out var pathEnv)
            ? Preflight.ResolveOnPath("bundle", pathEnv) ?? "bundle"
            : "bundle";
        var pty = factory.Start(bundle,
            ["exec", "fastlane", platform, lane.Name], platformDir, env);
        // Strip ANSI colour/escape codes (fastlane emits them, especially over a real
        // pty) so the plain-text output view doesn't show literal escape-code boxes.
        pty.OutputReceived += line => onOutput(AnsiEscape.Strip(line));
        return new RunHandle(pty);
    }
}
