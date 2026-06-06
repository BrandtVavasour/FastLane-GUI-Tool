using System.Globalization;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace LaunchFast.Core.Signing;

/// <summary>
/// The match configuration declared in a project's <c>ios/fastlane/Matchfile</c>.
/// Every member is null when the corresponding directive is absent. Values that are
/// <c>ENV["..."]</c> lookups (with no literal) are treated as not declared.
/// </summary>
public sealed record MatchConfig(
    string? GitUrl,
    string? StorageMode,
    string? Type,
    string? AppIdentifier,
    string? Username,
    string? TeamId,
    string? Branch,
    bool? ReadOnly)
{
    /// <summary>An empty config for projects without a Matchfile.</summary>
    public static MatchConfig None { get; } =
        new(null, null, null, null, null, null, null, null);

    /// <summary>
    /// True when match is backed by a git repo: either an explicit <c>git</c>
    /// storage_mode, or a declared <c>git_url</c> with no other storage backend.
    /// </summary>
    public bool IsGitBacked =>
        string.Equals(StorageMode, "git", StringComparison.OrdinalIgnoreCase) ||
        (StorageMode is null && !string.IsNullOrWhiteSpace(GitUrl));
}

/// <summary>Validity state of a provisioning profile or certificate.</summary>
public enum SigningValidity
{
    /// <summary>Expires more than 30 days out.</summary>
    Valid,

    /// <summary>Still valid but expires within 30 days.</summary>
    ExpiresSoon,

    /// <summary>Past its expiration date.</summary>
    Expired,
}

/// <summary>
/// A provisioning profile parsed from a <c>.mobileprovision</c> file's embedded
/// XML plist. Dates and device counts are pulled straight from the plist.
/// </summary>
public sealed record ProvisioningProfile(
    string Name,
    string? AppIdName,
    string? BundleId,
    DateTimeOffset? CreationDate,
    DateTimeOffset? ExpirationDate,
    string? TeamName,
    int ProvisionedDevices,
    bool ProvisionsAllDevices)
{
    /// <summary>
    /// Whole days until expiry relative to <paramref name="now"/>; null when there is
    /// no expiration date. Negative when already expired.
    /// </summary>
    public int? DaysToExpiry(DateTimeOffset now) =>
        ExpirationDate is { } exp ? (int)Math.Floor((exp - now).TotalDays) : null;

    /// <summary>Validity classification relative to <paramref name="now"/>.</summary>
    public SigningValidity ValidityAt(DateTimeOffset now)
    {
        if (ExpirationDate is not { } exp) return SigningValidity.Valid;
        if (exp <= now) return SigningValidity.Expired;
        return (exp - now).TotalDays < 30 ? SigningValidity.ExpiresSoon : SigningValidity.Valid;
    }
}

/// <summary>A codesigning identity discovered via <c>security find-identity</c>.</summary>
public sealed record SigningCertificate(string Name, string Sha1)
{
    /// <summary>The identity kind inferred from the name (Distribution / Development).</summary>
    public string Kind =>
        Name.Contains("Distribution", StringComparison.OrdinalIgnoreCase) ? "Distribution"
        : Name.Contains("Development", StringComparison.OrdinalIgnoreCase) ? "Development"
        : "Identity";
}

/// <summary>
/// The iOS signing picture discovered on disk for a project: the parsed Matchfile
/// plus the provisioning profiles read from a directory. Certificates are read
/// separately (they require the <c>security</c> CLI).
/// </summary>
public sealed record IosSigningInfo(
    bool HasIos,
    MatchConfig Match,
    IReadOnlyList<ProvisioningProfile> Profiles)
{
    /// <summary>An empty result for projects without an iOS fastlane dir.</summary>
    public static IosSigningInfo None { get; } =
        new(false, MatchConfig.None, Array.Empty<ProvisioningProfile>());
}

/// <summary>
/// Reads a project's iOS signing setup from disk and (at run time) from the macOS
/// <c>security</c> CLI. Total — never throws.
///
/// <list type="bullet">
/// <item><b>Matchfile</b> (<c>ios/fastlane/Matchfile</c>): parses the match DSL
/// directives — <c>git_url</c>, <c>storage_mode</c>, <c>type</c>,
/// <c>app_identifier</c>, <c>username</c>, <c>team_id</c>, <c>branch</c>,
/// <c>readonly</c>.</item>
/// <item><b>Provisioning profiles</b>: a <c>.mobileprovision</c> is a CMS blob with
/// an embedded XML plist. <see cref="ReadProfiles"/> extracts the
/// <c>&lt;?xml … &lt;/plist&gt;</c> substring and parses it with
/// <see cref="System.Xml.Linq"/>, pulling Name, AppIDName, the
/// <c>application-identifier</c> entitlement, creation/expiration dates, TeamName,
/// the provisioned-device count and the ProvisionsAllDevices flag.</item>
/// <item><b>Certificates</b>: <see cref="ParseSecurityIdentities"/> is a pure parser
/// of <c>security find-identity -p codesigning -v</c> output;
/// <see cref="ReadInstalledCertificates"/> shells out and feeds it (returning empty
/// on any failure / non-macOS). The shell-out is the only run-time-only bit.</item>
/// </list>
/// </summary>
public sealed partial class IosSigningReader
{
    /// <summary>
    /// The default user provisioning-profiles directory on macOS. Callers (and tests)
    /// may pass a different directory to <see cref="ReadProfiles"/>.
    /// </summary>
    public static string DefaultProfilesDir =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            "Library", "MobileDevice", "Provisioning Profiles");

    // match DSL directives tolerate both call form — key("value") — and the bare
    // assignment form — key "value" / key: "value".
    static readonly Regex GitUrlRegex = MatchKey("git_url");
    static readonly Regex StorageModeRegex = MatchKey("storage_mode");
    static readonly Regex TypeRegex = MatchKey("type");
    static readonly Regex AppIdentifierRegex = MatchKey("app_identifier");
    static readonly Regex UsernameRegex = MatchKey("username");
    static readonly Regex TeamIdRegex = MatchKey("team_id");
    static readonly Regex BranchRegex = MatchKey("git_branch|branch");
    static readonly Regex ReadOnlyRegex = new(
        """\breadonly\s*[(:]?\s*(?<v>true|false)\b""",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    static Regex MatchKey(string key) => new(
        $$"""\b(?:{{key}})\s*[(:]?\s*["'](?<v>[^"']+)["']""",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    readonly Func<string?> _runSecurity;

    /// <summary>
    /// Creates a reader. <paramref name="runSecurity"/> supplies the raw output of
    /// <c>security find-identity -p codesigning -v</c> (or null on failure). When
    /// omitted, the real <c>security</c> CLI is invoked on demand.
    /// </summary>
    public IosSigningReader(Func<string?>? runSecurity = null)
    {
        _runSecurity = runSecurity ?? RunSecurityCli;
    }

    // ---- Matchfile -----------------------------------------------------------

    /// <summary>
    /// Reads + parses the project's <c>ios/fastlane/Matchfile</c> and the provisioning
    /// profiles under <paramref name="profilesDir"/> (defaulting to the user dir). When
    /// the project has no iOS fastlane dir, returns <see cref="IosSigningInfo.None"/>.
    /// </summary>
    public IosSigningInfo Read(
        string? iosFastlaneDir,
        string? profilesDir = null,
        string? filterBundleId = null,
        DateTimeOffset? now = null)
    {
        if (iosFastlaneDir is null) return IosSigningInfo.None;

        var match = ReadMatchfile(Path.Combine(iosFastlaneDir, "Matchfile"));
        var dir = profilesDir ?? DefaultProfilesDir;
        var profiles = ReadProfiles(dir, filterBundleId, now);

        return new IosSigningInfo(HasIos: true, Match: match, Profiles: profiles);
    }

    /// <summary>
    /// Parses a Matchfile at <paramref name="path"/>. Returns
    /// <see cref="MatchConfig.None"/> when absent or unreadable.
    /// </summary>
    public static MatchConfig ReadMatchfile(string path)
    {
        var text = ReadTextOrNull(path);
        return text is null ? MatchConfig.None : ParseMatchfile(text);
    }

    /// <summary>Pure parser of Matchfile text.</summary>
    public static MatchConfig ParseMatchfile(string text)
    {
        bool? readOnly = null;
        var ro = ReadOnlyRegex.Match(text);
        if (ro.Success) readOnly = ro.Groups["v"].Value.Equals("true", StringComparison.OrdinalIgnoreCase);

        return new MatchConfig(
            GitUrl: Literal(GitUrlRegex, text),
            StorageMode: Literal(StorageModeRegex, text),
            Type: Literal(TypeRegex, text),
            AppIdentifier: Literal(AppIdentifierRegex, text),
            Username: Literal(UsernameRegex, text),
            TeamId: Literal(TeamIdRegex, text),
            Branch: Literal(BranchRegex, text),
            ReadOnly: readOnly);
    }

    static string? Literal(Regex regex, string text)
    {
        var m = regex.Match(text);
        if (!m.Success) return null;
        var v = m.Groups["v"].Value.Trim();
        // An ENV["..."] lookup token is not a literal declaration.
        return v.Length == 0 ? null : v;
    }

    // ---- Provisioning profiles ----------------------------------------------

    /// <summary>
    /// Reads every <c>.mobileprovision</c> under <paramref name="profilesDir"/>, parses
    /// the embedded plist, and (optionally) filters to <paramref name="filterBundleId"/>
    /// by suffix/glob match. Unreadable / unparseable files are skipped. Returns an
    /// empty list when the directory is missing.
    /// </summary>
    public IReadOnlyList<ProvisioningProfile> ReadProfiles(
        string profilesDir,
        string? filterBundleId = null,
        DateTimeOffset? now = null)
    {
        if (!Directory.Exists(profilesDir)) return Array.Empty<ProvisioningProfile>();

        string[] files;
        try
        {
            files = Directory.GetFiles(profilesDir, "*.mobileprovision");
        }
        catch (IOException) { return Array.Empty<ProvisioningProfile>(); }
        catch (UnauthorizedAccessException) { return Array.Empty<ProvisioningProfile>(); }

        Array.Sort(files, StringComparer.OrdinalIgnoreCase);

        var result = new List<ProvisioningProfile>();
        foreach (var file in files)
        {
            var raw = ReadTextOrNull(file);
            if (raw is null) continue;

            var profile = ParseProfile(raw);
            if (profile is null) continue;

            if (filterBundleId is not null && !BundleIdMatches(profile.BundleId, filterBundleId))
                continue;

            result.Add(profile);
        }

        // Soonest-expiring first, so warnings surface at the top.
        result.Sort((a, b) =>
            Nullable.Compare(a.ExpirationDate, b.ExpirationDate));

        return result;
    }

    /// <summary>
    /// Extracts the embedded XML plist from a <c>.mobileprovision</c> blob's text and
    /// parses the profile fields. Returns null when no plist is found / it cannot be
    /// parsed.
    /// </summary>
    public static ProvisioningProfile? ParseProfile(string fileText)
    {
        var plist = ExtractPlistXml(fileText);
        if (plist is null) return null;

        XElement? root;
        try
        {
            root = XDocument.Parse(plist).Root;
        }
        catch (System.Xml.XmlException) { return null; }

        var dict = root?.Element("dict");
        if (dict is null) return null;

        var name = StringValue(dict, "Name") ?? "(unnamed profile)";
        var appIdName = StringValue(dict, "AppIDName");
        var teamName = StringValue(dict, "TeamName");
        var creation = DateValue(dict, "CreationDate");
        var expiration = DateValue(dict, "ExpirationDate");
        var provisionsAll = BoolValue(dict, "ProvisionsAllDevices") ?? false;

        var devices = ArrayValue(dict, "ProvisionedDevices");
        var deviceCount = devices?.Elements().Count(e => e.Name.LocalName == "string") ?? 0;

        var bundleId = ApplicationIdentifier(dict);

        return new ProvisioningProfile(
            Name: name,
            AppIdName: appIdName,
            BundleId: bundleId,
            CreationDate: creation,
            ExpirationDate: expiration,
            TeamName: teamName,
            ProvisionedDevices: deviceCount,
            ProvisionsAllDevices: provisionsAll);
    }

    /// <summary>
    /// Locates the <c>&lt;?xml … &lt;/plist&gt;</c> substring inside a
    /// <c>.mobileprovision</c> CMS blob (or a plain plist file). Returns null when no
    /// plist is present.
    /// </summary>
    public static string? ExtractPlistXml(string fileText)
    {
        var start = fileText.IndexOf("<?xml", StringComparison.Ordinal);
        if (start < 0) return null;

        var closeIdx = fileText.IndexOf("</plist>", start, StringComparison.Ordinal);
        if (closeIdx < 0) return null;

        var end = closeIdx + "</plist>".Length;
        return fileText.Substring(start, end - start);
    }

    /// <summary>
    /// The <c>application-identifier</c> entitlement value (the bundle id, possibly
    /// prefixed by the team id, e.g. <c>ABCDE12345.com.example.app</c>). The team
    /// prefix is stripped. Null when absent.
    /// </summary>
    static string? ApplicationIdentifier(XElement dict)
    {
        var entitlements = DictValue(dict, "Entitlements");
        var raw = entitlements is null ? null : StringValue(entitlements, "application-identifier");
        if (raw is null) return null;

        // Strip the leading team-id prefix: TEAMID.com.example.app → com.example.app
        var dot = raw.IndexOf('.');
        return dot >= 0 && dot < raw.Length - 1 ? raw[(dot + 1)..] : raw;
    }

    /// <summary>
    /// Whether <paramref name="bundleId"/> matches <paramref name="filter"/>. A trailing
    /// <c>*</c> wildcard matches a prefix; otherwise an exact or suffix match (so a
    /// profile's <c>com.example.app</c> matches a filter of <c>com.example.app</c>, and
    /// a wildcard profile <c>com.example.*</c> matches the filter's prefix).
    /// </summary>
    static bool BundleIdMatches(string? bundleId, string filter)
    {
        if (string.IsNullOrEmpty(bundleId)) return false;
        if (filter == "*") return true;

        // Profile carries a wildcard app id (com.example.*): match the filter prefix.
        if (bundleId.EndsWith("*", StringComparison.Ordinal))
        {
            var prefix = bundleId[..^1];
            return filter.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
        }

        // Filter carries a wildcard (com.example.*): match the profile prefix.
        if (filter.EndsWith("*", StringComparison.Ordinal))
        {
            var prefix = filter[..^1];
            return bundleId.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
        }

        return string.Equals(bundleId, filter, StringComparison.OrdinalIgnoreCase);
    }

    // ---- plist value helpers (<key>X</key> followed by its typed value) -------

    static string? StringValue(XElement dict, string key) =>
        ValueAfterKey(dict, key) is { } v && v.Name.LocalName == "string" ? v.Value : null;

    static bool? BoolValue(XElement dict, string key) =>
        ValueAfterKey(dict, key) is { } v
            ? v.Name.LocalName switch { "true" => true, "false" => false, _ => null }
            : null;

    static XElement? ArrayValue(XElement dict, string key) =>
        ValueAfterKey(dict, key) is { } v && v.Name.LocalName == "array" ? v : null;

    static XElement? DictValue(XElement dict, string key) =>
        ValueAfterKey(dict, key) is { } v && v.Name.LocalName == "dict" ? v : null;

    static DateTimeOffset? DateValue(XElement dict, string key)
    {
        var v = ValueAfterKey(dict, key);
        if (v is null || v.Name.LocalName != "date") return null;
        return DateTimeOffset.TryParse(
            v.Value, CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var dt)
            ? dt
            : null;
    }

    /// <summary>
    /// In a plist <c>&lt;dict&gt;</c>, finds the element immediately following the
    /// <c>&lt;key&gt;name&lt;/key&gt;</c> element (which holds that key's value).
    /// </summary>
    static XElement? ValueAfterKey(XElement dict, string key)
    {
        foreach (var k in dict.Elements())
        {
            if (k.Name.LocalName == "key" && k.Value == key)
                return (k.NextNode as XElement) ?? k.ElementsAfterSelf().FirstOrDefault();
        }
        return null;
    }

    // ---- certificates --------------------------------------------------------

    /// <summary>
    /// Reads the installed codesigning identities by shelling out to
    /// <c>security find-identity -p codesigning -v</c> (or the injected supplier).
    /// Returns an empty list on any failure / non-macOS. Graceful — never throws.
    /// </summary>
    public IReadOnlyList<SigningCertificate> ReadInstalledCertificates()
    {
        try
        {
            var output = _runSecurity();
            return output is null
                ? Array.Empty<SigningCertificate>()
                : ParseSecurityIdentities(output);
        }
        catch
        {
            return Array.Empty<SigningCertificate>();
        }
    }

    // Lines look like: `  1) ABC123…DEF "Apple Distribution: Company (TEAMID)"`.
    [GeneratedRegex(
        "^\\s*\\d+\\)\\s+(?<sha1>[0-9A-Fa-f]{40})\\s+\"(?<name>[^\"]+)\"",
        RegexOptions.Multiline)]
    private static partial Regex IdentityLineRegex();

    /// <summary>
    /// Pure parser of <c>security find-identity -p codesigning -v</c> output. Each
    /// matched identity line yields a <see cref="SigningCertificate"/> (name + sha1).
    /// Lines that don't match the identity shape are ignored, as is the trailing
    /// "N valid identities found" summary.
    /// </summary>
    public static IReadOnlyList<SigningCertificate> ParseSecurityIdentities(string securityOutput)
    {
        if (string.IsNullOrEmpty(securityOutput)) return Array.Empty<SigningCertificate>();

        var result = new List<SigningCertificate>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (Match m in IdentityLineRegex().Matches(securityOutput))
        {
            var sha1 = m.Groups["sha1"].Value;
            var name = m.Groups["name"].Value.Trim();
            // De-dupe by sha1 (the same identity can be listed under several keychains).
            if (seen.Add(sha1))
                result.Add(new SigningCertificate(name, sha1));
        }

        return result;
    }

    static string? RunSecurityCli()
    {
        // Run-time only: not exercised by unit tests. Returns null off macOS / on any
        // failure so callers degrade to an honest empty state.
        if (!OperatingSystem.IsMacOS()) return null;

        try
        {
            using var proc = new System.Diagnostics.Process
            {
                StartInfo = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "security",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                },
            };
            proc.StartInfo.ArgumentList.Add("find-identity");
            proc.StartInfo.ArgumentList.Add("-p");
            proc.StartInfo.ArgumentList.Add("codesigning");
            proc.StartInfo.ArgumentList.Add("-v");

            if (!proc.Start()) return null;
            var output = proc.StandardOutput.ReadToEnd();
            proc.WaitForExit(5000);
            return output;
        }
        catch
        {
            return null;
        }
    }

    static string? ReadTextOrNull(string path)
    {
        if (!File.Exists(path)) return null;
        try { return File.ReadAllText(path); }
        catch (IOException) { return null; }
        catch (UnauthorizedAccessException) { return null; }
    }
}
