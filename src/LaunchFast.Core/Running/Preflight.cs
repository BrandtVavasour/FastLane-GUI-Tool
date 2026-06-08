namespace LaunchFast.Core.Running;

public sealed record PreflightResult(bool Ok, string Message);

public static class Preflight
{
    /// <summary>
    /// Mirrors bundler's Gemfile resolution: starts in <paramref name="workingDir"/>
    /// and walks UP the directory tree looking for a <c>Gemfile</c>. The search stops
    /// after checking <paramref name="stopAt"/> (inclusive) — typically the project
    /// root — so it never escapes above the project, or at the filesystem root when
    /// <paramref name="stopAt"/> is null. Never throws: IO errors are treated as
    /// "keep walking / not found".
    /// </summary>
    public static PreflightResult CheckGemfile(string workingDir, string? stopAt = null)
    {
        var stop = stopAt is null ? null : Normalize(stopAt);

        var current = SafeFullPath(workingDir);
        while (current is not null)
        {
            if (GemfileExists(current))
                return new(true, "Gemfile present");

            // Stop once we've checked the stopAt directory (inclusive).
            if (stop is not null && string.Equals(Normalize(current), stop, PathComparison))
                break;

            current = SafeParent(current);
        }

        return new(false,
            "No Gemfile in this platform directory or any parent up to the project root — fastlane can't run via bundler.");
    }

    static readonly StringComparison PathComparison =
        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    static string Normalize(string path) =>
        Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

    static string? SafeFullPath(string path)
    {
        try { return Path.GetFullPath(path); }
        catch { return null; }
    }

    static string? SafeParent(string path)
    {
        try { return Directory.GetParent(path)?.FullName; }
        catch { return null; }
    }

    static bool GemfileExists(string dir)
    {
        try { return File.Exists(Path.Combine(dir, "Gemfile")); }
        catch { return false; }
    }

    /// <summary>
    /// Checks whether <paramref name="tool"/> resolves on a PATH. When
    /// <paramref name="path"/> is null the process PATH is used (the historical
    /// behaviour); pass an explicit PATH (e.g. the user's interactive-login-shell
    /// PATH) to resolve tools exactly as they would in the user's terminal.
    /// </summary>
    public static PreflightResult CheckTool(string tool, string? path = null) =>
        ResolveOnPath(tool, path) is not null
            ? new(true, $"{tool} found")
            : new(false, $"`{tool}` not found on PATH.");

    /// <summary>
    /// The absolute path of <paramref name="tool"/> on the given PATH (or the process
    /// PATH when null), or null when not found. Used to run a tool by absolute path so
    /// it works under <c>posix_spawn</c> (which doesn't search PATH) and is immune to a
    /// minimal host PATH (e.g. a Finder-launched app).
    /// </summary>
    public static string? ResolveOnPath(string tool, string? path = null) =>
        FindOnPath(tool, path);

    static string? FindOnPath(string tool, string? path)
    {
        foreach (var dir in (path ?? Environment.GetEnvironmentVariable("PATH") ?? "").Split(':'))
        {
            var candidate = Path.Combine(dir, tool);
            if (File.Exists(candidate)) return candidate;
        }
        return null;
    }
}
