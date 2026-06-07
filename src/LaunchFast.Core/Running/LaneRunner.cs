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
        var pty = factory.Start("bundle",
            ["exec", "fastlane", platform, lane.Name], platformDir, env);
        // Strip ANSI colour/escape codes (fastlane emits them, especially over a real
        // pty) so the plain-text output view doesn't show literal escape-code boxes.
        pty.OutputReceived += line => onOutput(AnsiEscape.Strip(line));
        return new RunHandle(pty);
    }
}
