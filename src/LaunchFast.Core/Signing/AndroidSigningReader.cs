using System.Text.RegularExpressions;
using LaunchFast.Core.Models;

namespace LaunchFast.Core.Signing;

/// <summary>
/// The Android release signing configuration discovered on disk for a project:
/// the values declared in the gradle <c>signingConfigs { release { ... } }</c>
/// block, whether that config is applied to the release build type, and the
/// presence + declared key names of an <c>android/key.properties</c> file.
///
/// Values may be literals (e.g. <c>"app/upload-keystore.jks"</c>) or the name of
/// the <c>key.properties</c> entry they reference (e.g. when gradle uses
/// <c>keystoreProperties['storeFile']</c>). Secret <b>values</b> are never read —
/// only the key names declared in <c>key.properties</c> are surfaced.
/// </summary>
public sealed record AndroidSigningInfo(
    bool HasAndroid,
    string? StoreFile,
    string? StoreType,
    string? KeyAlias,
    bool ReleaseSigningApplied,
    bool HasKeyProperties,
    IReadOnlyList<string> KeyPropertyNames)
{
    /// <summary>An empty result for projects without an Android module on disk.</summary>
    public static AndroidSigningInfo None { get; } =
        new(false, null, null, null, false, false, Array.Empty<string>());
}

/// <summary>
/// Pure, file-based reader for a project's Android release signing setup. Reads
/// <c>android/app/build.gradle</c> (or <c>build.gradle.kts</c>) and an optional
/// <c>android/key.properties</c>. Total — never throws; returns
/// <see cref="AndroidSigningInfo.None"/> when no Android module is present.
/// </summary>
public static partial class AndroidSigningReader
{
    // storeFile file("…") / storeFile = file("…") / storeFile "…" (Groovy & Kotlin).
    [GeneratedRegex(
        """storeFile\s*=?\s*(?:file\s*\(\s*)?["'](?<v>[^"']+)["']""",
        RegexOptions.IgnoreCase)]
    private static partial Regex StoreFileLiteralRegex();

    // storeType "PKCS12" / storeType = "PKCS12".
    [GeneratedRegex("""storeType\s*=?\s*["'](?<v>[^"']+)["']""", RegexOptions.IgnoreCase)]
    private static partial Regex StoreTypeLiteralRegex();

    // keyAlias "upload" / keyAlias = "upload".
    [GeneratedRegex("""keyAlias\s*=?\s*["'](?<v>[^"']+)["']""", RegexOptions.IgnoreCase)]
    private static partial Regex KeyAliasLiteralRegex();

    // A property reference: keystoreProperties['storeFile'] / keystoreProperties["storeFile"]
    // / keystoreProperties.getProperty('storeFile').
    [GeneratedRegex(
        """(?:keystoreProperties|keyProperties|props)\s*(?:\[\s*|\.getProperty\s*\(\s*)["'](?<k>[^"']+)["']""",
        RegexOptions.IgnoreCase)]
    private static partial Regex PropertyRefRegex();

    /// <summary>
    /// Reads the project's Android release signing info from disk. Returns
    /// <see cref="AndroidSigningInfo.None"/> when the project has no Android module.
    /// </summary>
    public static AndroidSigningInfo Read(Project project)
    {
        var androidRoot = AndroidRoot(project);
        if (androidRoot is null) return AndroidSigningInfo.None;

        var gradle = ReadGradle(androidRoot);
        var release = gradle is null ? null : ExtractReleaseSigningBlock(gradle);

        var (storeFile, storeType, keyAlias) = release is null
            ? (null, null, null)
            : ((string?, string?, string?))(
                FindValue(release, StoreFileLiteralRegex(), "storeFile"),
                FindValue(release, StoreTypeLiteralRegex(), "storeType"),
                FindValue(release, KeyAliasLiteralRegex(), "keyAlias"));

        var releaseApplied = gradle is not null && ReleaseSigningApplied(gradle);

        var (hasKeyProps, keyPropNames) = ReadKeyProperties(androidRoot);

        return new AndroidSigningInfo(
            HasAndroid: true,
            StoreFile: storeFile,
            StoreType: storeType,
            KeyAlias: keyAlias,
            ReleaseSigningApplied: releaseApplied,
            HasKeyProperties: hasKeyProps,
            KeyPropertyNames: keyPropNames);
    }

    /// <summary>
    /// The project's <c>android/</c> directory (parent of the fastlane dir), or null
    /// when the project has no Android platform / the directory is absent.
    /// </summary>
    static string? AndroidRoot(Project project)
    {
        if (project.AndroidFastlaneDir is not null)
        {
            var parent = Directory.GetParent(project.AndroidFastlaneDir)?.FullName;
            if (parent is not null && Directory.Exists(parent)) return parent;
        }

        var guess = Path.Combine(project.Path, "android");
        return Directory.Exists(guess) ? guess : null;
    }

    static string? ReadGradle(string androidRoot)
    {
        foreach (var rel in new[]
                 {
                     Path.Combine("app", "build.gradle"),
                     Path.Combine("app", "build.gradle.kts"),
                 })
        {
            var path = Path.Combine(androidRoot, rel);
            if (File.Exists(path))
            {
                try { return File.ReadAllText(path); }
                catch { return null; }
            }
        }

        return null;
    }

    /// <summary>
    /// Extracts the body of the <c>release { ... }</c> block nested inside
    /// <c>signingConfigs { ... }</c>, brace-balanced. Returns null when absent.
    /// </summary>
    static string? ExtractReleaseSigningBlock(string gradle)
    {
        var sc = BlockBody(gradle, "signingConfigs");
        if (sc is null) return null;
        return BlockBody(sc, "release");
    }

    /// <summary>
    /// Returns the brace-balanced body of the first <c>name { ... }</c> block in
    /// <paramref name="text"/> (Groovy or Kotlin <c>name { ... }</c> / <c>create("name") { ... }</c>).
    /// </summary>
    static string? BlockBody(string text, string name)
    {
        // Match `name {`, or Kotlin `create("name") {` / `getByName("name") {`.
        var esc = Regex.Escape(name);
        var pattern =
            @"(?:\b" + esc + @"\b|(?:create|getByName|named)\s*\(\s*[""']" + esc + @"[""']\s*\))\s*\{";
        var header = new Regex(pattern, RegexOptions.IgnoreCase);

        var m = header.Match(text);
        if (!m.Success) return null;

        var start = m.Index + m.Length - 1; // position of the opening brace
        var depth = 0;
        for (var i = start; i < text.Length; i++)
        {
            if (text[i] == '{') depth++;
            else if (text[i] == '}')
            {
                depth--;
                if (depth == 0) return text.Substring(start + 1, i - start - 1);
            }
        }

        return null; // unbalanced — treat as absent
    }

    /// <summary>
    /// Whether <c>signingConfig signingConfigs.release</c> (or the Kotlin
    /// <c>signingConfig = signingConfigs.getByName("release")</c>) appears in a
    /// <c>buildTypes { release { ... } }</c> block.
    /// </summary>
    static bool ReleaseSigningApplied(string gradle)
    {
        var buildTypes = BlockBody(gradle, "buildTypes");
        var releaseBody = buildTypes is null ? null : BlockBody(buildTypes, "release");
        if (releaseBody is null) return false;

        return Regex.IsMatch(
            releaseBody,
            """signingConfig\s*=?\s*signingConfigs(?:\.release|\.getByName\s*\(\s*["']release["']\s*\)|\s*\.\s*release)""",
            RegexOptions.IgnoreCase);
    }

    /// <summary>
    /// The literal value for a field, or — when the field references a
    /// <c>key.properties</c> entry — the referenced property key name.
    /// </summary>
    static string? FindValue(string block, Regex literal, string fieldName)
    {
        // First, the line(s) mentioning this field — so a literal on the same line
        // (or a property-ref) is matched against the right field, not another.
        var lineRegex = new Regex(
            @"^[^\n]*\b" + Regex.Escape(fieldName) + @"\b[^\n]*$",
            RegexOptions.IgnoreCase | RegexOptions.Multiline);

        foreach (Match line in lineRegex.Matches(block))
        {
            var pr = PropertyRefRegex().Match(line.Value);
            if (pr.Success) return pr.Groups["k"].Value;

            var lit = literal.Match(line.Value);
            if (lit.Success) return lit.Groups["v"].Value;
        }

        return null;
    }

    static (bool Present, IReadOnlyList<string> Names) ReadKeyProperties(string androidRoot)
    {
        var path = Path.Combine(androidRoot, "key.properties");
        if (!File.Exists(path)) return (false, Array.Empty<string>());

        string text;
        try { text = File.ReadAllText(path); }
        catch { return (true, Array.Empty<string>()); }

        var names = new List<string>();
        foreach (var rawLine in text.Split('\n'))
        {
            var line = rawLine.Trim();
            if (line.Length == 0 || line.StartsWith('#') || line.StartsWith('!')) continue;

            var eq = line.IndexOf('=');
            if (eq <= 0) continue;

            var key = line[..eq].Trim();
            if (key.Length > 0 && !names.Contains(key, StringComparer.Ordinal))
                names.Add(key);
        }

        return (true, names);
    }
}
