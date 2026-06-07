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
    readonly object _gate = new();
    readonly List<string> _pendingOutput = [];
    Action<string>? _outputHandlers;
    Action<int>? _exitedHandlers;
    bool _hasExited;
    int _exitCode;
    int _exitedRaised;
    int _disposed;

    // Buffer early output and remember the exit code so a subscriber that attaches
    // AFTER Start() — the normal usage, since callers wire handlers on the returned
    // process — never misses lines or a fast Exited. Without this a quick child can
    // fire before the caller subscribes, dropping output or hanging the run (this also
    // surfaced as a flaky test under CI's contended scheduling).
    public event Action<string>? OutputReceived
    {
        add
        {
            List<string>? replay = null;
            lock (_gate)
            {
                _outputHandlers += value;
                if (_pendingOutput.Count > 0)
                {
                    replay = [.. _pendingOutput];
                    _pendingOutput.Clear();
                }
            }

            if (replay is not null && value is not null)
            {
                foreach (var line in replay)
                {
                    value(line);
                }
            }
        }
        remove
        {
            lock (_gate)
            {
                _outputHandlers -= value;
            }
        }
    }

    public event Action<int>? Exited
    {
        add
        {
            bool fireNow = false;
            int code = 0;
            lock (_gate)
            {
                _exitedHandlers += value;
                if (_hasExited)
                {
                    fireNow = true;
                    code = _exitCode;
                }
            }

            if (fireNow)
            {
                value?.Invoke(code);
            }
        }
        remove
        {
            lock (_gate)
            {
                _exitedHandlers -= value;
            }
        }
    }

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
        if (e.Data is null)
        {
            return;
        }

        Action<string>? handlers;
        lock (_gate)
        {
            handlers = _outputHandlers;
            if (handlers is null)
            {
                _pendingOutput.Add(e.Data);
                return;
            }
        }

        handlers(e.Data);
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

        Action<int>? handlers;
        lock (_gate)
        {
            _hasExited = true;
            _exitCode = code;
            handlers = _exitedHandlers;
        }

        handlers?.Invoke(code);
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
