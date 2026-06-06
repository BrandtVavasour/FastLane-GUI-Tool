using System.Collections;
using System.Runtime.InteropServices;
using System.Text;

namespace LaunchFast.Core.Running;

/// <summary>
/// A real pseudo-terminal <see cref="IPtyFactory"/> for macOS. Each call to
/// <see cref="Start"/> opens a pty via <c>openpty</c> and launches the command with
/// <c>posix_spawn</c>, wiring the child's stdin/stdout/stderr to the pty slave so the
/// program sees a genuine tty (ANSI colour, <c>isatty()</c>, line discipline). Output is
/// streamed off the master fd; the exit code is reaped with <c>waitpid</c>.
/// </summary>
public sealed class MacPtyFactory : IPtyFactory
{
    public IPtyProcess Start(string command, string[] args, string cwd,
        IReadOnlyDictionary<string, string> env)
    {
        var process = new MacPtyProcess();
        process.Start(command, args, cwd, env);
        return process;
    }
}

internal sealed class MacPtyProcess : IPtyProcess
{
    readonly object _writeLock = new();
    int _masterFd = -1;
    int _pid = -1;
    Thread? _readerThread;
    Thread? _exitThread;
    int _exitedRaised;
    int _disposed;

    public event Action<string>? OutputReceived;
    public event Action<int>? Exited;

    public void Start(string command, string[] args, string cwd,
        IReadOnlyDictionary<string, string> env)
    {
        var rc = MacPtyInterop.openpty(out var master, out var slave,
            IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
        if (rc != 0)
        {
            throw new PtyStartException(
                $"openpty failed (rc={rc}, errno={Marshal.GetLastPInvokeError()}).");
        }

        var baseEnv = SnapshotEnvironment();
        var argv = PtyNativeHelpers.BuildArgv(command, args);
        var envp = PtyNativeHelpers.BuildEnvp(baseEnv, env);

        var fileActions = Marshal.AllocHGlobal(MacPtyInterop.FileActionsSize);
        IntPtr[]? argvNative = null;
        IntPtr[]? envpNative = null;
        var fileActionsInit = false;

        try
        {
            ZeroNative(fileActions, MacPtyInterop.FileActionsSize);

            if (MacPtyInterop.posix_spawn_file_actions_init(fileActions) != 0)
            {
                throw new PtyStartException(
                    $"posix_spawn_file_actions_init failed (errno={Marshal.GetLastPInvokeError()}).");
            }

            fileActionsInit = true;

            // Set the child cwd, then bind the slave fd onto the three std streams and
            // close the (now-duplicated) slave in the child.
            Check(MacPtyInterop.posix_spawn_file_actions_addchdir_np(fileActions, cwd),
                "addchdir_np");
            Check(MacPtyInterop.posix_spawn_file_actions_adddup2(fileActions, slave, MacPtyInterop.Stdin),
                "adddup2(stdin)");
            Check(MacPtyInterop.posix_spawn_file_actions_adddup2(fileActions, slave, MacPtyInterop.Stdout),
                "adddup2(stdout)");
            Check(MacPtyInterop.posix_spawn_file_actions_adddup2(fileActions, slave, MacPtyInterop.Stderr),
                "adddup2(stderr)");
            Check(MacPtyInterop.posix_spawn_file_actions_addclose(fileActions, slave),
                "addclose(slave)");

            argvNative = BuildNativeStringArray(argv);
            envpNative = BuildNativeStringArray(envp);

            var spawnRc = MacPtyInterop.posix_spawn(out var pid, command, fileActions,
                IntPtr.Zero, argvNative, envpNative);
            if (spawnRc != 0)
            {
                throw new PtyStartException(
                    $"posix_spawn failed for '{command}' (rc={spawnRc}).");
            }

            _pid = pid;
            _masterFd = master;
            master = -1; // ownership transferred to this instance
        }
        catch
        {
            // Spawn failed: the parent still owns the master fd; release it.
            if (master >= 0)
            {
                MacPtyInterop.close(master);
            }

            throw;
        }
        finally
        {
            // The parent never uses the slave fd.
            MacPtyInterop.close(slave);

            if (fileActionsInit)
            {
                MacPtyInterop.posix_spawn_file_actions_destroy(fileActions);
            }

            Marshal.FreeHGlobal(fileActions);
            FreeNativeStringArray(argvNative);
            FreeNativeStringArray(envpNative);
        }

        _readerThread = new Thread(ReaderLoop)
        {
            IsBackground = true,
            Name = "MacPty-Reader",
        };
        _readerThread.Start();

        _exitThread = new Thread(ExitWatcher)
        {
            IsBackground = true,
            Name = "MacPty-Exit",
        };
        _exitThread.Start();
    }

    void ReaderLoop()
    {
        var buffer = new byte[8192];
        var decoder = Encoding.UTF8.GetDecoder(); // carries partial multibyte sequences across reads
        var chars = new char[8192];

        while (true)
        {
            var fd = Volatile.Read(ref _masterFd);
            if (fd < 0)
            {
                break;
            }

            var n = MacPtyInterop.read(fd, buffer, (nuint)buffer.Length);

            if (n > 0)
            {
                var count = (int)n;
                var charCount = decoder.GetChars(buffer, 0, count, chars, 0, flush: false);
                if (charCount > 0)
                {
                    OutputReceived?.Invoke(new string(chars, 0, charCount));
                }

                continue;
            }

            // n == 0: EOF (slave closed). n < 0: read error (EBADF after close, or EIO when
            // the pty's child end has gone away). Either way the stream is finished.
            break;
        }
    }

    void ExitWatcher()
    {
        var pid = _pid;
        if (pid <= 0)
        {
            return;
        }

        // Block until the child is reaped. EINTR can interrupt; retry in that case.
        while (true)
        {
            var rc = MacPtyInterop.waitpid(pid, out var status, 0);
            if (rc == pid)
            {
                RaiseExited(PtyNativeHelpers.DecodeWaitStatus(status));
                return;
            }

            if (rc == -1)
            {
                const int EINTR = 4;
                if (Marshal.GetLastPInvokeError() == EINTR)
                {
                    continue;
                }

                // ECHILD or similar: the child is gone but we cannot read a status.
                RaiseExited(-1);
                return;
            }
        }
    }

    void RaiseExited(int code)
    {
        if (Interlocked.Exchange(ref _exitedRaised, 1) != 0)
        {
            return;
        }

        Exited?.Invoke(code);
    }

    public void Write(string input)
    {
        var fd = Volatile.Read(ref _masterFd);
        if (fd < 0)
        {
            return;
        }

        var payload = input.EndsWith('\n') ? input : input + "\n";
        var bytes = Encoding.UTF8.GetBytes(payload);

        lock (_writeLock)
        {
            var offset = 0;
            while (offset < bytes.Length)
            {
                var chunk = bytes;
                if (offset > 0)
                {
                    chunk = bytes[offset..];
                }

                var written = MacPtyInterop.write(fd, chunk, (nuint)chunk.Length);
                if (written <= 0)
                {
                    // fd closed mid-write or the child is gone: stop, ignore.
                    return;
                }

                offset += (int)written;
            }
        }
    }

    public void Kill()
    {
        var pid = _pid;
        if (pid <= 0)
        {
            return;
        }

        // Polite first, then forceful. The exit watcher raises Exited from the reaped status.
        MacPtyInterop.kill(pid, MacPtyInterop.SIGTERM);

        var exitThread = _exitThread;
        if (exitThread is not null && !exitThread.Join(TimeSpan.FromSeconds(2)))
        {
            MacPtyInterop.kill(pid, MacPtyInterop.SIGKILL);
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        // Closing the master fd unblocks the reader thread (read returns <= 0).
        var fd = Interlocked.Exchange(ref _masterFd, -1);
        if (fd >= 0)
        {
            MacPtyInterop.close(fd);
        }

        _readerThread?.Join(TimeSpan.FromSeconds(2));
        // The exit watcher exits on its own once the child is reaped; don't block forever.
        _exitThread?.Join(TimeSpan.FromSeconds(2));
    }

    static IReadOnlyDictionary<string, string> SnapshotEnvironment()
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (DictionaryEntry entry in Environment.GetEnvironmentVariables())
        {
            if (entry.Key is string key && entry.Value is string value)
            {
                result[key] = value;
            }
        }

        return result;
    }

    static IntPtr[] BuildNativeStringArray(string[] values)
    {
        // NULL-terminated array of UTF-8 C-string pointers.
        var array = new IntPtr[values.Length + 1];
        for (var i = 0; i < values.Length; i++)
        {
            array[i] = Marshal.StringToCoTaskMemUTF8(values[i]);
        }

        array[values.Length] = IntPtr.Zero;
        return array;
    }

    static void FreeNativeStringArray(IntPtr[]? array)
    {
        if (array is null)
        {
            return;
        }

        foreach (var ptr in array)
        {
            if (ptr != IntPtr.Zero)
            {
                Marshal.FreeCoTaskMem(ptr);
            }
        }
    }

    static void ZeroNative(IntPtr ptr, int length)
    {
        var zeros = new byte[length];
        Marshal.Copy(zeros, 0, ptr, length);
    }

    static void Check(int rc, string what)
    {
        if (rc != 0)
        {
            throw new PtyStartException(
                $"posix_spawn_file_actions_{what} failed (rc={rc}, errno={Marshal.GetLastPInvokeError()}).");
        }
    }
}

/// <summary>
/// Thrown when a real pty could not be opened or the command could not be spawned. The
/// factory selector uses this to fall back to the pipe-based backend.
/// </summary>
public sealed class PtyStartException : Exception
{
    public PtyStartException(string message) : base(message)
    {
    }
}
