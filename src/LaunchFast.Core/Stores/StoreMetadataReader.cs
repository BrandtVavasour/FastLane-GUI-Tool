using LaunchFast.Core.Models;

namespace LaunchFast.Core.Stores;

/// <summary>
/// Per-platform character limits enforced by App Store Connect (deliver) and
/// Google Play (supply) for the listing fields we surface. Used by the UI to draw
/// "N / max" counters and flag over-limit text.
/// </summary>
public static class StoreFieldLimits
{
    // App Store Connect (iOS deliver)
    public const int AppStoreName = 30;
    public const int AppStoreSubtitle = 30;
    public const int AppStorePromotionalText = 170;
    public const int AppStoreKeywords = 100;
    public const int AppStoreDescription = 4000;
    public const int AppStoreReleaseNotes = 4000;

    // Google Play (Android supply)
    public const int PlayTitle = 30;
    public const int PlayShortDescription = 80;
    public const int PlayFullDescription = 4000;
    public const int PlayWhatsNew = 500;
}

/// <summary>
/// A single store-listing locale's text content plus its screenshot paths, as read
/// from a project's fastlane metadata tree. Fields are unified across platforms:
/// <list type="bullet">
/// <item><see cref="Subtitle"/>, <see cref="PromotionalText"/>, <see cref="Keywords"/>
/// are iOS-only (null on Android).</item>
/// <item><see cref="ShortDescription"/> is Android-only (null on iOS).</item>
/// <item><see cref="Name"/> maps to iOS <c>name.txt</c> / Android <c>title.txt</c>;
/// <see cref="FullDescription"/> to iOS <c>description.txt</c> / Android
/// <c>full_description.txt</c>.</item>
/// </list>
/// Any field is null when its backing file is absent or empty.
/// </summary>
public sealed record StoreListing(
    Platform Platform,
    string Locale,
    string? Name,
    string? Subtitle,
    string? ShortDescription,
    string? PromotionalText,
    string? Keywords,
    string? FullDescription,
    string? ReleaseNotes,
    string? MarketingUrl,
    string? SupportUrl,
    string? PrivacyUrl,
    string? VideoUrl,
    IReadOnlyList<string> ScreenshotPaths)
{
    /// <summary>An empty listing (all text null, no screenshots) for a locale.</summary>
    public static StoreListing Empty(Platform platform, string locale) => new(
        platform, locale,
        Name: null, Subtitle: null, ShortDescription: null, PromotionalText: null,
        Keywords: null, FullDescription: null, ReleaseNotes: null,
        MarketingUrl: null, SupportUrl: null, PrivacyUrl: null, VideoUrl: null,
        ScreenshotPaths: Array.Empty<string>());

    /// <summary>True when every text field is null and no screenshots were found.</summary>
    public bool IsEmpty =>
        Name is null && Subtitle is null && ShortDescription is null &&
        PromotionalText is null && Keywords is null && FullDescription is null &&
        ReleaseNotes is null && MarketingUrl is null && SupportUrl is null &&
        PrivacyUrl is null && VideoUrl is null && ScreenshotPaths.Count == 0;
}

/// <summary>
/// Pure, file-based reader for fastlane store metadata and screenshots. Total —
/// never throws; any missing file or directory yields null/empty rather than an
/// error. Reads:
/// <list type="bullet">
/// <item><b>iOS (deliver)</b>: <c>ios/fastlane/metadata/&lt;locale&gt;/*.txt</c> and
/// screenshots under <c>ios/fastlane/screenshots/&lt;locale&gt;/*.png</c> (falling
/// back to PNGs alongside the metadata locale folder).</item>
/// <item><b>Android (supply)</b>:
/// <c>android/fastlane/metadata/android/&lt;locale&gt;/*.txt</c>, changelogs under
/// <c>changelogs/&lt;versionCode&gt;.txt</c>, and screenshots under
/// <c>images/phoneScreenshots/*.png</c> (plus <c>tenInch</c>/<c>sevenInch</c>/
/// <c>tablet</c> variants).</item>
/// </list>
/// </summary>
public static class StoreMetadataReader
{
    /// <summary>Android screenshot sub-folders under a locale's <c>images/</c> dir.</summary>
    static readonly string[] AndroidScreenshotFolders =
    {
        "phoneScreenshots",
        "sevenInchScreenshots",
        "tenInchScreenshots",
        "tvScreenshots",
        "wearScreenshots",
    };

    /// <summary>
    /// The on-disk locale folder names for a platform's metadata tree, sorted. Empty
    /// when the project has no fastlane dir for that platform or no metadata exists.
    /// </summary>
    public static IReadOnlyList<string> Locales(Project project, Platform platform)
    {
        var root = MetadataRoot(project, platform);
        if (root is null || !Directory.Exists(root))
        {
            return Array.Empty<string>();
        }

        try
        {
            return Directory.GetDirectories(root)
                .Select(Path.GetFileName)
                .Where(n => !string.IsNullOrEmpty(n))
                .Select(n => n!)
                .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
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

    /// <summary>
    /// Reads the listing text + screenshots for a single locale. Returns an empty
    /// listing when the locale folder is absent. Never throws.
    /// </summary>
    public static StoreListing ReadListing(Project project, Platform platform, string locale)
    {
        var root = MetadataRoot(project, platform);
        if (root is null)
        {
            return StoreListing.Empty(platform, locale);
        }

        var localeDir = Path.Combine(root, locale);

        return platform == Platform.Ios
            ? ReadIos(project, locale, localeDir)
            : ReadAndroid(locale, localeDir);
    }

    static StoreListing ReadIos(Project project, string locale, string localeDir) => new(
        Platform.Ios, locale,
        Name: ReadText(localeDir, "name.txt"),
        Subtitle: ReadText(localeDir, "subtitle.txt"),
        ShortDescription: null,
        PromotionalText: ReadText(localeDir, "promotional_text.txt"),
        Keywords: ReadText(localeDir, "keywords.txt"),
        FullDescription: ReadText(localeDir, "description.txt"),
        ReleaseNotes: ReadText(localeDir, "release_notes.txt"),
        MarketingUrl: ReadText(localeDir, "marketing_url.txt"),
        SupportUrl: ReadText(localeDir, "support_url.txt"),
        PrivacyUrl: ReadText(localeDir, "privacy_url.txt"),
        VideoUrl: null,
        ScreenshotPaths: IosScreenshots(project, locale, localeDir));

    static StoreListing ReadAndroid(string locale, string localeDir) => new(
        Platform.Android, locale,
        Name: ReadText(localeDir, "title.txt"),
        Subtitle: null,
        ShortDescription: ReadText(localeDir, "short_description.txt"),
        PromotionalText: null,
        Keywords: null,
        FullDescription: ReadText(localeDir, "full_description.txt"),
        ReleaseNotes: LatestChangelog(localeDir),
        MarketingUrl: null,
        SupportUrl: null,
        PrivacyUrl: null,
        VideoUrl: ReadText(localeDir, "video.txt"),
        ScreenshotPaths: AndroidScreenshots(localeDir));

    // ---- screenshots ---------------------------------------------------------

    static IReadOnlyList<string> IosScreenshots(Project project, string locale, string localeDir)
    {
        var paths = new List<string>();

        // Preferred location: ios/fastlane/screenshots/<locale>/*.png
        if (project.IosFastlaneDir is { } iosFl)
        {
            paths.AddRange(PngsIn(Path.Combine(iosFl, "screenshots", locale)));
        }

        // Fallback: PNGs sitting alongside the metadata locale folder.
        paths.AddRange(PngsIn(localeDir));

        return Dedupe(paths);
    }

    static IReadOnlyList<string> AndroidScreenshots(string localeDir)
    {
        var imagesDir = Path.Combine(localeDir, "images");
        var paths = new List<string>();
        foreach (var folder in AndroidScreenshotFolders)
        {
            paths.AddRange(PngsIn(Path.Combine(imagesDir, folder)));
        }
        return Dedupe(paths);
    }

    static IEnumerable<string> PngsIn(string dir)
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

    static IReadOnlyList<string> Dedupe(List<string> paths)
    {
        if (paths.Count == 0)
        {
            return Array.Empty<string>();
        }

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result = new List<string>(paths.Count);
        foreach (var p in paths)
        {
            if (seen.Add(p))
            {
                result.Add(p);
            }
        }
        return result;
    }

    // ---- changelogs ----------------------------------------------------------

    /// <summary>
    /// The newest Android changelog (<c>changelogs/&lt;versionCode&gt;.txt</c>) by
    /// numeric version code, falling back to the lexically-greatest filename. Null
    /// when no changelog exists.
    /// </summary>
    static string? LatestChangelog(string localeDir)
    {
        var dir = Path.Combine(localeDir, "changelogs");
        if (!Directory.Exists(dir))
        {
            return null;
        }

        string[] files;
        try
        {
            files = Directory.GetFiles(dir, "*.txt");
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }

        if (files.Length == 0)
        {
            return null;
        }

        var best = files
            .OrderByDescending(f => ParseVersionCode(f))
            .ThenByDescending(f => Path.GetFileName(f), StringComparer.OrdinalIgnoreCase)
            .First();

        return NullIfBlank(SafeReadAllText(best));
    }

    static long ParseVersionCode(string file)
    {
        var name = Path.GetFileNameWithoutExtension(file);
        return long.TryParse(name, out var code) ? code : -1;
    }

    // ---- text helpers --------------------------------------------------------

    static string? ReadText(string dir, string fileName) =>
        NullIfBlank(SafeReadAllText(Path.Combine(dir, fileName)));

    static string? SafeReadAllText(string path)
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

    static string? NullIfBlank(string? text)
    {
        if (text is null)
        {
            return null;
        }

        var trimmed = text.Trim();
        return trimmed.Length == 0 ? null : trimmed;
    }

    // ---- roots ---------------------------------------------------------------

    /// <summary>
    /// The metadata root directory for a platform, or null when the project has no
    /// fastlane dir for it. iOS: <c>&lt;iosFastlane&gt;/metadata</c>; Android:
    /// <c>&lt;androidFastlane&gt;/metadata/android</c>.
    /// </summary>
    static string? MetadataRoot(Project project, Platform platform) => platform switch
    {
        Platform.Ios => project.IosFastlaneDir is { } ios
            ? Path.Combine(ios, "metadata")
            : null,
        Platform.Android => project.AndroidFastlaneDir is { } android
            ? Path.Combine(android, "metadata", "android")
            : null,
        _ => null,
    };
}
