using System.Diagnostics;

namespace LaunchFast.App.Services;

/// <summary>
/// Resolves the user's interactive-login-shell <c>PATH</c> once per app session.
///
/// When the app is launched from Finder it inherits macOS's minimal GUI PATH
/// (<c>/usr/bin:/bin:...</c>), NOT the PATH the user gets in their terminal (which
/// <c>.zshrc</c> / <c>.zprofile</c> extends with e.g. Homebrew Ruby). That means a
/// shelled-out <c>bundle</c>/<c>ruby</c>/<c>fastlane</c>/<c>flutter</c> would resolve
/// to the wrong (often system) tool. We capture the real PATH by running the login
/// shell interactively and reuse it for preflight checks and the run environment.
/// </summary>
public static class ShellEnvironment
{
    const string Marker = "__LF_PATH__:";

    /// <summary>
    /// Pure helper: scans <paramref name="shellOutput"/> for a line starting with
    /// the <see cref="Marker"/> and returns the rest of that line, trimmed. Returns
    /// null when no such line is present.
    /// </summary>
    public static string? ExtractPath(string shellOutput)
    {
        foreach (var line in shellOutput.Split('\n'))
        {
            var trimmed = line.Trim();
            if (trimmed.StartsWith(Marker, StringComparison.Ordinal))
                return trimmed[Marker.Length..].Trim();
        }
        return null;
    }

    static string? _cached;

    /// <summary>
    /// The user's interactive-login-shell PATH, computed once and cached. Spawns the
    /// login shell at most once per app session. Fail-silent: any exception, timeout,
    /// or missing marker falls back to the process PATH. Never throws.
    /// </summary>
    public static string Path => _cached ??= Resolve();

    static string Resolve()
    {
        var fallback = Environment.GetEnvironmentVariable("PATH") ?? "";
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = Environment.GetEnvironmentVariable("SHELL") ?? "/bin/zsh",
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            psi.ArgumentList.Add("-ilc");
            psi.ArgumentList.Add($"echo {Marker}$PATH");

            using var proc = Process.Start(psi);
            if (proc is null) return fallback;

            var output = proc.StandardOutput.ReadToEnd();
            if (!proc.WaitForExit(5000)) return fallback;

            return ExtractPath(output) ?? fallback;
        }
        catch
        {
            return fallback;
        }
    }
}
