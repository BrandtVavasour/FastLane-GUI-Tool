using LaunchFast.Core.Models;

namespace LaunchFast.Core.Stores;

/// <summary>
/// File-based writer for fastlane store metadata — the persistence counterpart to
/// <see cref="StoreMetadataReader"/>. Writes the exact deliver (iOS) / supply
/// (Android) <c>.txt</c> files the reader reads, so a write→read round-trip is
/// lossless.
///
/// <para><b>Contract:</b> unlike the (total) reader, the writer is NOT total — it
/// surfaces failures. Any I/O problem (missing permissions, path collision, disk
/// full) propagates as an <see cref="IOException"/> (or
/// <see cref="UnauthorizedAccessException"/>) so callers can show a Save error
/// rather than silently losing data. A platform with no fastlane dir configured is a
/// caller error and throws <see cref="InvalidOperationException"/>.</para>
///
/// <para><b>Null semantics:</b> in <see cref="WriteListing"/> a null field leaves
/// its backing file untouched; a non-null field (including empty string) is written
/// verbatim. Text is written as-is with no trailing-newline manipulation, matching
/// what deliver/supply emit and what the reader trims on read.</para>
/// </summary>
public static class StoreMetadataWriter
{
    /// <summary>
    /// Writes each edited field of <paramref name="listing"/> to its backing
    /// <c>.txt</c> file under the platform's <c>metadata/&lt;locale&gt;</c> dir
    /// (created if absent). Null fields are skipped; non-null values (including empty)
    /// are written verbatim. Throws <see cref="IOException"/> /
    /// <see cref="UnauthorizedAccessException"/> on write failure.
    /// </summary>
    public static void WriteListing(Project project, Platform platform, string locale, StoreListing listing)
    {
        var localeDir = LocaleDir(project, platform, locale);
        Directory.CreateDirectory(localeDir);

        if (platform == Platform.Ios)
        {
            WriteField(localeDir, "name.txt", listing.Name);
            WriteField(localeDir, "subtitle.txt", listing.Subtitle);
            WriteField(localeDir, "promotional_text.txt", listing.PromotionalText);
            WriteField(localeDir, "keywords.txt", listing.Keywords);
            WriteField(localeDir, "description.txt", listing.FullDescription);
            WriteField(localeDir, "release_notes.txt", listing.ReleaseNotes);
            WriteField(localeDir, "marketing_url.txt", listing.MarketingUrl);
            WriteField(localeDir, "support_url.txt", listing.SupportUrl);
            WriteField(localeDir, "privacy_url.txt", listing.PrivacyUrl);
        }
        else
        {
            WriteField(localeDir, "title.txt", listing.Name);
            WriteField(localeDir, "short_description.txt", listing.ShortDescription);
            WriteField(localeDir, "full_description.txt", listing.FullDescription);
            WriteField(localeDir, "video.txt", listing.VideoUrl);
        }
    }

    /// <summary>
    /// Writes release notes for a version+locale to the file the reader reads back:
    /// iOS → <c>metadata/&lt;locale&gt;/release_notes.txt</c>; Android →
    /// <c>metadata/android/&lt;locale&gt;/changelogs/&lt;versionCode&gt;.txt</c>
    /// (the <c>changelogs/</c> dir is created if absent). Throws
    /// <see cref="IOException"/> / <see cref="UnauthorizedAccessException"/> on write
    /// failure; throws <see cref="ArgumentException"/> when Android is requested
    /// without a version code.
    /// </summary>
    public static void WriteReleaseNotes(
        Project project, Platform platform, string locale, string? androidVersionCode, string text)
    {
        var localeDir = LocaleDir(project, platform, locale);

        if (platform == Platform.Ios)
        {
            Directory.CreateDirectory(localeDir);
            File.WriteAllText(Path.Combine(localeDir, "release_notes.txt"), text);
            return;
        }

        if (string.IsNullOrWhiteSpace(androidVersionCode))
        {
            throw new ArgumentException(
                "An Android version code is required to write a changelog.", nameof(androidVersionCode));
        }

        var changelogs = Path.Combine(localeDir, "changelogs");
        Directory.CreateDirectory(changelogs);
        File.WriteAllText(Path.Combine(changelogs, androidVersionCode + ".txt"), text);
    }

    static void WriteField(string localeDir, string fileName, string? value)
    {
        if (value is null)
        {
            return;
        }

        File.WriteAllText(Path.Combine(localeDir, fileName), value);
    }

    /// <summary>
    /// The on-disk <c>metadata/&lt;locale&gt;</c> dir for a platform. iOS:
    /// <c>&lt;iosFastlane&gt;/metadata/&lt;locale&gt;</c>; Android:
    /// <c>&lt;androidFastlane&gt;/metadata/android/&lt;locale&gt;</c>. Throws when the
    /// project has no fastlane dir for the platform.
    /// </summary>
    static string LocaleDir(Project project, Platform platform, string locale) => platform switch
    {
        Platform.Ios => project.IosFastlaneDir is { } ios
            ? Path.Combine(ios, "metadata", locale)
            : throw new InvalidOperationException("Project has no iOS fastlane dir to write into."),
        Platform.Android => project.AndroidFastlaneDir is { } android
            ? Path.Combine(android, "metadata", "android", locale)
            : throw new InvalidOperationException("Project has no Android fastlane dir to write into."),
        _ => throw new InvalidOperationException($"Unsupported platform {platform}."),
    };
}
