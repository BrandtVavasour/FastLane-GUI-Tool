using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LaunchFast.Core.Models;

namespace LaunchFast.App.ViewModels;

/// <summary>
/// Content view-model for a project's "TestFlight" section.
///
/// SHELL / PLACEHOLDER: a faithful themed shell. The build status, release notes
/// and tester table are <b>illustrative</b> placeholder data (see
/// <see cref="IsPlaceholder"/>) shown until a real App Store Connect backend lands.
/// The only genuinely wired action is "Distribute build", which triggers the
/// project's real <c>beta</c> fastlane lane (disabled when that lane is absent).
/// </summary>
public partial class TestFlightSectionViewModel : ObservableObject
{
    readonly Action<Platform, string>? _runLane;
    readonly Func<bool> _hasBetaLane;

    public TestFlightSectionViewModel(
        Project project,
        Action<Platform, string>? runLane = null,
        Func<bool>? hasBetaLane = null)
    {
        _ = project; // reserved for a future real backend (version/build from ASC)
        _runLane = runLane;
        _hasBetaLane = hasBetaLane ?? (() => false);

        Testers = new ObservableCollection<TesterRow>
        {
            new("Maya Chen", "maya@example.io", "Internal", "Active", TesterState.Ok, "12", "2h ago"),
            new("Priya Nair", "priya@example.io", "Internal", "Active", TesterState.Ok, "8", "Yesterday"),
            new("Marco Reyes", "marco@example.com", "Beta Crew", "Active", TesterState.Ok, "3", "3d ago"),
            new("Lena Fischer", "lena@example.com", "Beta Crew", "Invited", TesterState.Warn, "0", "—"),
            new("Sam Okoye", "sam@example.com", "External", "Pending", TesterState.Neutral, "0", "—"),
        };
    }

    /// <summary>Marks this section's list data as illustrative placeholder, not live.</summary>
    public bool IsPlaceholder => true;

    // ---- subbar (placeholder) ------------------------------------------------
    public string VersionBuild => "1.4.2 (18)";
    public string SyncedText => "expires in 90 days";

    // ---- build status panel (placeholder) ------------------------------------
    public string BuildVersion => "1.4.2 (18)";
    public string Processing => "Done";
    public string ExportCompliance => "Provided";
    public string DistributedTo => "Internal + External (5)";

    // ---- what to test --------------------------------------------------------
    public string ReleaseNotes { get; } =
        "• Fixed a crash when opening the project switcher on cold start.\n" +
        "• Faster lane runs: output now streams without buffering.\n" +
        "• New Signing section shows certificate + profile expiry at a glance.\n" +
        "• Various polish across the dashboard and settings.\n\n" +
        "Please focus testing on the new onboarding flow and report anything odd.";

    public string NotesCountText => $"{ReleaseNotes.Length} / 4000";

    public ObservableCollection<TesterRow> Testers { get; }

    // ---- filter segmented control (informational for now) --------------------
    [ObservableProperty]
    private TesterFilter _filter = TesterFilter.All;

    public bool FilterAll
    {
        get => Filter == TesterFilter.All;
        set { if (value) Filter = TesterFilter.All; }
    }

    public bool FilterInternal
    {
        get => Filter == TesterFilter.Internal;
        set { if (value) Filter = TesterFilter.Internal; }
    }

    public bool FilterExternal
    {
        get => Filter == TesterFilter.External;
        set { if (value) Filter = TesterFilter.External; }
    }

    public bool FilterPending
    {
        get => Filter == TesterFilter.Pending;
        set { if (value) Filter = TesterFilter.Pending; }
    }

    partial void OnFilterChanged(TesterFilter value)
    {
        OnPropertyChanged(nameof(FilterAll));
        OnPropertyChanged(nameof(FilterInternal));
        OnPropertyChanged(nameof(FilterExternal));
        OnPropertyChanged(nameof(FilterPending));
    }

    /// <summary>True when the project exposes the <c>beta</c> iOS lane.</summary>
    public bool CanDistribute => _hasBetaLane();

    /// <summary>Runs the real <c>beta</c> lane via the shell's lane runner.</summary>
    [RelayCommand]
    void Distribute()
    {
        if (!CanDistribute) return;
        _runLane?.Invoke(Platform.Ios, "beta");
    }
}

public enum TesterFilter { All, Internal, External, Pending }

/// <summary>State of a placeholder tester (drives the status pill tint).</summary>
public enum TesterState { Ok, Warn, Neutral }

/// <summary>Illustrative tester row for the TestFlight shell.</summary>
public sealed record TesterRow(
    string Name, string Email, string Group, string StatusText,
    TesterState State, string Sessions, string LastSession)
{
    public bool IsOk => State == TesterState.Ok;
    public bool IsWarn => State == TesterState.Warn;
    public bool IsNeutral => State == TesterState.Neutral;

    public string Initials
    {
        get
        {
            var parts = Name.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0) return "?";
            var first = parts[0][..1];
            var last = parts.Length > 1 ? parts[^1][..1] : "";
            return (first + last).ToUpperInvariant();
        }
    }
}
