using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LaunchFast.Core.Models;
using LaunchFast.Core.Stores;

namespace LaunchFast.App.ViewModels;

/// <summary>
/// Content view-model for a project's "Signing &amp; Certificates" section.
///
/// SHELL / PLACEHOLDER: this screen is a faithful themed shell. The certificate,
/// provisioning-profile, device and match-storage rows are <b>illustrative</b>
/// placeholder data (see <see cref="IsPlaceholder"/>) shown until a real signing
/// backend lands — none of it is queried from Apple or a match repo yet. The only
/// genuinely wired piece is the "Run match" action, which triggers the project's
/// real <c>sync_certificates</c> fastlane lane (and is disabled when that lane is
/// absent from the parsed Fastfile).
/// </summary>
public partial class SigningSectionViewModel : ObservableObject
{
    readonly Project _project;
    readonly Action<Platform, string>? _runLane;
    readonly Func<bool> _hasSyncLane;

    public SigningSectionViewModel(
        Project project,
        Action<Platform, string>? runLane = null,
        Func<bool>? hasSyncLane = null)
    {
        _project = project;
        _runLane = runLane;
        _hasSyncLane = hasSyncLane ?? (() => false);

        BundleId = ReadBundleId(project) ?? "com.example.app";

        Certificates = new ObservableCollection<SigningCertRow>
        {
            new("Apple Distribution", "Valid", IsValid: true,
                "Serial 1A2B3C4D5E · Team JAB Technologies (7F8G9H)", "expires in 280d"),
            new("Apple Development", "Valid", IsValid: true,
                "Serial 6E7F8A9B0C · Team JAB Technologies (7F8G9H)", "expires in 142d"),
        };

        Profiles = new ObservableCollection<SigningProfileRow>
        {
            new("App Store", "Valid", ProfileState.Ok,
                "match AppStore com.example.app · Distribution", "expires in 280d"),
            new("Ad Hoc", "Valid", ProfileState.Ok,
                "match AdHoc com.example.app · 24 devices", "expires in 280d"),
            new("Development", "Expires soon", ProfileState.Warn,
                "match Development com.example.app · 24 devices", "expires in 11d"),
        };

        Devices = new ObservableCollection<SigningDeviceRow>
        {
            new("iPhone 15 Pro — Dev", "00008130-001A2B3C0E84001E"),
            new("iPad Air (M2) — QA", "00008112-000C4D5E1A23002A"),
        };
    }

    /// <summary>Marks this section's list data as illustrative placeholder, not live.</summary>
    public bool IsPlaceholder => true;

    /// <summary>App bundle id from the iOS Appfile, or a placeholder when absent.</summary>
    public string BundleId { get; }

    // ---- match storage (placeholder) -----------------------------------------
    public string MatchRepo => "git@github.com:jabtech/certificates.git";
    public string MatchBranch => "main";
    public string MatchStorage => "git (encrypted)";
    public string MatchLastSynced => "—";

    // ---- subbar --------------------------------------------------------------
    public string SyncedText => "Synced —";

    public ObservableCollection<SigningCertRow> Certificates { get; }
    public ObservableCollection<SigningProfileRow> Profiles { get; }
    public ObservableCollection<SigningDeviceRow> Devices { get; }

    /// <summary>True when the project exposes the <c>sync_certificates</c> iOS lane.</summary>
    public bool CanRunMatch => _hasSyncLane();

    /// <summary>Runs the real <c>sync_certificates</c> lane via the shell's lane runner.</summary>
    [RelayCommand]
    void RunMatch()
    {
        if (!CanRunMatch) return;
        _runLane?.Invoke(Platform.Ios, "sync_certificates");
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
}

/// <summary>Illustrative certificate row for the Signing shell.</summary>
public sealed record SigningCertRow(
    string Title, string StatusText, bool IsValid, string Sub, string ExpiresMeta);

/// <summary>State of a placeholder provisioning profile (drives the status pill).</summary>
public enum ProfileState { Ok, Warn, Bad }

/// <summary>Illustrative provisioning-profile row for the Signing shell.</summary>
public sealed record SigningProfileRow(
    string Title, string StatusText, ProfileState State, string Sub, string ExpiresMeta)
{
    public bool IsOk => State == ProfileState.Ok;
    public bool IsWarn => State == ProfileState.Warn;
    public bool IsBad => State == ProfileState.Bad;
}

/// <summary>Illustrative registered-device row for the Signing shell.</summary>
public sealed record SigningDeviceRow(string Name, string Udid)
{
    public string Initials
    {
        get
        {
            var parts = Name.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0) return "?";
            var first = parts[0][..1];
            var last = parts[^1].Length > 0 && char.IsLetter(parts[^1][0]) ? parts[^1][..1] : "";
            return (first + last).ToUpperInvariant();
        }
    }
}
