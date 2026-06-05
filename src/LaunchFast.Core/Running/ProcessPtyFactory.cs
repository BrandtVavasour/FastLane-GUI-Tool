// Dependency-free IPtyFactory backend built on System.Diagnostics.Process.
//
// This replaces the native-pty adapter that was gated behind #if PTYNET: the pinned
// 0.1.39-pre package is not restorable from nuget.org. We do not get a real
// pseudo-terminal here, so we coax colour out of child tools via CLICOLOR_FORCE /
// FORCE_COLOR. Interactive TTY-only prompts will not behave exactly like a pty, but
// stdin/stdout/stderr streaming, environment passing, and exit-code reporting all work.
using System.Diagnostics;

namespace LaunchFast.Core.Running;

public sealed class ProcessPtyFactory : IPtyFactory
{
    public IPtyProcess Start(string command, string[] args, string cwd,
        IReadOnlyDictionary<string, string> env)
    {
        var process = new ProcessPtyProcess(command, args, cwd, env);
        process.Start();
        return process;
    }
}

internal sealed class ProcessPtyProcess : IPtyProcess
{
    readonly Process _process;
    int _exitedRaised;
    int _disposed;

    public event Action<string>? OutputReceived;
    public event Action<int>? Exited;

    public ProcessPtyProcess(string command, string[] args, string cwd,
        IReadOnlyDictionary<string, string> env)
    {
        var psi = new ProcessStartInfo
        {
            FileName = command,
            WorkingDirectory = cwd,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
        };

        foreach (var arg in args)
        {
            psi.ArgumentList.Add(arg);
        }

        foreach (var pair in env)
        {
            psi.Environment[pair.Key] = pair.Value;
        }

        // Best-effort colour even though we have no real tty.
        psi.Environment["CLICOLOR_FORCE"] = "1";
        psi.Environment["FORCE_COLOR"] = "1";

        _process = new Process { StartInfo = psi, EnableRaisingEvents = true };
        _process.OutputDataReceived += OnDataReceived;
        _process.ErrorDataReceived += OnDataReceived;
        _process.Exited += OnExited;
    }

    public void Start()
    {
        _process.Start();
        _process.BeginOutputReadLine();
        _process.BeginErrorReadLine();
    }

    void OnDataReceived(object? sender, DataReceivedEventArgs e)
    {
        if (e.Data is not null)
        {
            OutputReceived?.Invoke(e.Data);
        }
    }

    void OnExited(object? sender, EventArgs e) => RaiseExited();

    void RaiseExited()
    {
        if (Interlocked.Exchange(ref _exitedRaised, 1) != 0)
        {
            return;
        }

        int code;
        try
        {
            code = _process.ExitCode;
        }
        catch
        {
            // Process not actually exited / already disposed: nothing useful to report.
            return;
        }

        Exited?.Invoke(code);
    }

    public void Write(string input)
    {
        try
        {
            var writer = _process.StandardInput;
            writer.Write(input);
            if (!input.EndsWith('\n'))
            {
                writer.Write('\n');
            }

            writer.Flush();
        }
        catch
        {
            // stdin already closed / process exited: ignore.
        }
    }

    public void Kill()
    {
        try
        {
            _process.Kill(entireProcessTree: true);
        }
        catch
        {
            // Already exited: ignore. Exited fires from the natural exit.
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        _process.OutputDataReceived -= OnDataReceived;
        _process.ErrorDataReceived -= OnDataReceived;
        _process.Exited -= OnExited;
        _process.Dispose();
    }
}
