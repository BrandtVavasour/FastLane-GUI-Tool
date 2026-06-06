using System.Collections.ObjectModel;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LaunchFast.Core.History;
using LaunchFast.Core.Models;

namespace LaunchFast.App.ViewModels;

/// <summary>
/// Content view-model for a project's "History" section: a REAL run history &amp;
/// audit log. Reads persisted <see cref="RunRecord"/>s from the shared
/// <see cref="RunHistoryStore"/> (the same instance the Lanes VM records into), so a
/// lane run shows up here. Surfaces aggregate stats, a filterable list of run rows
/// (each expandable to a mini-terminal of the output tail), and a "Re-run lane"
/// action wired back to the shell's real lane runner.
///
/// Stats are computed against an injected <c>nowUtc</c> so the screen is deterministic
/// in tests; the app passes a real <see cref="DateTime.UtcNow"/>.
/// </summary>
public partial class RunHistorySectionViewModel : ObservableObject
{
    readonly RunHistoryStore _history;
    readonly string _projectId;
    readonly Action<Platform, string>? _runLane;
    readonly Func<DateTime> _nowUtc;

    IReadOnlyList<RunRecord> _all = [];

    public RunHistorySectionViewModel(
        RunHistoryStore history,
        string projectId,
        Action<Platform, string>? runLane = null,
        Func<DateTime>? nowUtc = null)
    {
        _history = history;
        _projectId = projectId;
        _runLane = runLane;
        _nowUtc = nowUtc ?? (() => DateTime.UtcNow);

        Rows = new ObservableCollection<RunHistoryRowViewModel>();
        LaneFilters = new ObservableCollection<string>();
        StatusFilters = new ObservableCollection<string> { AllStatus, "Succeeded", "Failed" };

        Reload();
    }

    public ObservableCollection<RunHistoryRowViewModel> Rows { get; }

    /// <summary>The project identifier used to scope history records.</summary>
    public string ProjectId => _projectId;

    // ---- stats ---------------------------------------------------------------

    [ObservableProperty] private string _runsLast30Days = "0";
    [ObservableProperty] private string _successRate = "—";
    [ObservableProperty] private string _medianDuration = "—";
    [ObservableProperty] private string _lastFailure = "Never";

    // ---- filters -------------------------------------------------------------

    const string AllLanes = "All lanes";
    const string AllStatus = "All status";

    public ObservableCollection<string> LaneFilters { get; }
    public ObservableCollection<string> StatusFilters { get; }

    [ObservableProperty] private string _selectedLaneFilter = AllLanes;
    [ObservableProperty] private string _selectedStatusFilter = AllStatus;

    partial void OnSelectedLaneFilterChanged(string value) => ApplyFilter();
    partial void OnSelectedStatusFilterChanged(string value) => ApplyFilter();

    // ---- empty state ---------------------------------------------------------

    public bool IsEmpty => _all.Count == 0;
    public bool HasRows => Rows.Count > 0;

    public string EmptyStateText =>
        "No runs recorded yet — run a lane from Lanes or Fastfile.";

    /// <summary>Result of the last Export logs action (or null when not yet run).</summary>
    [ObservableProperty] private string? _exportStatus;

    /// <summary>
    /// Optional callback set by the view so it can open a native save-as dialog and then
    /// call <see cref="WriteExport"/> with the chosen path.  When null the command falls
    /// back to writing straight to a temp file (useful in tests / headless contexts).
    /// </summary>
    public Action? RequestExport { get; set; }

    // ---- loading -------------------------------------------------------------

    /// <summary>Re-reads the store and recomputes stats + rows. Call when re-entering the section.</summary>
    public void Reload()
    {
        _all = _history.List(_projectId);
        ComputeStats();
        RebuildLaneFilters();
        ApplyFilter();
        OnPropertyChanged(nameof(IsEmpty));
    }

    void ComputeStats()
    {
        var s = _history.Stats(_projectId, _nowUtc());
        RunsLast30Days = s.RunsLast30Days.ToString();
        SuccessRate = _all.Count == 0 ? "—" : $"{Math.Round(s.SuccessRatePercent)}%";
        MedianDuration = s.MedianDuration is { } d ? RelativeTime.Duration(d) : "—";
        LastFailure = s.LastFailureUtc is { } f ? RelativeTime.Ago(f, _nowUtc()) : "Never";
    }

    void RebuildLaneFilters()
    {
        var current = SelectedLaneFilter;
        LaneFilters.Clear();
        LaneFilters.Add(AllLanes);
        foreach (var lane in _all
                     .Select(r => LaneLabel(r.Platform, r.LaneName))
                     .Distinct()
                     .OrderBy(l => l, StringComparer.Ordinal))
            LaneFilters.Add(lane);

        if (!LaneFilters.Contains(current)) SelectedLaneFilter = AllLanes;
    }

    void ApplyFilter()
    {
        Rows.Clear();
        foreach (var record in _all)
        {
            if (SelectedLaneFilter != AllLanes &&
                LaneLabel(record.Platform, record.LaneName) != SelectedLaneFilter)
                continue;

            if (SelectedStatusFilter == "Succeeded" && record.Status != RunStatus.Succeeded) continue;
            if (SelectedStatusFilter == "Failed" && record.Status != RunStatus.Failed) continue;

            Rows.Add(new RunHistoryRowViewModel(record, _nowUtc()));
        }

        OnPropertyChanged(nameof(HasRows));
    }

    static string LaneLabel(Platform platform, string laneName) =>
        $"{(platform == Platform.Ios ? "ios" : "android")} {laneName}";

    // ---- actions -------------------------------------------------------------

    public bool CanRerun => _runLane is not null;

    /// <summary>Re-runs the lane of a given history row through the shell's real runner.</summary>
    [RelayCommand]
    void Rerun(RunHistoryRowViewModel? row)
    {
        if (row is null) return;
        _runLane?.Invoke(row.Platform, row.LaneName);
    }

    /// <summary>Toggles a row's expanded detail (mini-terminal).</summary>
    [RelayCommand]
    static void ToggleRow(RunHistoryRowViewModel? row)
    {
        if (row is not null) row.IsExpanded = !row.IsExpanded;
    }

    /// <summary>
    /// Produces the export text for the currently-visible rows: a header block per row
    /// with lane / status / when / duration followed by the output tail.
    /// Returns a header-only string when there are no rows.
    /// Pure — no I/O, safe to call in tests.
    /// </summary>
    public string BuildExportContent()
    {
        var sb = new StringBuilder();
        sb.AppendLine("LaunchFast Run History Export");
        sb.AppendLine($"Generated: {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC");
        sb.AppendLine();

        if (Rows.Count == 0)
        {
            sb.AppendLine("(no runs match the current filter)");
            return sb.ToString();
        }

        foreach (var row in Rows)
        {
            sb.AppendLine($"=== {row.LaneLabel} · {row.StatusText} · {row.WhenText} · {row.DurationText} ===");
            sb.AppendLine(row.OutputTail);
            sb.AppendLine();
        }

        return sb.ToString();
    }

    /// <summary>
    /// Writes <see cref="BuildExportContent"/> to <paramref name="path"/> and updates
    /// <see cref="ExportStatus"/> with a success or failure message.
    /// </summary>
    public void WriteExport(string path)
    {
        try
        {
            File.WriteAllText(path, BuildExportContent());
            ExportStatus = $"Exported {Rows.Count} run(s) → {path}";
        }
        catch (Exception ex)
        {
            ExportStatus = $"Export failed: {ex.Message}";
        }
    }

    /// <summary>
    /// Invokes <see cref="RequestExport"/> so the view can open a native save-as dialog
    /// and subsequently call <see cref="WriteExport"/>.  If no callback is registered
    /// (headless / tests) the export is written directly to a temp file.
    /// </summary>
    [RelayCommand]
    void ExportLogs()
    {
        if (RequestExport is not null)
        {
            RequestExport();
            return;
        }

        // Fallback for headless contexts: write to temp dir.
        if (Rows.Count == 0)
        {
            ExportStatus = "Nothing to export.";
            return;
        }

        var dir = Path.Combine(Path.GetTempPath(), "launchfast-exports");
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, $"run-logs-{DateTime.UtcNow:yyyyMMdd-HHmmss}.txt");
        WriteExport(path);
    }
}

/// <summary>One run row in the history list, projected from a persisted <see cref="RunRecord"/>.</summary>
public sealed partial class RunHistoryRowViewModel : ObservableObject
{
    public RunHistoryRowViewModel(RunRecord record, DateTime nowUtc)
    {
        Platform = record.Platform;
        LaneName = record.LaneName;
        LaneLabel = $"{(record.Platform == Platform.Ios ? "ios" : "android")} {record.LaneName}";
        Succeeded = record.Status == RunStatus.Succeeded;
        ResultSummary = record.ResultSummary;
        Trigger = string.IsNullOrWhiteSpace(record.Trigger) ? "Local" : record.Trigger;
        Initials = InitialsFor(Trigger);
        AvatarColor = AvatarColorFor(Trigger);
        DurationText = RelativeTime.Duration(record.Duration);
        WhenText = RelativeTime.Ago(record.StartedUtc, nowUtc);
        OutputTail = record.OutputTail;
        StatusText = Succeeded ? "Succeeded" : "Failed";
    }

    public Platform Platform { get; }
    public string LaneName { get; }
    public string LaneLabel { get; }
    public bool Succeeded { get; }
    public bool Failed => !Succeeded;
    public string ResultSummary { get; }
    public string Trigger { get; }
    public string Initials { get; }
    public string AvatarColor { get; }
    public string DurationText { get; }
    public string WhenText { get; }
    public string OutputTail { get; }
    public string StatusText { get; }

    public bool HasOutput => !string.IsNullOrWhiteSpace(OutputTail);

    [ObservableProperty]
    private bool _isExpanded;

    static string InitialsFor(string trigger)
    {
        var parts = trigger.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0) return "·";
        if (parts.Length == 1)
            return parts[0].Length >= 2 ? parts[0][..2].ToUpperInvariant() : parts[0].ToUpperInvariant();
        return $"{char.ToUpperInvariant(parts[0][0])}{char.ToUpperInvariant(parts[^1][0])}";
    }

    // Deterministic avatar colour from the trigger string, drawn from the design palette.
    static readonly string[] Palette =
        { "#2E5BD8", "#6E7681", "#34C759", "#BF5AF2", "#FF9F0A", "#FF453A" };

    static string AvatarColorFor(string trigger)
    {
        var sum = trigger.Aggregate(0, (acc, c) => acc + c);
        return Palette[Math.Abs(sum) % Palette.Length];
    }
}

/// <summary>Small formatting helpers for relative "when" strings and durations.</summary>
public static class RelativeTime
{
    /// <summary>Renders the gap between <paramref name="instantUtc"/> and now as "now/12m ago/2d ago".</summary>
    public static string Ago(DateTime instantUtc, DateTime nowUtc)
    {
        var span = nowUtc - instantUtc;
        if (span < TimeSpan.Zero) span = TimeSpan.Zero;

        if (span.TotalSeconds < 45) return "now";
        if (span.TotalMinutes < 60) return $"{Math.Max(1, (int)span.TotalMinutes)}m ago";
        if (span.TotalHours < 24) return $"{(int)span.TotalHours}h ago";
        if (span.TotalDays < 7) return $"{(int)span.TotalDays}d ago";
        if (span.TotalDays < 30) return $"{(int)(span.TotalDays / 7)}w ago";
        if (span.TotalDays < 365) return $"{(int)(span.TotalDays / 30)}mo ago";
        return $"{(int)(span.TotalDays / 365)}y ago";
    }

    /// <summary>Renders a duration compactly: "38s" or "1m 47s".</summary>
    public static string Duration(TimeSpan duration)
    {
        if (duration < TimeSpan.Zero) duration = TimeSpan.Zero;
        var totalSeconds = (int)duration.TotalSeconds;
        if (totalSeconds < 60) return $"{totalSeconds}s";
        var minutes = totalSeconds / 60;
        var seconds = totalSeconds % 60;
        return $"{minutes}m {seconds:D2}s";
    }
}
