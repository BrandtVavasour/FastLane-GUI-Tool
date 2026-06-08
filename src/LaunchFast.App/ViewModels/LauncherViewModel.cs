using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LaunchFast.Core.Icons;
using LaunchFast.Core.Models;
using LaunchFast.Core.Scanning;
using LaunchFast.Core.Updates;

namespace LaunchFast.App.ViewModels;

public partial class LauncherViewModel(ProjectStore store) : ObservableObject
{
    public ProjectStore Store => store;

    public ObservableCollection<ProjectCardViewModel> Cards { get; } = new();

    /// <summary>Set by the shell; invoked when a card asks to open its detail view.</summary>
    public Action<Project>? OpenDetailRequested { get; set; }

    /// <summary>Set by the shell; invoked when a fastlane-less card asks to run setup.</summary>
    public Action<Project>? OpenSetupRequested { get; set; }

    public void Load()
    {
        Cards.Clear();
        var seen = new HashSet<string>();
        foreach (var path in store.RecentPaths)
            AddIfProject(path, seen);
        foreach (var ws in store.Workspaces)
            foreach (var p in ProjectScanner.ScanWorkspace(ws))
                AddIfProject(p.Path, seen);
    }

    void AddIfProject(string path, HashSet<string> seen)
    {
        if (!seen.Add(path)) return;
        var project = ProjectScanner.TryScanRoot(path);
        if (project is null) return;
        var withIcon = project with { IconPath = IconExtractor.Resolve(path) };
        Cards.Add(new ProjectCardViewModel(withIcon));
    }

    [ObservableProperty]
    private ReleaseInfo? _availableUpdate;

    public bool HasUpdate => AvailableUpdate is not null;

    public string UpdateBannerText =>
        AvailableUpdate is { } r ? $"⬆ Update available: {r.TagName}" : string.Empty;

    public string UpdateUrl => AvailableUpdate?.HtmlUrl ?? string.Empty;

    partial void OnAvailableUpdateChanged(ReleaseInfo? value)
    {
        OnPropertyChanged(nameof(HasUpdate));
        OnPropertyChanged(nameof(UpdateBannerText));
        OnPropertyChanged(nameof(UpdateUrl));
    }

    /// <summary>Sets (or clears) the available-update banner. Called by the shell after
    /// the background update check completes.</summary>
    public void SetAvailableUpdate(ReleaseInfo? update) => AvailableUpdate = update;

    [RelayCommand]
    void OpenProject(string path)
    {
        store.AddRecent(path);
        Load();
    }

    [RelayCommand]
    void OpenDetail(ProjectCardViewModel? card)
    {
        if (card is null) return;
        store.AddRecent(card.Project.Path);

        // A fastlane-less card opens the setup wizard rather than a (non-existent)
        // project shell; this also routes the card's "Set up →" affordance, whose
        // click bubbles to the whole-card OpenDetail command.
        if (card.NeedsSetup) OpenSetupRequested?.Invoke(card.Project);
        else OpenDetailRequested?.Invoke(card.Project);
    }
}
