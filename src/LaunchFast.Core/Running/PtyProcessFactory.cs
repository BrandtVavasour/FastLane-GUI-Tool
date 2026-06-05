// Real Pty.Net wrapper for the IPtyFactory/IPtyProcess seam.
//
// NOTE ON THE Pty.Net API:
// The plan pins Pty.Net 0.1.39-pre and sketches the Microsoft `vs-pty.net` API
// (PtyProvider.SpawnAsync(PtyOptions{ Name, Cwd, App, CommandLine, Environment }),
// connection.ReaderStream / WriterStream / ProcessExited / Kill()). That API is the
// only Pty.Net variant that can pass a custom environment AND surface a process exit
// code, both of which this seam requires, so this adapter is written against it.
//
// This file is gated behind the `PTYNET` compile constant (see LaunchFast.Core.csproj)
// because Pty.Net 0.1.39-pre is not currently restorable from the configured feed
// (nuget.org only publishes up to 0.1.16-pre, which exposes an older, incompatible API
// surface — synchronous Spawn(command, w, h, cwd, options), PtyData/PtyDisconnected,
// no environment, no exit code). Once the pinned package is restorable, define PTYNET
// to compile this adapter. It is exercised by IntegrationTests in Phase 10, not by the
// unit tests in this phase.
#if PTYNET
using System.Text;

using Pty.Net;

namespace LaunchFast.Core.Running;

public sealed class PtyProcessFactory : IPtyFactory
{
    public IPtyProcess Start(string command, string[] args, string cwd,
        IReadOnlyDictionary<string, string> env)
    {
        var options = new PtyOptions
        {
            Name = "xterm-256color",
            Cwd = cwd,
            App = command,
            CommandLine = args,
            Environment = new Dictionary<string, string>(env),
        };

        // SpawnAsync is the documented entry point; block to keep the synchronous seam.
        var connection = PtyProvider.SpawnAsync(options, CancellationToken.None)
            .GetAwaiter().GetResult();

        return new PtyProcessAdapter(connection);
    }
}

internal sealed class PtyProcessAdapter : IPtyProcess
{
    readonly IPtyConnection _connection;
    readonly CancellationTokenSource _cts = new();
    int _disposed;

    public event Action<string>? OutputReceived;
    public event Action<int>? Exited;

    public PtyProcessAdapter(IPtyConnection connection)
    {
        _connection = connection;
        _connection.ProcessExited += OnProcessExited;
        _ = PumpAsync(_cts.Token);
    }

    void OnProcessExited(object? sender, PtyExitedEventArgs e) => Exited?.Invoke(e.ExitCode);

    async Task PumpAsync(CancellationToken token)
    {
        var buffer = new byte[4096];
        var stream = _connection.ReaderStream;
        try
        {
            while (!token.IsCancellationRequested)
            {
                var read = await stream.ReadAsync(buffer.AsMemory(), token).ConfigureAwait(false);
                if (read <= 0)
                {
                    break;
                }

                OutputReceived?.Invoke(Encoding.UTF8.GetString(buffer, 0, read));
            }
        }
        catch (OperationCanceledException)
        {
            // Expected on Kill/Dispose.
        }
        catch (IOException)
        {
            // Stream closed as the pty exited.
        }
    }

    public void Write(string input)
    {
        var bytes = Encoding.UTF8.GetBytes(input);
        _connection.WriterStream.Write(bytes, 0, bytes.Length);
        _connection.WriterStream.Flush();
    }

    public void Kill() => _connection.Kill();

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        _connection.ProcessExited -= OnProcessExited;
        _cts.Cancel();
        _cts.Dispose();
        _connection.Dispose();
    }
}
#endif
