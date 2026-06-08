using LaunchFast.Core.Updates;

namespace LaunchFast.App.Services;

/// <summary>
/// Checks GitHub for a newer release of LaunchFast. Fail-silent: any network error,
/// non-200, throttle, or malformed body yields null (no UI noise). Returns a
/// <see cref="ReleaseInfo"/> only when the latest release tag is strictly newer than
/// the running version.
/// </summary>
public sealed class UpdateService
{
    const string DefaultUrl =
        "https://api.github.com/repos/BrandtVavasour/FastLane-GUI-Tool/releases/latest";

    readonly HttpClient _http;
    readonly string _currentVersion;
    readonly string _url;

    public UpdateService(HttpClient http, string? currentVersion = null, string? url = null)
    {
        _http = http;
        _currentVersion = currentVersion ?? AppVersion.Current;
        _url = url ?? DefaultUrl;
    }

    public async Task<ReleaseInfo?> CheckAsync(CancellationToken ct = default)
    {
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, _url);
            req.Headers.UserAgent.ParseAdd("LaunchFast-update-check");
            req.Headers.Accept.ParseAdd("application/vnd.github+json");

            using var resp = await _http.SendAsync(req, ct);
            if (!resp.IsSuccessStatusCode) return null;

            var json = await resp.Content.ReadAsStringAsync(ct);
            var rel = GitHubReleases.ParseLatest(json);
            if (rel is null) return null;

            return GitHubReleases.IsNewer(_currentVersion, rel.TagName) ? rel : null;
        }
        catch
        {
            return null;
        }
    }
}
