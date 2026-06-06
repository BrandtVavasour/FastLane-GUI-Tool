using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LaunchFast.Core.Env;
using LaunchFast.Core.Models;
using LaunchFast.Core.Signing;
using LaunchFast.Core.Stores;

namespace LaunchFast.App.ViewModels;

/// <summary>
/// Content view-model for a project's "Android Signing" section.
///
/// REAL: the gradle <c>signingConfig</c> rows (storeFile / storeType / keyAlias /
/// release-applied) and the <c>key.properties</c> presence come from
/// <see cref="AndroidSigningReader"/> reading the project's
/// <c>android/app/build.gradle</c> on disk. The credential rows
/// (<c>KEYSTORE_PASSWORD</c>, <c>KEY_PASSWORD</c>, the Play service-account key)
/// are resolved against the real env / <c>.env*</c> / Keychain precedence, exactly
/// like the Secrets screen. The "Build AAB" action runs the project's genuine
/// Android <c>build</c> fastlane lane (disabled when that lane is absent).
///
/// ILLUSTRATIVE (flagged via <see cref="IsPlaceholder"/> and an "Illustrative"
/// hint in the view): the upper "Signing keys" list (upload / app-signing keys),
/// the certificate-fingerprints panel, the "Play App Signing · Enrolled" pills,
/// and the upload-key-reset danger zone. These need <c>keytool</c> / the Play
/// Developer API, which are not wired yet. "Verify keystore" is an inert
/// placeholder (running <c>keytool</c> is a future enhancement).
/// </summary>
public partial class AndroidSigningSectionViewModel : ObservableObject
{
    /// <summary>Credential keys surfaced as real presence rows (in display order).</summary>
    static readonly (string Name, string Description)[] CredentialKeys =
    {
        ("KEYSTORE_PASSWORD", "Password for the release keystore"),
        ("KEY_PASSWORD", "Password for the signing key"),
        ("PLAY_JSON_KEY", "Service-account JSON key for the Play Developer API"),
    };

    // Recognised env var names that hold the Play service-account key, by priority.
    static readonly string[] PlayKeyCandidates =
    {
        "PLAY_JSON_KEY", "SUPPLY_JSON_KEY", "GOOGLE_PLAY_JSON_KEY", "PLAY_JSON_KEY_DATA",
    };

    readonly Project _project;
    readonly ISecretStore _secrets;
    readonly Func<string, string?> _readProcessEnv;
    readonly Action<Platform, string>? _runLane;
    readonly Func<bool> _hasBuildLane;

    public AndroidSigningSectionViewModel(
        Project project,
        ISecretStore secrets,
        Action<Platform, string>? runLane = null,
        Func<bool>? hasBuildLane = null,
        Func<string, string?>? readProcessEnv = null)
    {
        _project = project;
        _secrets = secrets;
        _runLane = runLane;
        _hasBuildLane = hasBuildLane ?? (() => false);
        _readProcessEnv = readProcessEnv ?? Environment.GetEnvironmentVariable;

        var info = AndroidSigningReader.Read(project);
        HasAndroid = info.HasAndroid;
        PackageName = ReadPackageName(project) ?? "com.example.app";

        BuildGradleRows(info);
        BuildCredentialRows();
        BuildIllustrativeKeys();
        BuildFingerprints();
    }

    /// <summary>True only for the illustrative blocks (keys, fingerprints, enrolment).</summary>
    public bool IsPlaceholder => true;

    /// <summary>False when the project has no Android module → an empty-state view.</summary>
    public bool HasAndroid { get; }

    /// <summary>Convenience inverse for binding the empty-state panel.</summary>
    public bool HasNoAndroid => !HasAndroid;

    /// <summary>Android package id from the Android Appfile, or a placeholder.</summary>
    public string PackageName { get; }

    // ---- gradle signingConfig (REAL) ----------------------------------------
    public ObservableCollection<GradleSigningRow> GradleRows { get; } = new();

    /// <summary>Whether the project declares a real release signingConfig.</summary>
    public bool HasGradleConfig { get; private set; }

    /// <summary>key.properties presence note (REAL).</summary>
    public string KeyPropertiesText { get; private set; } = "key.properties not found";

    public bool HasKeyProperties { get; private set; }

    // ---- credentials (REAL) -------------------------------------------------
    public ObservableCollection<AndroidCredentialRow> Credentials { get; } = new();

    // ---- illustrative signing keys ------------------------------------------
    public ObservableCollection<SigningKeyRow> SigningKeys { get; } = new();

    // ---- illustrative fingerprints ------------------------------------------
    public ObservableCollection<FingerprintRow> Fingerprints { get; } = new();

    // ---- subbar illustrative pills ------------------------------------------
    public string EnrolledText => "Enrolled";

    // ---- actions ------------------------------------------------------------

    /// <summary>True when the project exposes an Android <c>build</c> lane.</summary>
    public bool CanBuildAab => _hasBuildLane();

    /// <summary>Runs the real Android <c>build</c> lane via the shell's lane runner.</summary>
    [RelayCommand]
    void BuildAab()
    {
        if (!CanBuildAab) return;
        _runLane?.Invoke(Platform.Android, "build");
    }

    void BuildGradleRows(AndroidSigningInfo info)
    {
        HasGradleConfig = info.StoreFile is not null
            || info.StoreType is not null
            || info.KeyAlias is not null
            || info.ReleaseSigningApplied;

        GradleRows.Clear();
        GradleRows.Add(new GradleSigningRow("storeFile", info.StoreFile ?? "—"));
        GradleRows.Add(new GradleSigningRow("storeType", info.StoreType ?? "—"));
        GradleRows.Add(new GradleSigningRow("keyAlias", info.KeyAlias ?? "—"));
        GradleRows.Add(new GradleSigningRow(
            "signingConfig release",
            info.ReleaseSigningApplied ? "applied" : "not applied",
            IsGood: info.ReleaseSigningApplied));

        HasKeyProperties = info.HasKeyProperties;
        KeyPropertiesText = info.HasKeyProperties
            ? "key.properties · " + (info.KeyPropertyNames.Count > 0
                ? string.Join(", ", info.KeyPropertyNames)
                : "no keys declared")
            : "key.properties not found";
    }

    void BuildCredentialRows()
    {
        var fromFiles = ProjectSecretScanner.ReadEnvFiles(_project.Path);

        Credentials.Clear();
        foreach (var (name, description) in CredentialKeys)
        {
            // The Play key may live under any of several conventional names.
            var resolvedName = name == "PLAY_JSON_KEY"
                ? PlayKeyCandidates.FirstOrDefault(c => IsSet(c, fromFiles)) ?? name
                : name;

            var source = ResolveSource(resolvedName, fromFiles);
            Credentials.Add(new AndroidCredentialRow(resolvedName, description, source));
        }
    }

    bool IsSet(string name, IReadOnlyDictionary<string, string> fromFiles) =>
        ResolveSource(name, fromFiles) != SecretSource.None;

    SecretSource ResolveSource(string name, IReadOnlyDictionary<string, string> fromFiles)
    {
        var ci = _readProcessEnv(name);
        if (!string.IsNullOrEmpty(ci)) return SecretSource.CiEnv;
        if (fromFiles.ContainsKey(name)) return SecretSource.EnvFile;
        if (_secrets.Get(_project.Path, name) is not null) return SecretSource.Keychain;
        return SecretSource.None;
    }

    void BuildIllustrativeKeys()
    {
        SigningKeys.Clear();
        SigningKeys.Add(new SigningKeyRow(
            "Upload key", "Active", IsGreen: true,
            "alias: upload · RSA 2048 · you sign the AAB", "valid to 2052"));
        SigningKeys.Add(new SigningKeyRow(
            "App signing key", "Enrolled", IsGreen: false,
            "Held & managed by Google Play · re-signs each release", "Google"));
    }

    void BuildFingerprints()
    {
        Fingerprints.Clear();
        Fingerprints.Add(new FingerprintRow("Upload", "SHA-256",
            "A1:B2:C3:D4:E5:F6:07:18:29:3A:4B:5C:6D:7E:8F:90:A1:B2:C3:D4:E5:F6:07:18:29:3A:4B:5C:6D:7E:8F:90",
            IsAccent: true));
        Fingerprints.Add(new FingerprintRow("App signing", "SHA-256",
            "5F:0E:1D:2C:3B:4A:59:68:77:86:95:A4:B3:C2:D1:E0:FF:0E:1D:2C:3B:4A:59:68:77:86:95:A4:B3:C2:D1:E0",
            IsAccent: false));
        Fingerprints.Add(new FingerprintRow("App signing", "SHA-1",
            "9C:8B:7A:69:58:47:36:25:14:03:F2:E1:D0:CF:BE:AD:9C:8B:7A:69",
            IsAccent: false));
    }

    static string? ReadPackageName(Project project)
    {
        if (project.AndroidFastlaneDir is null) return null;
        var appfile = Path.Combine(project.AndroidFastlaneDir, "Appfile");
        if (!File.Exists(appfile)) return null;

        try
        {
            var id = AppfileReader.PackageName(File.ReadAllText(appfile));
            return string.IsNullOrWhiteSpace(id) ? null : id;
        }
        catch
        {
            return null;
        }
    }
}

/// <summary>One REAL gradle signingConfig dl-row (key + value).</summary>
public sealed record GradleSigningRow(string Key, string Value, bool IsGood = false);

/// <summary>One REAL credential presence row (Set/Missing + source).</summary>
public sealed record AndroidCredentialRow(string Name, string Description, SecretSource Source)
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

/// <summary>Illustrative signing-key row (upload / app-signing key).</summary>
public sealed record SigningKeyRow(
    string Title, string StatusText, bool IsGreen, string Sub, string Meta);

/// <summary>Illustrative certificate-fingerprint row.</summary>
public sealed record FingerprintRow(string Label, string Algorithm, string Value, bool IsAccent);
