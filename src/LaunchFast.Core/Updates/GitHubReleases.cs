using System.Text.Json;

namespace LaunchFast.Core.Updates;

/// <summary>
/// Pure helpers for the in-app update check: comparing the running version to the
/// latest GitHub release tag, and parsing the GitHub releases API response. Total —
/// never throws.
/// </summary>
public static class GitHubReleases
{
    /// <summary>
    /// True only when <paramref name="latest"/> is a strictly greater version than
    /// <paramref name="current"/>. Tolerates a leading 'v', ignores any 4th component
    /// and prerelease/build suffixes, treats missing components as 0, and returns false
    /// for anything non-numeric (can't compare → not newer).
    /// </summary>
    public static bool IsNewer(string current, string latest)
    {
        var c = Parse(current);
        var l = Parse(latest);
        if (c is null || l is null) return false;

        for (var i = 0; i < 3; i++)
        {
            if (l[i] > c[i]) return true;
            if (l[i] < c[i]) return false;
        }
        return false;
    }

    /// <summary>
    /// Parses the GitHub <c>releases/latest</c> response into a <see cref="ReleaseInfo"/>.
    /// Returns null on malformed JSON or a missing/empty <c>tag_name</c>. Never throws.
    /// </summary>
    public static ReleaseInfo? ParseLatest(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return null;

            if (!root.TryGetProperty("tag_name", out var tag) ||
                tag.ValueKind != JsonValueKind.String) return null;

            var tagName = tag.GetString();
            if (string.IsNullOrWhiteSpace(tagName)) return null;

            var url = root.TryGetProperty("html_url", out var u) && u.ValueKind == JsonValueKind.String
                ? u.GetString() ?? string.Empty
                : string.Empty;

            return new ReleaseInfo(tagName, url);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    static int[]? Parse(string version)
    {
        var v = version.Trim();
        if (v.StartsWith('v') || v.StartsWith('V')) v = v[1..];

        var cut = v.IndexOfAny(['+', '-']);
        if (cut >= 0) v = v[..cut];

        var parts = v.Split('.');
        var result = new int[3];
        for (var i = 0; i < 3; i++)
        {
            if (i >= parts.Length) { result[i] = 0; continue; }
            if (!int.TryParse(parts[i], out result[i])) return null;
        }
        return result;
    }
}
