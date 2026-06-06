using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LaunchFast.Core.Env;
using LaunchFast.Core.Models;
using LaunchFast.Core.Running;
using LaunchFast.Core.Stores;

namespace LaunchFast.App.ViewModels;

/// <summary>
/// Per-project navigation host. Sits between "open a project" and the section
/// screens: owns the project header info, the sidebar section list, and maps the
/// selected <see cref="ProjectSection"/> to a content view-model. The view then
/// maps each content VM → a View via DataTemplates.
///
/// Content VMs are built lazily and cached, so switching back to a section keeps
/// its state (e.g. a run in progress on Lanes). Only Lanes has a real screen for
/// now; the other sections resolve to a <see cref="SectionPlaceholderViewModel"/>
/// that later tasks replace with real section view-models.
/// </summary>
public partial class ProjectShellViewModel : ObservableObject
{
    readonly Project _project;
    readonly ISecretStore _secrets;
    readonly IPtyFactory _ptyFactory;
    readonly StoreStatusProvider _storeStatus;
    readonly StoreIdentifiers _identifiers;

    readonly Dictionary<ProjectSection, object> _contentCache = new();

    public ProjectShellViewModel(
        Project project,
        ISecretStore secrets,
        IPtyFactory ptyFactory,
        StoreStatusProvider storeStatus,
        StoreIdentifiers identifiers)
    {
        _project = project;
        _secrets = secrets;
        _ptyFactory = ptyFactory;
        _storeStatus = storeStatus;
        _identifiers = identifiers;

        Sections = new ObservableCollection<ProjectSectionViewModel>
        {
            new(ProjectSection.Lanes, "Lanes", "🚀"),
            new(ProjectSection.Signing, "Signing", "🔐"),
            new(ProjectSection.Secrets, "Secrets", "🔑"),
            new(ProjectSection.TestFlight, "TestFlight", "✈"),
            new(ProjectSection.Screenshots, "Screenshots", "🖼"),
            new(ProjectSection.BuildTest, "Build & Test", "🧪"),
        };

        SelectSection(ProjectSection.Lanes);
    }

    /// <summary>Set by the shell; invoked by the Back command to return to the launcher.</summary>
    public Action? GoBack { get; set; }

    public Project Project => _project;
    public string Name => _project.Name;
    public string? Version => _project.Version;
    public string? IconPath => _project.IconPath;

    public ObservableCollection<ProjectSectionViewModel> Sections { get; }

    [ObservableProperty]
    private ProjectSection _selectedSection;

    /// <summary>The content view-model for the currently selected section.</summary>
    [ObservableProperty]
    private object? _currentContent;

    /// <summary>Selects a section, swapping (and caching) its content view-model.</summary>
    [RelayCommand]
    public void SelectSection(ProjectSection section)
    {
        SelectedSection = section;

        foreach (var item in Sections)
            item.IsSelected = item.Section == section;

        CurrentContent = ContentFor(section);
    }

    object ContentFor(ProjectSection section)
    {
        if (_contentCache.TryGetValue(section, out var cached)) return cached;

        var content = Build(section);
        _contentCache[section] = content;
        return content;
    }

    object Build(ProjectSection section) => section switch
    {
        ProjectSection.Lanes => BuildLanes(),
        ProjectSection.Signing => Placeholder("Signing"),
        ProjectSection.Secrets => Placeholder("Secrets"),
        ProjectSection.TestFlight => Placeholder("TestFlight"),
        ProjectSection.Screenshots => Placeholder("Screenshots"),
        ProjectSection.BuildTest => Placeholder("Build & Test"),
        _ => Placeholder(section.ToString()),
    };

    ProjectDetailViewModel BuildLanes()
    {
        var detail = new ProjectDetailViewModel(_project, _secrets, _ptyFactory, _storeStatus, _identifiers);
        detail.Load();
        return detail;
    }

    static SectionPlaceholderViewModel Placeholder(string title) =>
        new(title, "Coming up — not wired yet");

    [RelayCommand]
    void Back() => GoBack?.Invoke();
}
