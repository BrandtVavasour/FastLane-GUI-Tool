using System.Text.RegularExpressions;
using LaunchFast.Core.Models;

namespace LaunchFast.Core.Screenshots;

/// <summary>
/// A group of captured screenshots for a single locale, ordered by file name.
/// </summary>
public sealed record ScreenshotGroup(string Locale, IReadOnlyList<string> Paths);

/// <summary>
/// The iOS snapshot (fastlane <c>snapshot</c> + optional <c>frameit</c>) configuration
/// discovered on disk for a project, plus the screenshots already captured.
///
/// <para><see cref="Devices"/>, <see cref="Languages"/>, <see cref="Scheme"/>,
/// <see cref="LaunchArguments"/> and <see cref="OutputDirectory"/> come from
/// <c>ios/fastlane/Snapfile</c> when present. <see cref="FrameitEnabled"/> reflects
/// the presence of a <c>Framefile</c>/<c>Framefile.json</c>; <see cref="FrameTitle"/>
/// and <see cref="FrameBackground"/> are read from <c>Framefile.json</c> when cheap.</para>
///
/// <para><see cref="Captured"/> enumerates the actual PNGs found on disk, grouped by
/// locale. When there is no Snapfile, <see cref="Languages"/> is derived from the
/// locales discovered among the captured screenshots so the UI still shows real shots.</para>
/// </summary>
public sealed record SnapshotConfig(
    bool HasSnapfile,
    IReadOnlyList<string> Devices,
    IReadOnlyList<string> Languages,
    string? Scheme,
    string? LaunchArguments,
    bool FrameitEnabled,
    string? FrameTitle,
    string? FrameBackground,
    string? OutputDirectory,
    IReadOnlyList<ScreenshotGroup> Captured)
{
    /// <summary>An empty config for projects without an iOS snapshot setup on disk.</summary>
    public static SnapshotConfig None { get; } = new(
        HasSnapfile: false,
        Devices: Array.Empty<string>(),
        Languages: Array.Empty<string>(),
        Scheme: null,
        LaunchArguments: null,
        FrameitEnabled: false,
        FrameTitle: null,
        FrameBackground: null,
        OutputDirectory: null,
        Captured: Array.Empty<ScreenshotGroup>());

    /// <summary>Total number of captured PNGs across all locale groups.</summary>
    public int CapturedCount => Captured.Sum(g => g.Paths.Count);
}

/// <summary>
/// Pure, file-based reader for a project's iOS fastlane <c>snapshot</c> config and
/// the screenshots already captured on disk. Total — never throws; returns
/// <see cref="SnapshotConfig.None"/> when the project has no iOS fastlane dir.
///
/// Reads (all under <c>ios/fastlane</c>):
/// <list type="bullet">
/// <item><c>Snapfile</c> — <c>devices([...])</c>, <c>languages([...])</c>,
/// <c>scheme("...")</c>, <c>launch_arguments([...])</c>/<c>launch_arguments("...")</c>,
/// <c>output_directory("...")</c>. Arrays may span multiple lines.</item>
/// <item><c>Framefile</c> / <c>Framefile.json</c> — presence enables frameit; the
/// title/background are read from <c>Framefile.json</c> when present.</item>
/// <item>Captured PNGs — from the Snapfile <c>output_directory</c> (resolved relative
/// to <c>ios/fastlane</c> then the project) when set, else
/// <c>ios/fastlane/screenshots/&lt;locale&gt;/*.png</c> and
/// <c>ios/fastlane/screenshots/*.png</c>. Grouped by locale folder name.</item>
/// </list>
/// </summary>
public static partial class SnapshotConfigReader
{
    [GeneratedRegex("""scheme\s*\(\s*["'](?<v>[^"']+)["']""", RegexOptions.IgnoreCase)]
    private static partial Regex SchemeRegex();

    [GeneratedRegex("""output_directory\s*\(\s*["'](?<v>[^"']+)["']""", RegexOptions.IgnoreCase)]
    private static partial Regex OutputDirRegex();

    // devices([ ... ]) / languages([ ... ]) — body captured (may span lines).
    [GeneratedRegex(
        """devices\s*\(\s*\[(?<v>[^\]]*)\]""",
        RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex DevicesRegex();

    [GeneratedRegex(
        """languages\s*\(\s*\[(?<v>[^\]]*)\]""",
        RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex LanguagesRegex();

    // launch_arguments([ ... ]) — array form (body captured, may span lines).
    [GeneratedRegex(
        """launch_arguments\s*\(\s*\[(?<v>[^\]]*)\]""",
        RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex LaunchArgsArrayRegex();

    // launch_arguments("...") — single-string form.
    [GeneratedRegex(
        """launch_arguments\s*\(\s*["'](?<v>[^"']*)["']\s*\)""",
        RegexOptions.IgnoreCase)]
    private static partial Regex LaunchArgsStringRegex();

    // "a", 'b', "c" → individual quoted tokens.
    [GeneratedRegex("""["'](?<v>[^"']*)["']""")]
    private static partial Regex QuotedTokenRegex();

    // Framefile.json: "title": "...", "background": "..."
    [GeneratedRegex(""""title"\s*:\s*"(?<v>[^"]*)"""", RegexOptions.IgnoreCase)]
    private static partial Regex JsonTitleRegex();

    [GeneratedRegex(""""background"\s*:\s*"(?<v>[^"]*)"""", RegexOptions.IgnoreCase)]
    private static partial Regex JsonBackgroundRegex();

    /// <summary>
    /// Reads the project's iOS snapshot config + captured screenshots from disk.
    /// Returns <see cref="SnapshotConfig.None"/> when the project has no iOS fastlane dir.
    /// </summary>
    public static SnapshotConfig Read(Project project)
    {
        if (project.IosFastlaneDir is not { } iosFl)
        {
            return SnapshotConfig.None;
        }

        var snapfilePath = Path.Combine(iosFl, "Snapfile");
        var snapfile = ReadTextOrNull(snapfilePath);
        var hasSnapfile = snapfile is not null;

        var devices = snapfile is null
            ? Array.Empty<string>()
            : ExtractList(DevicesRegex(), snapfile);
        var snapfileLanguages = snapfile is null
            ? Array.Empty<string>()
            : ExtractList(LanguagesRegex(), snapfile);
        var scheme = snapfile is null ? null : FindFirst(SchemeRegex(), snapfile);
        var launchArgs = snapfile is null ? null : ExtractLaunchArguments(snapfile);
        var outputDir = snapfile is null ? null : FindFirst(OutputDirRegex(), snapfile);

        var (frameitEnabled, frameTitle, frameBackground) = ReadFrameit(iosFl);

        var captured = EnumerateCaptured(project, iosFl, outputDir);

        // No Snapfile (or no explicit languages): derive locales from disk so the UI
        // still shows the real captured shots and their locales.
        IReadOnlyList<string> languages = snapfileLanguages.Count > 0
            ? snapfileLanguages
            : captured.Select(g => g.Locale)
                .Where(l => l.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(l => l, StringComparer.OrdinalIgnoreCase)
                .ToList();

        return new SnapshotConfig(
            HasSnapfile: hasSnapfile,
            Devices: devices,
            Languages: languages,
            Scheme: scheme,
            LaunchArguments: launchArgs,
            FrameitEnabled: frameitEnabled,
            FrameTitle: frameTitle,
            FrameBackground: frameBackground,
            OutputDirectory: outputDir,
            Captured: captured);
    }

    // ---- Snapfile parsing ----------------------------------------------------

    static IReadOnlyList<string> ExtractList(Regex arrayRegex, string text)
    {
        var m = arrayRegex.Match(text);
        if (!m.Success)
        {
            return Array.Empty<string>();
        }

        return Tokens(m.Groups["v"].Value);
    }

    static string? ExtractLaunchArguments(string text)
    {
        var arr = LaunchArgsArrayRegex().Match(text);
        if (arr.Success)
        {
            var tokens = Tokens(arr.Groups["v"].Value);
            return tokens.Count == 0 ? null : string.Join(" ", tokens);
        }

        var str = LaunchArgsStringRegex().Match(text);
        if (str.Success)
        {
            var v = str.Groups["v"].Value.Trim();
            return v.Length == 0 ? null : v;
        }

        return null;
    }

    static List<string> Tokens(string body)
    {
        var result = new List<string>();
        foreach (Match m in QuotedTokenRegex().Matches(body))
        {
            var v = m.Groups["v"].Value.Trim();
            if (v.Length > 0)
            {
                result.Add(v);
            }
        }
        return result;
    }

    static string? FindFirst(Regex regex, string text)
    {
        var m = regex.Match(text);
        if (!m.Success)
        {
            return null;
        }

        var v = m.Groups["v"].Value.Trim();
        return v.Length == 0 ? null : v;
    }

    // ---- frameit -------------------------------------------------------------

    static (bool Enabled, string? Title, string? Background) ReadFrameit(string iosFl)
    {
        var jsonPath = Path.Combine(iosFl, "Framefile.json");
        var json = ReadTextOrNull(jsonPath);
        if (json is not null)
        {
            var title = FindFirst(JsonTitleRegex(), json);
            var background = FindFirst(JsonBackgroundRegex(), json);
            return (true, title, background);
        }

        var framefile = Path.Combine(iosFl, "Framefile");
        if (File.Exists(framefile))
        {
            return (true, null, null);
        }

        return (false, null, null);
    }

    // ---- captured screenshots ------------------------------------------------

    static IReadOnlyList<ScreenshotGroup> EnumerateCaptured(
        Project project, string iosFl, string? outputDir)
    {
        var root = ResolveScreenshotsRoot(project, iosFl, outputDir);
        if (root is null || !Directory.Exists(root))
        {
            return Array.Empty<ScreenshotGroup>();
        }

        var groups = new List<ScreenshotGroup>();

        // Per-locale sub-folders: <root>/<locale>/*.png
        IEnumerable<string> localeDirs;
        try
        {
            localeDirs = Directory.GetDirectories(root)
                .OrderBy(d => Path.GetFileName(d), StringComparer.OrdinalIgnoreCase);
        }
        catch (IOException)
        {
            localeDirs = Array.Empty<string>();
        }
        catch (UnauthorizedAccessException)
        {
            localeDirs = Array.Empty<string>();
        }

        foreach (var dir in localeDirs)
        {
            var pngs = PngsIn(dir);
            if (pngs.Count > 0)
            {
                groups.Add(new ScreenshotGroup(Path.GetFileName(dir), pngs));
            }
        }

        // Loose PNGs directly under the root (no locale folder).
        var loose = PngsIn(root);
        if (loose.Count > 0)
        {
            groups.Add(new ScreenshotGroup(string.Empty, loose));
        }

        return groups;
    }

    static string? ResolveScreenshotsRoot(Project project, string iosFl, string? outputDir)
    {
        if (!string.IsNullOrWhiteSpace(outputDir))
        {
            if (Path.IsPathRooted(outputDir))
            {
                return outputDir;
            }

            var relToFastlane = Path.Combine(iosFl, outputDir);
            if (Directory.Exists(relToFastlane))
            {
                return relToFastlane;
            }

            var relToProject = Path.Combine(project.Path, outputDir);
            if (Directory.Exists(relToProject))
            {
                return relToProject;
            }

            // Default to the fastlane-relative path even if it doesn't exist yet.
            return relToFastlane;
        }

        return Path.Combine(iosFl, "screenshots");
    }

    static IReadOnlyList<string> PngsIn(string dir)
    {
        if (!Directory.Exists(dir))
        {
            return Array.Empty<string>();
        }

        try
        {
            return Directory.GetFiles(dir, "*.png")
                .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
        catch (IOException)
        {
            return Array.Empty<string>();
        }
        catch (UnauthorizedAccessException)
        {
            return Array.Empty<string>();
        }
    }

    // ---- io helpers ----------------------------------------------------------

    static string? ReadTextOrNull(string path)
    {
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            return File.ReadAllText(path);
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }
}
