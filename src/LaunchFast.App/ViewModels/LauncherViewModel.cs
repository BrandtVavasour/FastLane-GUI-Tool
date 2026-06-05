using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LaunchFast.Core.Icons;
using LaunchFast.Core.Models;
using LaunchFast.Core.Scanning;

namespace LaunchFast.App.ViewModels;

public partial class LauncherViewModel(ProjectStore store) : ObservableObject
{
    public ProjectStore Store => store;

    public ObservableCollection<ProjectCardViewModel> Cards { get; } = new();

    /// <summary>Set by the shell; invoked when a card asks to open its detail view.</summary>
    public Action<Project>? OpenDetailRequested { get; set; }

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
        OpenDetailRequested?.Invoke(card.Project);
    }
}
