using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using LaunchFast.Core.Models;

namespace LaunchFast.Core.Stores;

/// <summary>
/// Talks to the App Store Connect API. The JSON→model mappers are pure static
/// functions (unit-tested against recorded fixtures); the JWT/HTTP plumbing is
/// orchestration exercised manually / in later phases.
/// </summary>
public sealed class AppStoreConnectClient : IAppStoreConnectClient, IDisposable
{
    private const string BaseUrl = "https://api.appstoreconnect.apple.com";
    private const string Audience = "appstoreconnect-v1";

    private static readonly string[] InReviewStates =
    [
        "IN_REVIEW",
        "WAITING_FOR_REVIEW",
        "PENDING_DEVELOPER_RELEASE",
        "PROCESSING_FOR_APP_STORE",
    ];

    private readonly ECDsa _key;
    private readonly string _keyId;
    private readonly string _issuerId;
    private readonly HttpClient _http;
    private readonly bool _ownsKey;

    /// <summary>Production constructor: builds the signing key from a .p8 PEM string.</summary>
    public AppStoreConnectClient(string privateKeyPem, string keyId, string issuerId, HttpClient? http = null)
    {
        var key = ECDsa.Create();
        key.ImportFromPem(privateKeyPem);
        _key = key;
        _keyId = keyId;
        _issuerId = issuerId;
        _http = http ?? new HttpClient();
        _ownsKey = true;
    }

    private AppStoreConnectClient(ECDsa key, string keyId, string issuerId, HttpClient http)
    {
        _key = key;
        _keyId = keyId;
        _issuerId = issuerId;
        _http = http;
        _ownsKey = true;
    }

    /// <summary>
    /// Builds a client from fastlane's App Store Connect <c>api_key.json</c> shape
    /// <c>{ "key_id", "issuer_id", "key" }</c> where <c>key</c> is the .p8 PEM.
    /// Returns null (never throws) if the file is missing or unparseable.
    /// </summary>
    public static AppStoreConnectClient? FromKeyFile(string apiKeyJsonPath)
    {
        try
        {
            if (!File.Exists(apiKeyJsonPath))
            {
                return null;
            }

            using var doc = JsonDocument.Parse(File.ReadAllText(apiKeyJsonPath));
            var root = doc.RootElement;

            var keyId = root.TryGetProperty("key_id", out var kid) ? kid.GetString() : null;
            var issuerId = root.TryGetProperty("issuer_id", out var iss) ? iss.GetString() : null;
            var pem = root.TryGetProperty("key", out var k) ? k.GetString() : null;

            if (keyId is null || issuerId is null || string.IsNullOrWhiteSpace(pem))
            {
                return null;
            }

            return new AppStoreConnectClient(pem, keyId, issuerId);
        }
        catch (Exception ex) when (ex is JsonException or IOException or CryptographicException or ArgumentException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>
    /// Test seam: an instance backed by an ephemeral P-256 key (so JWT creation works)
    /// and a canned-response handler. Auth is irrelevant for canned responses.
    /// </summary>
    public static AppStoreConnectClient WithHandler(HttpMessageHandler handler)
    {
        var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var http = new HttpClient(handler) { BaseAddress = new Uri(BaseUrl) };
        return new AppStoreConnectClient(key, "TESTKEYID", "test-issuer", http);
    }

    /// <summary>Builds a well-formed ES256 JWT for App Store Connect.</summary>
    public string CreateJwt()
    {
        var iat = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var exp = iat + 1200;

        var header = $$"""{"alg":"ES256","kid":"{{_keyId}}","typ":"JWT"}""";
        var payload = $$"""{"iss":"{{_issuerId}}","iat":{{iat}},"exp":{{exp}},"aud":"{{Audience}}"}""";

        var signingInput =
            Base64UrlEncode(Encoding.UTF8.GetBytes(header)) + "." +
            Base64UrlEncode(Encoding.UTF8.GetBytes(payload));

        var signature = _key.SignData(
            Encoding.UTF8.GetBytes(signingInput),
            HashAlgorithmName.SHA256,
            DSASignatureFormat.IeeeP1363FixedFieldConcatenation);

        return signingInput + "." + Base64UrlEncode(signature);
    }

    public async Task<StoreStatus> GetStatusAsync(string bundleId, Destination destination, CancellationToken ct = default)
    {
        var appId = await ResolveAppIdAsync(bundleId, ct).ConfigureAwait(false);
        if (appId is null)
        {
            return StoreStatus.Unavailable(destination);
        }

        return destination switch
        {
            Destination.AppStore => MapAppStoreVersions(
                await GetAsync($"/v1/apps/{appId}/appStoreVersions?limit=10", ct).ConfigureAwait(false)),
            Destination.TestFlight => MapTestFlight(
                await GetAsync($"/v1/apps/{appId}/builds?sort=-version&limit=1", ct).ConfigureAwait(false)),
            _ => StoreStatus.Unavailable(destination),
        };
    }

    private async Task<string?> ResolveAppIdAsync(string bundleId, CancellationToken ct)
    {
        var json = await GetAsync(
            $"/v1/apps?filter[bundleId]={Uri.EscapeDataString(bundleId)}", ct).ConfigureAwait(false);
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("data", out var data) &&
                data.ValueKind == JsonValueKind.Array &&
                data.GetArrayLength() > 0 &&
                data[0].TryGetProperty("id", out var id))
            {
                return id.GetString();
            }
        }
        catch (JsonException)
        {
            // fall through
        }
        return null;
    }

    private async Task<string> GetAsync(string relativeUrl, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, BaseUrl + relativeUrl);
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", CreateJwt());
        using var response = await _http.SendAsync(request, ct).ConfigureAwait(false);
        return await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Pure mapper for an App Store Connect <c>appStoreVersions</c> list. Never throws.
    /// </summary>
    public static StoreStatus MapAppStoreVersions(string json)
    {
        string? live = null;
        string? inReview = null;

        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in data.EnumerateArray())
                {
                    if (!item.TryGetProperty("attributes", out var attrs))
                    {
                        continue;
                    }

                    var versionString = attrs.TryGetProperty("versionString", out var vs) ? vs.GetString() : null;
                    var state = attrs.TryGetProperty("appStoreState", out var st) ? st.GetString() : null;

                    if (versionString is null || state is null)
                    {
                        continue;
                    }

                    if (live is null && state == "READY_FOR_SALE")
                    {
                        live = versionString;
                    }
                    else if (inReview is null && Array.IndexOf(InReviewStates, state) >= 0)
                    {
                        inReview = versionString;
                    }
                }
            }
        }
        catch (JsonException)
        {
            // total mapper: fall through to nulls
        }

        return new StoreStatus(
            Destination.AppStore,
            Available: true,
            Line: live is null ? null : $"{live} live",
            Secondary: inReview is null ? null : $"{inReview} in review");
    }

    /// <summary>
    /// Pure mapper for an App Store Connect <c>builds</c> list. Takes the first
    /// (highest, given <c>sort=-version</c>) build's number. Never throws.
    /// </summary>
    public static StoreStatus MapTestFlight(string buildsJson)
    {
        string? buildNumber = null;

        try
        {
            using var doc = JsonDocument.Parse(buildsJson);
            if (doc.RootElement.TryGetProperty("data", out var data) &&
                data.ValueKind == JsonValueKind.Array &&
                data.GetArrayLength() > 0 &&
                data[0].TryGetProperty("attributes", out var attrs) &&
                attrs.TryGetProperty("version", out var version))
            {
                buildNumber = version.GetString();
            }
        }
        catch (JsonException)
        {
            // total mapper
        }

        return new StoreStatus(
            Destination.TestFlight,
            Available: true,
            Line: buildNumber is null ? null : $"build {buildNumber}",
            Secondary: null);
    }

    /// <summary>
    /// Resolves the app id from its bundle id, then reads the newest build,
    /// the beta groups and the beta testers and maps them into a single
    /// <see cref="TestFlightInfo"/>. Returns <see cref="TestFlightInfo.Empty"/>
    /// when the app cannot be resolved; never fabricates data.
    /// </summary>
    public async Task<TestFlightInfo> GetTestFlightAsync(string bundleId, CancellationToken ct = default)
    {
        var appId = await ResolveAppIdAsync(bundleId, ct).ConfigureAwait(false);
        if (appId is null)
        {
            return TestFlightInfo.Empty;
        }

        var buildsJson = await GetAsync(
            $"/v1/apps/{appId}/builds?sort=-version&limit=1&include=betaBuildLocalizations", ct)
            .ConfigureAwait(false);
        var groupsJson = await GetAsync(
            $"/v1/apps/{appId}/betaGroups?limit=50", ct).ConfigureAwait(false);
        var testersJson = await GetAsync(
            $"/v1/apps/{appId}/betaTesters?limit=50", ct).ConfigureAwait(false);

        return new TestFlightInfo(
            MapBuildsDetailed(buildsJson),
            MapBetaGroups(groupsJson),
            MapBetaTesters(testersJson));
    }

    /// <summary>
    /// Pure mapper for an App Store Connect <c>builds</c> list. Picks the newest
    /// build (first, given <c>sort=-version</c>) and maps its processing/compliance
    /// state. Never throws; missing fields → defaults.
    /// </summary>
    public static BuildInfo? MapBuildsDetailed(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("data", out var data) ||
                data.ValueKind != JsonValueKind.Array ||
                data.GetArrayLength() == 0 ||
                !data[0].TryGetProperty("attributes", out var attrs))
            {
                return null;
            }

            var buildNumber = attrs.TryGetProperty("version", out var v) ? v.GetString() : null;
            var processing = attrs.TryGetProperty("processingState", out var p) ? p.GetString() : null;
            var marketingVersion = attrs.TryGetProperty("preReleaseVersion", out var pre) &&
                pre.TryGetProperty("version", out var pv)
                    ? pv.GetString()
                    : null;

            // Export compliance: usesNonExemptEncryption == true with no docs filed
            // is the "expired/blocking" case ASC surfaces; map pragmatically.
            bool? expiredCompliance = null;
            if (attrs.TryGetProperty("usesNonExemptEncryption", out var enc) &&
                (enc.ValueKind == JsonValueKind.True || enc.ValueKind == JsonValueKind.False))
            {
                expiredCompliance = enc.GetBoolean();
            }

            string? expiresText = null;
            if (attrs.TryGetProperty("expired", out var expired) &&
                expired.ValueKind == JsonValueKind.True)
            {
                expiresText = "Expired";
            }
            else if (attrs.TryGetProperty("expirationDate", out var ed) &&
                ed.ValueKind == JsonValueKind.String &&
                DateTimeOffset.TryParse(ed.GetString(), out var when))
            {
                var days = (int)Math.Round((when - DateTimeOffset.UtcNow).TotalDays);
                expiresText = days <= 0 ? "Expired" : $"expires in {days} days";
            }

            var whatsToTest = ExtractWhatsToTest(doc.RootElement);

            return new BuildInfo(
                marketingVersion ?? "—",
                buildNumber ?? "—",
                processing ?? "—",
                expiredCompliance,
                expiresText,
                whatsToTest);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// Pulls the first non-empty <c>whatsNew</c> from any included
    /// <c>betaBuildLocalizations</c> resource, if present.
    /// </summary>
    private static string? ExtractWhatsToTest(JsonElement root)
    {
        if (!root.TryGetProperty("included", out var included) ||
            included.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        foreach (var item in included.EnumerateArray())
        {
            if (item.TryGetProperty("type", out var type) &&
                type.GetString() == "betaBuildLocalizations" &&
                item.TryGetProperty("attributes", out var attrs) &&
                attrs.TryGetProperty("whatsNew", out var wn) &&
                wn.ValueKind == JsonValueKind.String)
            {
                var text = wn.GetString();
                if (!string.IsNullOrWhiteSpace(text))
                {
                    return text;
                }
            }
        }
        return null;
    }

    /// <summary>
    /// Pure mapper for an App Store Connect <c>betaGroups</c> list. Tester count is
    /// read from <c>relationships.betaTesters.meta.paging.total</c> when present.
    /// Never throws; missing fields → defaults.
    /// </summary>
    public static IReadOnlyList<BetaGroup> MapBetaGroups(string json)
    {
        var groups = new List<BetaGroup>();
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("data", out var data) ||
                data.ValueKind != JsonValueKind.Array)
            {
                return groups;
            }

            foreach (var item in data.EnumerateArray())
            {
                if (!item.TryGetProperty("attributes", out var attrs))
                {
                    continue;
                }

                var name = attrs.TryGetProperty("name", out var n) ? n.GetString() : null;
                if (name is null)
                {
                    continue;
                }

                var isInternal = attrs.TryGetProperty("isInternalGroup", out var ig) &&
                    ig.ValueKind == JsonValueKind.True;

                var count = 0;
                if (item.TryGetProperty("relationships", out var rels) &&
                    rels.TryGetProperty("betaTesters", out var bt) &&
                    bt.TryGetProperty("meta", out var meta) &&
                    meta.TryGetProperty("paging", out var paging) &&
                    paging.TryGetProperty("total", out var total) &&
                    total.ValueKind == JsonValueKind.Number)
                {
                    count = total.GetInt32();
                }

                groups.Add(new BetaGroup(name, isInternal, count));
            }
        }
        catch (JsonException)
        {
            // total mapper
        }
        return groups;
    }

    /// <summary>
    /// Pure mapper for an App Store Connect <c>betaTesters</c> list. Maps the
    /// tester's name/email and normalises their state from the <c>state</c> /
    /// <c>inviteType</c> attributes. Never throws; missing fields → defaults.
    /// </summary>
    public static IReadOnlyList<BetaTester> MapBetaTesters(string json)
    {
        var testers = new List<BetaTester>();
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("data", out var data) ||
                data.ValueKind != JsonValueKind.Array)
            {
                return testers;
            }

            foreach (var item in data.EnumerateArray())
            {
                if (!item.TryGetProperty("attributes", out var attrs))
                {
                    continue;
                }

                var first = attrs.TryGetProperty("firstName", out var fn) ? fn.GetString() : null;
                var last = attrs.TryGetProperty("lastName", out var ln) ? ln.GetString() : null;
                var email = attrs.TryGetProperty("email", out var em) ? em.GetString() : null;

                var state = attrs.TryGetProperty("state", out var st) ? st.GetString() : null;
                if (string.IsNullOrWhiteSpace(state) &&
                    attrs.TryGetProperty("inviteType", out var it))
                {
                    state = it.GetString();
                }

                testers.Add(new BetaTester(
                    first ?? "",
                    last ?? "",
                    email ?? "",
                    NormalizeTesterState(state),
                    GroupName: null));
            }
        }
        catch (JsonException)
        {
            // total mapper
        }
        return testers;
    }

    /// <summary>Normalises ASC tester state codes into a display token.</summary>
    private static string NormalizeTesterState(string? raw) => raw switch
    {
        null or "" => "Pending",
        "INSTALLED" => "Installed",
        "INVITED" or "EMAIL" => "Invited",
        "ACCEPTED" => "Accepted",
        "NOT_INSTALLED" => "Pending",
        _ => CapitalizeToken(raw),
    };

    private static string CapitalizeToken(string raw)
    {
        var lower = raw.Replace('_', ' ').ToLowerInvariant();
        return lower.Length == 0 ? lower : char.ToUpperInvariant(lower[0]) + lower[1..];
    }

    private static string Base64UrlEncode(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    public void Dispose()
    {
        if (_ownsKey)
        {
            _key.Dispose();
        }
    }
}
