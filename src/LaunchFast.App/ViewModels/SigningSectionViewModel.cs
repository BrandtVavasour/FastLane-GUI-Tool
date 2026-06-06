using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LaunchFast.Core.Env;
using LaunchFast.Core.Models;
using LaunchFast.Core.Signing;
using LaunchFast.Core.Stores;

namespace LaunchFast.App.ViewModels;

/// <summary>
/// Content view-model for a project's "Signing &amp; Certificates" section.
///
/// REAL: the <b>match storage</b> panel (repo / branch / storage / type) comes from
/// <see cref="IosSigningReader"/> parsing the project's <c>ios/fastlane/Matchfile</c>.
/// The <b>provisioning profiles</b> list is read from the user's installed
/// <c>.mobileprovision</c> files (filtered to the app bundle id) with real
/// validity/expiry. The <b>certificates</b> list is read from
/// <c>security find-identity</c>. The <b>registered-device</b> count is derived from
/// the profiles' provisioned devices. The <b>credential</b> presence rows
/// (<c>MATCH_PASSWORD</c>, <c>MATCH_GIT_URL</c>) reuse the real secret store. The
/// "Run match" action runs the genuine <c>sync_certificates</c> lane.
///
/// ILLUSTRATIVE: only the <b>danger zone (match nuke)</b> button is inert — it is
/// flagged in the view and intentionally not wired (destructive).
/// </summary>
public partial class SigningSectionViewModel : ObservableObject
{
    /// <summary>Credential keys surfaced as real presence rows (in display order).</summary>
    static readonly (string Name, string Description)[] CredentialKeys =
    {
        ("MATCH_PASSWORD", "Passphrase that decrypts the match repo"),
        ("MATCH_GIT_URL", "Git URL of the match certificates repo"),
    };

    readonly Project _project;
    readonly IosSigningReader _reader;
    readonly string _profilesDir;
    readonly ISecretStore _secrets;
    readonly Func<string, string?> _readProcessEnv;
    readonly Func<DateTimeOffset> _now;
    readonly Action<Platform, string>? _runLane;
    readonly Func<bool> _hasSyncLane;

    public SigningSectionViewModel(
        Project project,
        ISecretStore? secrets = null,
        IosSigningReader? reader = null,
        string? profilesDir = null,
        Action<Platform, string>? runLane = null,
        Func<bool>? hasSyncLane = null,
        Func<string, string?>? readProcessEnv = null,
        Func<DateTimeOffset>? now = null)
    {
        _project = project;
        _secrets = secrets ?? new NullSecretStore();
        _reader = reader ?? new IosSigningReader();
        _profilesDir = profilesDir ?? IosSigningReader.DefaultProfilesDir;
        _runLane = runLane;
        _hasSyncLane = hasSyncLane ?? (() => false);
        _readProcessEnv = readProcessEnv ?? Environment.GetEnvironmentVariable;
        _now = now ?? (() => DateTimeOffset.Now);

        BundleId = ReadBundleId(project) ?? "com.example.app";

        Certificates = new ObservableCollection<SigningCertRow>();
        Profiles = new ObservableCollection<SigningProfileRow>();
        Credentials = new ObservableCollection<SigningCredentialRow>();

        Refresh();
    }

    /// <summary>App bundle id from the iOS Appfile, or a placeholder when absent.</summary>
    public string BundleId { get; }

    // ---- match storage (REAL) ------------------------------------------------

    /// <summary>True when the project has an iOS fastlane dir with a Matchfile.</summary>
    public bool HasMatch { get; private set; }

    /// <summary>Convenience inverse for the empty-state panel.</summary>
    public bool HasNoMatch => !HasMatch;

    public string MatchRepo { get; private set; } = "—";
    public string MatchBranch { get; private set; } = "—";
    public string MatchStorage { get; private set; } = "—";
    public string MatchType { get; private set; } = "—";

    /// <summary>True when match is git-backed (drives the "match · git" subbar pill).</summary>
    public bool IsGitBacked { get; private set; }

    // ---- subbar --------------------------------------------------------------

    /// <summary>Count summary shown in the subbar (certs + profiles).</summary>
    public string SyncedText { get; private set; } = "—";

    // ---- lists (REAL) --------------------------------------------------------

    public ObservableCollection<SigningCertRow> Certificates { get; }
    public ObservableCollection<SigningProfileRow> Profiles { get; }
    public ObservableCollection<SigningCredentialRow> Credentials { get; }

    public bool HasCertificates => Certificates.Count > 0;
    public bool HasNoCertificates => Certificates.Count == 0;
    public bool HasProfiles => Profiles.Count > 0;
    public bool HasNoProfiles => Profiles.Count == 0;

    /// <summary>Registered-device count derived from the profiles, or "—" when none.</summary>
    public string RegisteredDevicesText { get; private set; } = "—";

    // ---- actions -------------------------------------------------------------

    /// <summary>True when the project exposes the <c>sync_certificates</c> iOS lane.</summary>
    public bool CanRunMatch => _hasSyncLane();

    /// <summary>Runs the real <c>sync_certificates</c> lane via the shell's lane runner.</summary>
    [RelayCommand]
    void RunMatch()
    {
        if (!CanRunMatch) return;
        _runLane?.Invoke(Platform.Ios, "sync_certificates");
    }

    /// <summary>Re-reads the installed certificates + provisioning profiles from disk.</summary>
    [RelayCommand]
    void Refresh()
    {
        var info = _reader.Read(_project.IosFastlaneDir, _profilesDir, BundleId, _now());
        BuildMatch(info.Match);
        BuildProfiles(info.Profiles);
        BuildCertificates();
        BuildCredentials();
        BuildSubbar();

        OnPropertyChanged(nameof(HasMatch));
        OnPropertyChanged(nameof(HasNoMatch));
        OnPropertyChanged(nameof(MatchRepo));
        OnPropertyChanged(nameof(MatchBranch));
        OnPropertyChanged(nameof(MatchStorage));
        OnPropertyChanged(nameof(MatchType));
        OnPropertyChanged(nameof(IsGitBacked));
        OnPropertyChanged(nameof(HasCertificates));
        OnPropertyChanged(nameof(HasNoCertificates));
        OnPropertyChanged(nameof(HasProfiles));
        OnPropertyChanged(nameof(HasNoProfiles));
        OnPropertyChanged(nameof(RegisteredDevicesText));
        OnPropertyChanged(nameof(SyncedText));
    }

    void BuildMatch(MatchConfig match)
    {
        var declared = match.GitUrl is not null
            || match.StorageMode is not null
            || match.Type is not null
            || match.Branch is not null;

        HasMatch = _project.IosFastlaneDir is not null && declared;
        IsGitBacked = match.IsGitBacked;

        MatchRepo = match.GitUrl ?? "—";
        MatchBranch = match.Branch ?? "main";
        MatchStorage = match.StorageMode switch
        {
            null when match.IsGitBacked => "git (encrypted)",
            "git" => "git (encrypted)",
            { } mode => mode,
            _ => "—",
        };
        MatchType = match.Type ?? "—";
    }

    void BuildProfiles(IReadOnlyList<ProvisioningProfile> profiles)
    {
        var now = _now();
        Profiles.Clear();

        foreach (var p in profiles)
        {
            var validity = p.ValidityAt(now);
            var state = validity switch
            {
                SigningValidity.Expired => ProfileState.Bad,
                SigningValidity.ExpiresSoon => ProfileState.Warn,
                _ => ProfileState.Ok,
            };
            var statusText = validity switch
            {
                SigningValidity.Expired => "Expired",
                SigningValidity.ExpiresSoon => "Expires soon",
                _ => "Valid",
            };

            var subParts = new List<string>();
            if (p.AppIdName is not null) subParts.Add(p.AppIdName);
            if (p.BundleId is not null) subParts.Add(p.BundleId);
            if (p.ProvisionsAllDevices) subParts.Add("all devices");
            else if (p.ProvisionedDevices > 0) subParts.Add($"{p.ProvisionedDevices} devices");
            var sub = subParts.Count > 0 ? string.Join(" · ", subParts) : "(no app id)";

            Profiles.Add(new SigningProfileRow(p.Name, statusText, state, sub, ExpiryMeta(p, now)));
        }

        // Registered devices: the max provisioned-device count across profiles
        // (a single device may appear in several profiles, so this is a floor).
        var deviceCount = profiles.Count == 0 ? 0 : profiles.Max(p => p.ProvisionedDevices);
        RegisteredDevicesText = profiles.Any(p => p.ProvisionsAllDevices)
            ? "all devices"
            : deviceCount > 0 ? deviceCount.ToString() : "—";
    }

    static string ExpiryMeta(ProvisioningProfile p, DateTimeOffset now)
    {
        var days = p.DaysToExpiry(now);
        if (days is null) return "no expiry";
        if (days < 0) return $"expired {-days.Value}d ago";
        return $"expires in {days.Value}d";
    }

    void BuildCertificates()
    {
        Certificates.Clear();
        foreach (var cert in _reader.ReadInstalledCertificates())
        {
            Certificates.Add(new SigningCertRow(
                cert.Name,
                "Valid",
                IsValid: true,
                cert.Kind,
                cert.Sha1));
        }
    }

    void BuildCredentials()
    {
        var fromFiles = ProjectSecretScanner.ReadEnvFiles(_project.Path);
        Credentials.Clear();
        foreach (var (name, description) in CredentialKeys)
        {
            var source = ResolveSource(name, fromFiles);
            Credentials.Add(new SigningCredentialRow(name, description, source));
        }
    }

    SecretSource ResolveSource(string name, IReadOnlyDictionary<string, string> fromFiles)
    {
        var ci = _readProcessEnv(name);
        if (!string.IsNullOrEmpty(ci)) return SecretSource.CiEnv;
        if (fromFiles.ContainsKey(name)) return SecretSource.EnvFile;
        if (_secrets.Get(_project.Path, name) is not null) return SecretSource.Keychain;
        return SecretSource.None;
    }

    void BuildSubbar()
    {
        var certs = Certificates.Count;
        var profs = Profiles.Count;
        SyncedText = certs == 0 && profs == 0
            ? "Nothing installed"
            : $"{certs} cert{(certs == 1 ? "" : "s")} · {profs} profile{(profs == 1 ? "" : "s")}";
    }

    static string? ReadBundleId(Project project)
    {
        if (project.IosFastlaneDir is null) return null;
        var appfile = Path.Combine(project.IosFastlaneDir, "Appfile");
        if (!File.Exists(appfile)) return null;

        try
        {
            var id = AppfileReader.AppIdentifier(File.ReadAllText(appfile));
            // The fixture/Appfile may use app_identifier(ENV["..."]) with no literal;
            // treat an env-lookup token as "not declared" and fall back to placeholder.
            return string.IsNullOrWhiteSpace(id) || id.Contains("ENV[") ? null : id;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>A no-op secret store so the VM can be built without one (snapshots).</summary>
    sealed class NullSecretStore : ISecretStore
    {
        public string? Get(string projectId, string key) => null;
        public void Set(string projectId, string key, string value) { }
    }
}

/// <summary>
/// One REAL codesigning certificate row. <see cref="Sha1"/> is the real SHA-1
/// fingerprint reported by <c>security find-identity</c> (formatted colon-separated for
/// display). <c>security</c> does not report a SHA-256, so none is shown — honestly.
/// </summary>
public sealed record SigningCertRow(
    string Title, string StatusText, bool IsValid, string Sub, string Sha1)
{
    /// <summary>The SHA-1 with colon separators every two hex chars, for display.</summary>
    public string Sha1Display => FormatSha1(Sha1);

    static string FormatSha1(string sha1)
    {
        if (string.IsNullOrEmpty(sha1)) return sha1;
        var pairs = new List<string>(sha1.Length / 2 + 1);
        for (var i = 0; i < sha1.Length; i += 2)
            pairs.Add(sha1.Substring(i, Math.Min(2, sha1.Length - i)));
        return string.Join(":", pairs).ToUpperInvariant();
    }
}

/// <summary>State of a provisioning profile (drives the status pill).</summary>
public enum ProfileState { Ok, Warn, Bad }

/// <summary>One REAL provisioning-profile row.</summary>
public sealed record SigningProfileRow(
    string Title, string StatusText, ProfileState State, string Sub, string ExpiresMeta)
{
    public bool IsOk => State == ProfileState.Ok;
    public bool IsWarn => State == ProfileState.Warn;
    public bool IsBad => State == ProfileState.Bad;
}

/// <summary>One REAL match-credential presence row (Set/Missing + source).</summary>
public sealed record SigningCredentialRow(string Name, string Description, SecretSource Source)
{
    public bool IsSet => Source != SecretSource.None;
    public bool IsMissing => !IsSet;
    public string StatusText => IsSet ? "Set" : "Missing";

    public string SourceText => Source switch
    {
        SecretSource.CiEnv => "CI secret",
        SecretSource.EnvFile => ".env",
        SecretSource.Keychain => "Keychain",
        _ => "—",
    };
}
