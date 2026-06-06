namespace LaunchFast.Core.Running;

/// <summary>
/// Pure, allocation-free helpers used by the macOS PTY backend. Kept separate from the
/// native marshalling so they can be unit-tested without touching libc.
/// </summary>
internal static class PtyNativeHelpers
{
    /// <summary>
    /// Decodes a raw <c>waitpid</c> status word into a conventional process exit code.
    /// If the child exited normally, returns <c>WEXITSTATUS</c> (0-255). If it was
    /// terminated by a signal, returns <c>128 + signal</c> (the shell convention).
    /// </summary>
    public static int DecodeWaitStatus(int status)
    {
        // POSIX layout (matches macOS/BSD): low 7 bits are the term signal,
        // bit 7 is the core flag, bits 8-15 are the exit status.
        var termSignal = status & 0x7f;

        if (termSignal == 0)
        {
            // WIFEXITED: normal exit. WEXITSTATUS = (status >> 8) & 0xff.
            return (status >> 8) & 0xff;
        }

        // 0x7f means "stopped"; anything else is "terminated by signal".
        // We only ever wait for termination here, so treat as signalled.
        return 128 + termSignal;
    }

    /// <summary>
    /// Builds the <c>argv</c> string list for a spawn: the executable path followed by
    /// its arguments. The native layer appends the trailing NULL.
    /// </summary>
    public static string[] BuildArgv(string command, string[] args)
    {
        var argv = new string[args.Length + 1];
        argv[0] = command;
        Array.Copy(args, 0, argv, 1, args.Length);
        return argv;
    }

    /// <summary>
    /// Merges <paramref name="overrides"/> over the supplied base environment and renders
    /// each entry as a <c>KEY=VALUE</c> string. The native layer appends the trailing NULL.
    /// Override keys win; iteration order is not significant to the child.
    /// </summary>
    public static string[] BuildEnvp(
        IReadOnlyDictionary<string, string> baseEnv,
        IReadOnlyDictionary<string, string> overrides)
    {
        var merged = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var pair in baseEnv)
        {
            merged[pair.Key] = pair.Value;
        }

        foreach (var pair in overrides)
        {
            merged[pair.Key] = pair.Value;
        }

        var envp = new string[merged.Count];
        var i = 0;
        foreach (var pair in merged)
        {
            envp[i++] = pair.Key + "=" + pair.Value;
        }

        return envp;
    }
}
