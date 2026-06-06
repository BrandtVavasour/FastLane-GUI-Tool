using System.Text.RegularExpressions;
using LaunchFast.Core.Models;

namespace LaunchFast.Core.Stores;

/// <summary>
/// Resolves the best human-facing app name for a platform from a project's on-disk
/// native config, falling back to a prettified pubspec name. Used as a display
/// fallback when the store metadata (deliver <c>name.txt</c> / supply
/// <c>title.txt</c>) does not carry the name on disk.
///
/// <para>Resolution order:</para>
/// <list type="bullet">
/// <item><b>iOS:</b> <c>ios/Runner/Info.plist</c> — <c>CFBundleDisplayName</c> then
/// <c>CFBundleName</c> (ignoring empty values and build variables like
/// <c>$(PRODUCT_NAME)</c>).</item>
/// <item><b>Android:</b> <c>android/app/src/main/AndroidManifest.xml</c>
/// <c>android:label</c> — a literal value directly, or a <c>@string/foo</c> reference
/// resolved from <c>android/app/src/main/res/values/strings.xml</c>.</item>
/// <item><b>Fallback (both):</b> the prettified pubspec <c>name:</c> (snake/kebab-case
/// to Title Case).</item>
/// </list>
/// Total — never throws; any missing file or parse error falls through.
/// </summary>
public static partial class AppDisplayName
{
    /// <summary>
    /// The best human-facing app name for <paramref name="platform"/>, or null when
    /// nothing usable is available on disk. Never throws.
    /// </summary>
    public static string? Read(Project project, Platform platform)
    {
        var root = project.Path;

        var native = platform == Platform.Ios
            ? ReadIos(root)
            : ReadAndroid(root);

        return native ?? PrettyPubspecName(root);
    }

    // ---- iOS -----------------------------------------------------------------

    static string? ReadIos(string root)
    {
        var plist = Path.Combine(root, "ios", "Runner", "Info.plist");
        var text = ReadTextOrNull(plist);
        if (text is null)
        {
            return null;
        }

        return PlistString(text, "CFBundleDisplayName")
            ?? PlistString(text, "CFBundleName");
    }

    static string? PlistString(string plist, string key)
    {
        try
        {
            var m = Regex.Match(
                plist,
                $@"<key>{Regex.Escape(key)}</key>\s*<string>([^<]*)</string>");
            if (!m.Success)
            {
                return null;
            }

            return Clean(m.Groups[1].Value);
        }
        catch (RegexMatchTimeoutException)
        {
            return null;
        }
    }

    // ---- Android -------------------------------------------------------------

    static string? ReadAndroid(string root)
    {
        var mainDir = Path.Combine(root, "android", "app", "src", "main");
        var manifest = ReadTextOrNull(Path.Combine(mainDir, "AndroidManifest.xml"));
        if (manifest is null)
        {
            return null;
        }

        string? label;
        try
        {
            // Prefer the android:label on the <application> element: find the
            // <application tag first, then look for the next android:label="..." within
            // that element's opening tag. This avoids picking up activity-level labels
            // that may appear anywhere else in the manifest. Fall back to the first
            // match anywhere only when no application-level label is found.
            var appTagMatch = ApplicationTagRegex().Match(manifest);
            Match m;
            if (appTagMatch.Success)
            {
                // Search for android:label within the application opening tag text.
                m = AndroidLabelRegex().Match(appTagMatch.Value);
                if (!m.Success)
                {
                    // Application element found but no label attribute on it; fall back
                    // to first match in full manifest (activity-level).
                    m = AndroidLabelRegex().Match(manifest);
                }
            }
            else
            {
                m = AndroidLabelRegex().Match(manifest);
            }

            label = m.Success ? m.Groups["v"].Value : null;
        }
        catch (RegexMatchTimeoutException)
        {
            return null;
        }

        if (string.IsNullOrWhiteSpace(label))
        {
            return null;
        }

        label = label.Trim();
        if (!label.StartsWith('@'))
        {
            return Clean(label);
        }

        // @string/foo reference → resolve from strings.xml.
        var refName = label[1..];
        var slash = refName.IndexOf('/');
        if (slash >= 0)
        {
            refName = refName[(slash + 1)..];
        }

        var strings = ReadTextOrNull(Path.Combine(mainDir, "res", "values", "strings.xml"));
        if (strings is null)
        {
            return null;
        }

        try
        {
            var m = Regex.Match(
                strings,
                $@"<string\s+name=""{Regex.Escape(refName)}""\s*>([^<]*)</string>");
            return m.Success ? Clean(m.Groups[1].Value) : null;
        }
        catch (RegexMatchTimeoutException)
        {
            return null;
        }
    }

    [GeneratedRegex("android:label\\s*=\\s*\"(?<v>[^\"]*)\"")]
    private static partial Regex AndroidLabelRegex();

    /// <summary>
    /// Matches the opening tag of the &lt;application&gt; element (up to the first &gt;
    /// that closes it), so we can look for android:label within that tag only.
    /// </summary>
    [GeneratedRegex("<application\\b[^>]*>", RegexOptions.Singleline)]
    private static partial Regex ApplicationTagRegex();

    // ---- pubspec fallback ----------------------------------------------------

    static string? PrettyPubspecName(string root)
    {
        var pubspec = ReadTextOrNull(Path.Combine(root, "pubspec.yaml"));
        if (pubspec is null)
        {
            return null;
        }

        string? name;
        try
        {
            var m = Regex.Match(pubspec, @"^name:\s*(\S+)", RegexOptions.Multiline);
            name = m.Success ? m.Groups[1].Value : null;
        }
        catch (RegexMatchTimeoutException)
        {
            return null;
        }

        return Prettify(name);
    }

    /// <summary>
    /// Turns a snake_case / kebab-case package name into Title Case
    /// (e.g. <c>vending_machine_tracker</c> → <c>Vending Machine Tracker</c>).
    /// Null/empty in → null.
    /// </summary>
    internal static string? Prettify(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        var words = name
            .Replace('_', ' ')
            .Replace('-', ' ')
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Select(TitleWord);

        var pretty = string.Join(' ', words);
        return pretty.Length == 0 ? null : pretty;
    }

    static string TitleWord(string word) =>
        word.Length == 0
            ? word
            : char.ToUpperInvariant(word[0]) + word[1..];

    // ---- helpers -------------------------------------------------------------

    static string? Clean(string? value)
    {
        if (value is null)
        {
            return null;
        }

        var trimmed = value.Trim();
        if (trimmed.Length == 0 || trimmed.StartsWith("$(", StringComparison.Ordinal))
        {
            return null;
        }

        return trimmed;
    }

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
