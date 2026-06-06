using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LaunchFast.Core.Models;
using LaunchFast.Core.Parsing;

namespace LaunchFast.App.ViewModels;

/// <summary>Which face of a lane the detail pane shows.</summary>
public enum FastfileView { Steps, Source }

/// <summary>
/// Content view-model for a project's "Fastfile" section: a REAL Fastfile / Lane
/// inspector. Reads the project's iOS + Android <c>Fastfile</c>s from disk and runs
/// them through <see cref="FastfileParser.ParseDetailed"/>, exposing each public
/// lane (grouped by platform for the rail) with its parsed steps and raw Ruby source.
///
/// A <see cref="SelectedLane"/> drives the detail pane, toggled between a Steps view
/// and a Source view by <see cref="View"/>. <see cref="RunSelectedLaneCommand"/> is
/// wired to the shell's real lane runner (via the <c>runLane</c> delegate); since the
/// inspected lane is the project's own, it is always runnable. When the project has
/// no Fastfiles, <see cref="IsEmpty"/> drives an honest empty state.
/// </summary>
public partial class FastfileSectionViewModel : ObservableObject
{
    readonly Action<Platform, string>? _runLane;

    public FastfileSectionViewModel(
        Project project,
        Action<Platform, string>? runLane = null)
    {
        _runLane = runLane;

        Lanes = new ObservableCollection<LaneRowViewModel>();
        IosLanes = new ObservableCollection<LaneRowViewModel>();
        AndroidLanes = new ObservableCollection<LaneRowViewModel>();

        Load(project, Platform.Ios, project.IosFastlaneDir, IosLanes);
        Load(project, Platform.Android, project.AndroidFastlaneDir, AndroidLanes);

        SelectedLane = Lanes.FirstOrDefault();
    }

    void Load(Project project, Platform platform, string? fastlaneDir,
        ObservableCollection<LaneRowViewModel> bucket)
    {
        _ = project;
        if (fastlaneDir is null) return;

        var path = Path.Combine(fastlaneDir, "Fastfile");
        if (!File.Exists(path)) return;

        string text;
        try { text = File.ReadAllText(path); }
        catch (IOException) { return; }
        catch (UnauthorizedAccessException) { return; }

        foreach (var detail in FastfileParser.ParseDetailed(text, platform))
        {
            var row = new LaneRowViewModel(detail);
            bucket.Add(row);
            Lanes.Add(row);
        }
    }

    /// <summary>All lanes across both platforms (selection lives here).</summary>
    public ObservableCollection<LaneRowViewModel> Lanes { get; }

    public ObservableCollection<LaneRowViewModel> IosLanes { get; }
    public ObservableCollection<LaneRowViewModel> AndroidLanes { get; }

    public bool HasIos => IosLanes.Count > 0;
    public bool HasAndroid => AndroidLanes.Count > 0;

    /// <summary>True when no Fastfile (or no public lanes) was found on disk.</summary>
    public bool IsEmpty => Lanes.Count == 0;

    public string EmptyStateText =>
        "No fastlane/Fastfile found for this project — add one under ios/fastlane or android/fastlane.";

    public string LaneCountText
    {
        get
        {
            var n = Lanes.Count;
            return $"{n} lane{(n == 1 ? "" : "s")}";
        }
    }

    // ---- selection -----------------------------------------------------------

    [ObservableProperty]
    private LaneRowViewModel? _selectedLane;

    partial void OnSelectedLaneChanged(LaneRowViewModel? value)
    {
        foreach (var row in Lanes) row.IsSelected = row == value;

        OnPropertyChanged(nameof(HasSelection));
        OnPropertyChanged(nameof(SelectedTitle));
        OnPropertyChanged(nameof(SelectedPlatformLabel));
        OnPropertyChanged(nameof(SelectedInvocation));
        OnPropertyChanged(nameof(SelectedSource));
        OnPropertyChanged(nameof(SelectedSteps));
        OnPropertyChanged(nameof(CanRunSelected));
        RunSelectedLaneCommand.NotifyCanExecuteChanged();
    }

    /// <summary>Selects a lane (drives the rail highlight + detail pane).</summary>
    [RelayCommand]
    void SelectLane(LaneRowViewModel? lane)
    {
        if (lane is not null) SelectedLane = lane;
    }

    public bool HasSelection => SelectedLane is not null;

    /// <summary>"lane :beta" header text for the detail pane.</summary>
    public string SelectedTitle =>
        SelectedLane is { } l ? $"lane :{l.Name}" : string.Empty;

    public string SelectedPlatformLabel =>
        SelectedLane?.Platform switch
        {
            Platform.Ios => "iOS",
            Platform.Android => "Android",
            _ => string.Empty,
        };

    public bool SelectedIsIos => SelectedLane?.Platform == Platform.Ios;

    /// <summary>"$ fastlane ios beta · 2 steps" line under the title.</summary>
    public string SelectedInvocation
    {
        get
        {
            if (SelectedLane is not { } l) return string.Empty;
            var plat = l.Platform == Platform.Ios ? "ios" : "android";
            var n = l.Steps.Count;
            return $"$ fastlane {plat} {l.Name} · {n} step{(n == 1 ? "" : "s")}";
        }
    }

    public string SelectedSource => SelectedLane?.Source ?? string.Empty;

    public IReadOnlyList<LaneStepViewModel> SelectedSteps =>
        SelectedLane?.Steps ?? Array.Empty<LaneStepViewModel>();

    public bool SelectedHasSteps => SelectedSteps.Count > 0;

    // ---- view toggle ---------------------------------------------------------

    [ObservableProperty]
    private FastfileView _view = FastfileView.Steps;

    partial void OnViewChanged(FastfileView value)
    {
        OnPropertyChanged(nameof(IsStepsView));
        OnPropertyChanged(nameof(IsSourceView));
    }

    public bool IsStepsView
    {
        get => View == FastfileView.Steps;
        set { if (value) View = FastfileView.Steps; }
    }

    public bool IsSourceView
    {
        get => View == FastfileView.Source;
        set { if (value) View = FastfileView.Source; }
    }

    // ---- run -----------------------------------------------------------------

    /// <summary>The inspected lane is the project's own, so it is always runnable.</summary>
    public bool CanRunSelected => SelectedLane is not null && _runLane is not null;

    [RelayCommand(CanExecute = nameof(CanRunSelected))]
    void RunSelectedLane()
    {
        if (SelectedLane is not { } l) return;
        _runLane?.Invoke(l.Platform, l.Name);
    }
}

/// <summary>One lane row in the rail: its name, description, platform, source and steps.</summary>
public sealed partial class LaneRowViewModel : ObservableObject
{
    public LaneRowViewModel(LaneDetail detail)
    {
        Name = detail.Lane.Name;
        Description = detail.Lane.Description;
        Platform = detail.Lane.Platform;
        Source = detail.Source;
        Steps = detail.Steps.Select((s, i) => new LaneStepViewModel(i + 1, s)).ToList();
    }

    public string Name { get; }
    public string Description { get; }
    public Platform Platform { get; }
    public string Source { get; }
    public IReadOnlyList<LaneStepViewModel> Steps { get; }

    public bool HasDescription => !string.IsNullOrWhiteSpace(Description);

    [ObservableProperty]
    private bool _isSelected;
}

/// <summary>One step row in the Steps view: an index, action, tool badge and params.</summary>
public sealed class LaneStepViewModel
{
    public LaneStepViewModel(int index, LaneStep step)
    {
        Index = index;
        Action = step.Action;
        Tool = step.Tool;
        Params = step.Params;
    }

    public int Index { get; }
    public string Action { get; }
    public string? Tool { get; }
    public string Params { get; }

    public bool HasTool => !string.IsNullOrEmpty(Tool);
    public bool HasParams => !string.IsNullOrWhiteSpace(Params);
}
