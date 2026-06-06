using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LaunchFast.Core.Models;
using LaunchFast.Core.Stores;

namespace LaunchFast.App.ViewModels;

/// <summary>
/// Content view-model for a project's "TestFlight" section.
///
/// REAL: when an App Store Connect API key is configured
/// (<c>APP_STORE_CONNECT_API_KEY_PATH</c>) the build status (version/build,
/// processing state, export compliance, expiry), beta groups and the tester
/// table are read live from App Store Connect. The "What to test" notes prefer the
/// build's <c>whatsNew</c>, falling back to the project's on-disk release notes.
/// "Distribute build" runs the genuine <c>beta</c> fastlane lane.
///
/// UNAVAILABLE: with no key (or any fetch failure) the section shows an honest
/// empty state prompting the user to connect a key. Testers/builds are never
/// fabricated.
/// </summary>
public partial class TestFlightSectionViewModel : ObservableObject
{
    readonly Action<Platform, string>? _runLane;
    readonly Func<bool> _hasBetaLane;
    readonly IAppStoreConnectClient? _asc;
    readonly string? _bundleId;

    public TestFlightSectionViewModel(
        Project project,
        IAppStoreConnectClient? asc = null,
        Action<Platform, string>? runLane = null,
        Func<bool>? hasBetaLane = null)
    {
        _asc = asc;
        _runLane = runLane;
        _hasBetaLane = hasBetaLane ?? (() => false);
        _bundleId = ReadBundleId(project);
        _fallbackNotes = ReadFallbackNotes(project);

        Groups = new ObservableCollection<GroupRow>();
        Testers = new ObservableCollection<TesterRow>();

        // No client configured → honest "connect a key" state, no network.
        if (_asc is null || string.IsNullOrWhiteSpace(_bundleId))
        {
            IsAvailable = false;
            ReleaseNotes = _fallbackNotes;
            // Fire-and-forget is unnecessary; surface the unavailable state now.
            return;
        }

        // Real fetch: non-blocking, marshals results back to the UI thread.
        _ = LoadAsync();
    }

    /// <summary>
    /// Fetches TestFlight data from App Store Connect and applies it. Safe to await
    /// directly in tests; never throws (failures collapse to the unavailable state).
    /// </summary>
    public async Task LoadAsync(CancellationToken ct = default)
    {
        if (_asc is null || string.IsNullOrWhiteSpace(_bundleId))
        {
            Apply(TestFlightInfo.Empty, available: false);
            return;
        }

        TestFlightInfo info;
        bool available;
        try
        {
            info = await _asc.GetTestFlightAsync(_bundleId, ct).ConfigureAwait(false);
            available = true;
        }
        catch
        {
            info = TestFlightInfo.Empty;
            available = false;
        }

        if (Dispatcher.UIThread.CheckAccess())
        {
            Apply(info, available);
        }
        else
        {
            Dispatcher.UIThread.Post(() => Apply(info, available));
        }
    }

    void Apply(TestFlightInfo info, bool available)
    {
        IsAvailable = available;

        if (info.LatestBuild is { } b)
        {
            BuildVersion = $"{b.Version} ({b.BuildNumber})";
            Processing = ProcessingText(b.ProcessingState);
            ExportCompliance = b.ExpiredCompliance switch
            {
                true => "Missing",
                false => "Provided",
                null => "—",
            };
            SyncedText = b.ExpiresText ?? "—";
            ReleaseNotes = string.IsNullOrWhiteSpace(b.WhatsToTest) ? _fallbackNotes : b.WhatsToTest;
        }
        else
        {
            BuildVersion = "—";
            Processing = "—";
            ExportCompliance = "—";
            SyncedText = "—";
            ReleaseNotes = _fallbackNotes;
        }

        Groups.Clear();
        foreach (var g in info.Groups)
        {
            Groups.Add(new GroupRow(g.Name, g.IsInternal, g.TesterCount));
        }

        var internalNames = info.Groups
            .Where(g => g.IsInternal)
            .Select(g => g.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        DistributedTo = DistributedToText(info.Groups);

        Testers.Clear();
        foreach (var t in info.Testers)
        {
            var name = $"{t.FirstName} {t.LastName}".Trim();
            if (string.IsNullOrWhiteSpace(name))
            {
                name = t.Email;
            }

            var groupLabel = t.GroupName
                ?? (info.Groups.Count == 1 ? info.Groups[0].Name : "—");
            var isInternal = t.GroupName is not null && internalNames.Contains(t.GroupName);

            Testers.Add(new TesterRow(
                name,
                t.Email,
                groupLabel,
                t.State,
                StateOf(t.State),
                isInternal,
                IsPendingState(t.State)));
        }

        RaiseDerived();
    }

    static string ProcessingText(string state) => state switch
    {
        "VALID" => "Done",
        "PROCESSING" => "Processing",
        "INVALID" => "Invalid",
        "FAILED" => "Failed",
        _ => state,
    };

    static string DistributedToText(IReadOnlyList<BetaGroup> groups)
    {
        if (groups.Count == 0)
        {
            return "—";
        }

        var hasInternal = groups.Any(g => g.IsInternal);
        var hasExternal = groups.Any(g => !g.IsInternal);
        var total = groups.Sum(g => g.TesterCount);

        var scope = (hasInternal, hasExternal) switch
        {
            (true, true) => "Internal + External",
            (true, false) => "Internal",
            (false, true) => "External",
            _ => "—",
        };
        return $"{scope} ({total})";
    }

    static TesterState StateOf(string state) => state switch
    {
        "Installed" or "Accepted" => TesterState.Ok,
        "Invited" => TesterState.Warn,
        _ => TesterState.Neutral,
    };

    static bool IsPendingState(string state) =>
        state is not ("Installed" or "Accepted");

    /// <summary>True when live data was loaded; false → honest "connect a key" state.</summary>
    [ObservableProperty]
    private bool _isAvailable;

    public bool IsUnavailable => !IsAvailable;

    partial void OnIsAvailableChanged(bool value) =>
        OnPropertyChanged(nameof(IsUnavailable));

    /// <summary>Honest message shown when no ASC key is configured / fetch failed.</summary>
    public string UnavailableMessage =>
        "Connect an App Store Connect API key (APP_STORE_CONNECT_API_KEY_PATH) " +
        "to load build status & testers.";

    // ---- subbar --------------------------------------------------------------
    [ObservableProperty]
    private string _syncedText = "—";

    public string VersionBuild => BuildVersion;

    // ---- build status panel --------------------------------------------------
    [ObservableProperty]
    private string _buildVersion = "—";

    [ObservableProperty]
    private string _processing = "—";

    [ObservableProperty]
    private string _exportCompliance = "—";

    [ObservableProperty]
    private string _distributedTo = "—";

    // ---- what to test --------------------------------------------------------
    readonly string _fallbackNotes;

    [ObservableProperty]
    private string _releaseNotes = string.Empty;

    public string NotesCountText => $"{ReleaseNotes.Length} / 4000";

    partial void OnReleaseNotesChanged(string value) =>
        OnPropertyChanged(nameof(NotesCountText));

    partial void OnBuildVersionChanged(string value) =>
        OnPropertyChanged(nameof(VersionBuild));

    public ObservableCollection<GroupRow> Groups { get; }

    public ObservableCollection<TesterRow> Testers { get; }

    // ---- filter segmented control --------------------------------------------
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

    // ---- filter counts (derived from real testers) ---------------------------
    public int CountAll => Testers.Count;
    public int CountInternal => Testers.Count(t => t.IsInternal);
    public int CountExternal => Testers.Count(t => !t.IsInternal && !t.IsPending);
    public int CountPending => Testers.Count(t => t.IsPending);

    void RaiseDerived()
    {
        OnPropertyChanged(nameof(CountAll));
        OnPropertyChanged(nameof(CountInternal));
        OnPropertyChanged(nameof(CountExternal));
        OnPropertyChanged(nameof(CountPending));
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

    static string? ReadBundleId(Project project)
    {
        if (project.IosFastlaneDir is null) return null;
        var appfile = Path.Combine(project.IosFastlaneDir, "Appfile");
        if (!File.Exists(appfile)) return null;

        try
        {
            var id = AppfileReader.AppIdentifier(File.ReadAllText(appfile));
            return string.IsNullOrWhiteSpace(id) || id.Contains("ENV[") ? null : id;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// The project's most recent iOS release notes on disk (first locale that has
    /// any), used as the "What to test" fallback when the build exposes none.
    /// Returns an empty string when there are none — an empty editable box.
    /// </summary>
    static string ReadFallbackNotes(Project project)
    {
        try
        {
            foreach (var locale in StoreMetadataReader.Locales(project, Platform.Ios))
            {
                var listing = StoreMetadataReader.ReadListing(project, Platform.Ios, locale);
                if (!string.IsNullOrWhiteSpace(listing.ReleaseNotes))
                {
                    return listing.ReleaseNotes;
                }
            }
        }
        catch
        {
            // best-effort fallback only
        }
        return string.Empty;
    }
}

public enum TesterFilter { All, Internal, External, Pending }

/// <summary>State of a tester (drives the status pill tint).</summary>
public enum TesterState { Ok, Warn, Neutral }

/// <summary>A real beta group row.</summary>
public sealed record GroupRow(string Name, bool IsInternal, int TesterCount)
{
    public string ScopeText => IsInternal ? "Internal" : "External";
    public string CountText => TesterCount == 1 ? "1 tester" : $"{TesterCount} testers";
}

/// <summary>A real tester row mapped from App Store Connect.</summary>
public sealed record TesterRow(
    string Name, string Email, string Group, string StatusText,
    TesterState State, bool IsInternal, bool IsPending)
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
