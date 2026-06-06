namespace LaunchFast.Core.Running;

/// <summary>
/// The <see cref="IPtyFactory"/> the app composes by default. On macOS it prefers the real
/// pseudo-terminal backend (<see cref="MacPtyFactory"/>) for genuine tty behaviour and ANSI
/// colour; on other platforms, or if the native pty path fails (e.g. a restricted sandbox
/// blocks <c>posix_spawn</c>), it transparently falls back to the pipe-based
/// <see cref="ProcessPtyFactory"/>. The fallback is sticky: once the native backend faults,
/// subsequent starts go straight to the pipe backend.
/// </summary>
public sealed class DefaultPtyFactory : IPtyFactory
{
    readonly IPtyFactory? _native;
    readonly IPtyFactory _fallback;
    int _nativeDisabled;

    public DefaultPtyFactory()
        : this(OperatingSystem.IsMacOS() ? new MacPtyFactory() : null, new ProcessPtyFactory())
    {
    }

    internal DefaultPtyFactory(IPtyFactory? native, IPtyFactory fallback)
    {
        _native = native;
        _fallback = fallback;
    }

    public IPtyProcess Start(string command, string[] args, string cwd,
        IReadOnlyDictionary<string, string> env)
    {
        if (_native is not null && Volatile.Read(ref _nativeDisabled) == 0)
        {
            try
            {
                return _native.Start(command, args, cwd, env);
            }
            catch (Exception ex) when (ex is PtyStartException or DllNotFoundException
                or EntryPointNotFoundException)
            {
                // The native pty is unavailable on this host/sandbox: disable it and fall
                // back for this start and every future one.
                Volatile.Write(ref _nativeDisabled, 1);
            }
        }

        return _fallback.Start(command, args, cwd, env);
    }
}
