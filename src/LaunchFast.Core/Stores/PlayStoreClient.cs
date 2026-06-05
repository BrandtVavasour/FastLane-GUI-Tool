using System.Text.Json;
using Google.Apis.AndroidPublisher.v3;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Services;
using LaunchFast.Core.Models;

namespace LaunchFast.Core.Stores;

/// <summary>
/// Talks to the Google Play Developer API. The track→model mapping is a pure
/// static function (unit-tested against a recorded fixture); the OAuth/HTTP
/// plumbing is orchestration exercised manually / in later phases.
/// </summary>
public sealed class PlayStoreClient : IPlayStoreClient, IDisposable
{
    private readonly AndroidPublisherService _service;

    /// <summary>
    /// Production constructor: builds an <see cref="AndroidPublisherService"/> from a
    /// service-account JSON key file. Does NOT make any network call.
    /// </summary>
    public PlayStoreClient(string serviceAccountJsonPath)
    {
        var credential = GoogleCredential
            .FromFile(serviceAccountJsonPath)
            .CreateScoped(AndroidPublisherService.Scope.Androidpublisher);

        _service = new AndroidPublisherService(new BaseClientService.Initializer
        {
            HttpClientInitializer = credential,
            ApplicationName = "LaunchFast",
        });
    }

    /// <summary>Maps a Play <see cref="Destination"/> to its track id.</summary>
    public static string TrackName(Destination d) => d switch
    {
        Destination.PlayInternal => "internal",
        Destination.PlayBeta => "beta",
        Destination.PlayProduction => "production",
        _ => "",
    };

    private static Destination DestinationFor(string? track) => track switch
    {
        "internal" => Destination.PlayInternal,
        "beta" => Destination.PlayBeta,
        "production" => Destination.PlayProduction,
        _ => Destination.None,
    };

    /// <summary>
    /// Pure mapper for a Play <c>edits.tracks.list</c> response. Never throws;
    /// missing fields are skipped. Returns a dictionary keyed by track id.
    /// </summary>
    public static IReadOnlyDictionary<string, StoreStatus> MapTracks(string json)
    {
        var result = new Dictionary<string, StoreStatus>();

        try
        {
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("tracks", out var tracks) ||
                tracks.ValueKind != JsonValueKind.Array)
            {
                return result;
            }

            foreach (var track in tracks.EnumerateArray())
            {
                var trackId = track.TryGetProperty("track", out var t) ? t.GetString() : null;
                var destination = DestinationFor(trackId);
                if (trackId is null || destination == Destination.None)
                {
                    continue;
                }

                if (!track.TryGetProperty("releases", out var releases) ||
                    releases.ValueKind != JsonValueKind.Array ||
                    releases.GetArrayLength() == 0)
                {
                    continue;
                }

                var release = ChooseRelease(releases);
                var name = release.TryGetProperty("name", out var n) ? n.GetString() : null;
                var code = HighestVersionCode(release);

                result[trackId] = new StoreStatus(
                    destination,
                    Available: true,
                    Line: BuildLine(name, code),
                    Secondary: null);
            }
        }
        catch (JsonException)
        {
            // total mapper: return whatever was gathered (typically empty)
        }

        return result;
    }

    private static JsonElement ChooseRelease(JsonElement releases)
    {
        foreach (var release in releases.EnumerateArray())
        {
            var status = release.TryGetProperty("status", out var s) ? s.GetString() : null;
            if (status == "completed")
            {
                return release;
            }
        }

        return releases[0];
    }

    private static string? HighestVersionCode(JsonElement release)
    {
        if (!release.TryGetProperty("versionCodes", out var codes) ||
            codes.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        string? best = null;
        long bestValue = long.MinValue;
        foreach (var entry in codes.EnumerateArray())
        {
            var raw = entry.ValueKind == JsonValueKind.String
                ? entry.GetString()
                : entry.ToString();
            if (raw is not null && long.TryParse(raw, out var value) && value > bestValue)
            {
                bestValue = value;
                best = raw;
            }
        }

        return best;
    }

    private static string? BuildLine(string? name, string? code)
    {
        if (name is not null && code is not null)
        {
            return $"{name} ({code})";
        }

        return name ?? code;
    }

    public async Task<StoreStatus> GetStatusAsync(
        string packageName, Destination destination, CancellationToken ct = default)
    {
        var trackId = TrackName(destination);
        if (trackId.Length == 0)
        {
            return StoreStatus.Unavailable(destination);
        }

        var edit = await _service.Edits.Insert(new Google.Apis.AndroidPublisher.v3.Data.AppEdit(), packageName)
            .ExecuteAsync(ct).ConfigureAwait(false);
        var editId = edit.Id;

        try
        {
            var track = await _service.Edits.Tracks.Get(packageName, editId, trackId)
                .ExecuteAsync(ct).ConfigureAwait(false);

            // Reuse the pure mapper by serializing the strongly-typed Track back to the
            // Play JSON shape (track id + releases), keeping the mapping logic DRY.
            var json = JsonSerializer.Serialize(new
            {
                tracks = new[]
                {
                    new
                    {
                        track = trackId,
                        releases = (track.Releases ?? [])
                            .Select(r => new
                            {
                                name = r.Name,
                                versionCodes = r.VersionCodes,
                                status = r.Status,
                            }),
                    },
                },
            });

            var mapped = MapTracks(json);
            return mapped.TryGetValue(trackId, out var status)
                ? status
                : StoreStatus.Unavailable(destination);
        }
        finally
        {
            // Discard the read-only edit so it doesn't linger.
            try
            {
                await _service.Edits.Delete(packageName, editId).ExecuteAsync(ct).ConfigureAwait(false);
            }
            catch
            {
                // Best-effort cleanup; never mask the primary result/exception.
            }
        }
    }

    public void Dispose() => _service.Dispose();
}
