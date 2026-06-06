using CommunityToolkit.Mvvm.ComponentModel;

namespace LaunchFast.App.ViewModels.Wizard;

/// <summary>
/// iOS step: bundle id / team id plus the optional App Store Connect, match and
/// dart-define values. The values are referenced from generated files via
/// <c>ENV[...]</c> and the non-empty ones are stored as Keychain secrets on apply.
/// </summary>
public sealed partial class WizardIosStepViewModel : ObservableObject
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsValid))]
    private string? _bundleId;

    [ObservableProperty]
    private string? _appleId;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsValid))]
    private string? _teamId;

    [ObservableProperty]
    private string? _itcTeamId;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsValid))]
    private string? _matchGitUrl;

    [ObservableProperty]
    private string? _matchPassword;

    [ObservableProperty]
    private string? _appStoreConnectKeyPath;

    [ObservableProperty]
    private string? _apiUrl;

    [ObservableProperty]
    private string? _apiToken;

    /// <summary>Set by the wizard from the chosen lanes (true when a lane needs match).</summary>
    public bool RequiresMatch { get; set; }

    public bool IsValid =>
        !string.IsNullOrWhiteSpace(BundleId)
        && !string.IsNullOrWhiteSpace(TeamId)
        && (!RequiresMatch || !string.IsNullOrWhiteSpace(MatchGitUrl));
}
