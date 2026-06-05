using System.Text.RegularExpressions;

namespace LaunchFast.Core.Stores;

/// <summary>
/// Store identifiers discovered from a project's fastlane Appfiles: the iOS bundle
/// id and the Android package name. Either may be null when the corresponding
/// Appfile is absent or doesn't declare it.
/// </summary>
public sealed record StoreIdentifiers(string? IosBundleId, string? AndroidPackageName);

/// <summary>
/// Pure parser for the handful of fastlane Appfile directives we care about.
/// Never throws; returns null when a directive is absent.
/// </summary>
public static partial class AppfileReader
{
    [GeneratedRegex("""app_identifier\(\s*['"](?<v>[^'"]+)['"]""")]
    private static partial Regex AppIdentifierRegex();

    [GeneratedRegex("""package_name\(\s*['"](?<v>[^'"]+)['"]""")]
    private static partial Regex PackageNameRegex();

    // Captures every quoted token inside a json_key_file(...) call so we can
    // prefer the literal path over an ENV["..."] lookup key.
    [GeneratedRegex("""json_key_file\((?<args>[^)]*)\)""")]
    private static partial Regex JsonKeyFileCallRegex();

    [GeneratedRegex("""['"](?<v>[^'"]+)['"]""")]
    private static partial Regex QuotedTokenRegex();

    /// <summary><c>app_identifier("x")</c> → <c>x</c>, else null.</summary>
    public static string? AppIdentifier(string appfileText) =>
        Match(AppIdentifierRegex(), appfileText);

    /// <summary><c>package_name("x")</c> → <c>x</c>, else null.</summary>
    public static string? PackageName(string appfileText) =>
        Match(PackageNameRegex(), appfileText);

    /// <summary>
    /// <c>json_key_file("x")</c> → <c>x</c>. When the directive uses
    /// <c>ENV["KEY"] || "/literal/path"</c>, returns the literal path (the token
    /// that looks like a filesystem path) rather than the ENV key.
    /// </summary>
    public static string? JsonKeyFile(string appfileText)
    {
        var call = JsonKeyFileCallRegex().Match(appfileText);
        if (!call.Success)
        {
            return null;
        }

        var args = call.Groups["args"].Value;
        var tokens = QuotedTokenRegex().Matches(args)
            .Select(m => m.Groups["v"].Value)
            .ToList();

        if (tokens.Count == 0)
        {
            return null;
        }

        // Prefer a token that looks like a path (json key files always do).
        var path = tokens.FirstOrDefault(t =>
            t.Contains('/') || t.EndsWith(".json", StringComparison.OrdinalIgnoreCase));

        return path ?? tokens[0];
    }

    private static string? Match(Regex regex, string text)
    {
        var m = regex.Match(text);
        return m.Success ? m.Groups["v"].Value : null;
    }
}
