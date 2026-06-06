using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LaunchFast.Core.Env;
using LaunchFast.Core.Models;

namespace LaunchFast.App.ViewModels;

/// <summary>
/// Content view-model for a project's "Secrets &amp; credentials" section. Computes
/// the relevant secret names from the project's fastlane config (via the shared
/// <see cref="ProjectSecretScanner"/>), and for each resolves a status + source
/// using the same precedence a lane run uses: process / CI env → <c>.env*</c>
/// files → macOS Keychain. Backed by real data; the secret store and process-env
/// reader are injectable so tests run without a real Keychain or ambient env.
/// </summary>
public partial class SecretsSectionViewModel : ObservableObject
{
    /// <summary>Human descriptions for well-known secret keys.</summary>
    static readonly IReadOnlyDictionary<string, string> Descriptions =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["MATCH_PASSWORD"] = "Passphrase that decrypts the match certificates repo",
            ["APPLE_ID"] = "Apple ID email used for App Store Connect",
            ["APP_STORE_CONNECT_API_KEY_PATH"] = "App Store Connect API key (.p8)",
            ["MATCH_GIT_URL"] = "Git URL of the match certificates repository",
            ["MATCH_GIT_BASIC_AUTHORIZATION"] = "Base64 basic-auth token for the match repo",
            ["MATCH_KEYCHAIN_PASSWORD"] = "Password for the temporary match keychain",
            ["FASTLANE_APP_SPECIFIC_PASSWORD"] = "App-specific password for your Apple ID",
            ["FASTLANE_PASSWORD"] = "Apple ID password used by fastlane",
            ["FASTLANE_SESSION"] = "Reusable App Store Connect session cookie",
            ["FASTLANE_TEAM_ID"] = "Apple Developer team identifier",
            ["ITC_TEAM_ID"] = "App Store Connect team identifier",
            ["API_TOKEN"] = "API token used by deploy scripts",
        };

    readonly Project _project;
    readonly ISecretStore _secrets;
    readonly Func<string, string?> _readProcessEnv;

    /// <summary>
    /// Opens an editor for a secret and persists it. Set by the view to show the
    /// real dialog; tests inject a fake that writes straight to the store. May be
    /// null (then AddOrEdit is a no-op).
    /// </summary>
    public Func<SecretRowViewModel, Task>? Editor { get; set; }

    public SecretsSectionViewModel(
        Project project,
        ISecretStore secrets,
        Func<string, string?>? readProcessEnv = null)
    {
        _project = project;
        _secrets = secrets;
        _readProcessEnv = readProcessEnv ?? Environment.GetEnvironmentVariable;
        Load();
    }

    /// <summary>Stable id used for Keychain lookups (matches the lane run + resolver).</summary>
    public string ProjectId => _project.Path;

    /// <summary>The secret store, exposed so the editor dialog can write into it.</summary>
    public ISecretStore Store => _secrets;

    public ObservableCollection<SecretRowViewModel> Secrets { get; } = new();

    [ObservableProperty]
    private int _missingCount;

    /// <summary>Whether any secret is missing (drives banner visibility).</summary>
    public bool HasMissing => MissingCount > 0;

    /// <summary>"N missing" pill text.</summary>
    public string MissingPillText => $"{MissingCount} MISSING";

    /// <summary>Banner headline, e.g. "2 secrets are missing.".</summary>
    public string BannerText => MissingCount == 1
        ? "1 secret is missing."
        : $"{MissingCount} secrets are missing.";

    /// <summary>The missing key names, for the banner body line.</summary>
    public ObservableCollection<string> MissingNames { get; } = new();

    // ---- Auth segmented control (informational only for now) -----------------
    [ObservableProperty]
    private bool _useAscApiKey = true;

    public bool UseAppleId
    {
        get => !UseAscApiKey;
        set => UseAscApiKey = !value;
    }

    partial void OnUseAscApiKeyChanged(bool value) => OnPropertyChanged(nameof(UseAppleId));

    // ---- Reveal all ----------------------------------------------------------
    [ObservableProperty]
    private bool _revealAll;

    partial void OnRevealAllChanged(bool value)
    {
        foreach (var row in Secrets) row.SetRevealed(value);
    }

    [RelayCommand]
    void ToggleRevealAll() => RevealAll = !RevealAll;

    /// <summary>
    /// (Re)computes the secret rows from the project's fastlane config + current
    /// env files + secret store. Safe to call repeatedly (e.g. after a write).
    /// </summary>
    public void Load()
    {
        var scan = ProjectSecretScanner.Scan(_project);

        Secrets.Clear();
        MissingNames.Clear();
        var missing = 0;

        foreach (var name in scan.RequiredSecrets)
        {
            var (source, value) = Resolve(name, scan.FromFiles);
            Secrets.Add(new SecretRowViewModel(name, DescribeOf(name), source, value)
            {
                IsRevealed = RevealAll,
            });

            if (source == SecretSource.None)
            {
                missing++;
                MissingNames.Add(name);
            }
        }

        MissingCount = missing;
        OnPropertyChanged(nameof(HasMissing));
        OnPropertyChanged(nameof(MissingPillText));
        OnPropertyChanged(nameof(BannerText));
    }

    /// <summary>
    /// Resolves a secret's source + value in run precedence order: process / CI
    /// env first, then a project <c>.env*</c> file, then the Keychain.
    /// </summary>
    (SecretSource Source, string? Value) Resolve(
        string name, IReadOnlyDictionary<string, string> fromFiles)
    {
        var ci = _readProcessEnv(name);
        if (!string.IsNullOrEmpty(ci)) return (SecretSource.CiEnv, ci);

        if (fromFiles.TryGetValue(name, out var fileVal)) return (SecretSource.EnvFile, fileVal);

        var kc = _secrets.Get(ProjectId, name);
        if (kc is not null) return (SecretSource.Keychain, kc);

        return (SecretSource.None, null);
    }

    static string DescribeOf(string name) =>
        Descriptions.TryGetValue(name, out var d) ? d : "Secret referenced by the fastlane configuration";

    [RelayCommand]
    async Task AddOrEdit(SecretRowViewModel? row)
    {
        if (row is null || Editor is null) return;
        await Editor(row).ConfigureAwait(true);
        Load();
    }

    /// <summary>
    /// Test convenience: persist a value to the store and refresh, mimicking what
    /// the editor does without a dialog.
    /// </summary>
    public void SetSecret(string name, string value)
    {
        _secrets.Set(ProjectId, name, value);
        Load();
    }
}
