using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LaunchFast.Core.Env;
using LaunchFast.Core.History;
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
    readonly RunHistoryStore _history;
    readonly IAppStoreConnectClient? _asc;

    readonly Dictionary<ProjectSection, object> _contentCache = new();

    public ProjectShellViewModel(
        Project project,
        ISecretStore secrets,
        IPtyFactory ptyFactory,
        StoreStatusProvider storeStatus,
        StoreIdentifiers identifiers,
        RunHistoryStore? history = null,
        IAppStoreConnectClient? asc = null)
    {
        _project = project;
        _secrets = secrets;
        _ptyFactory = ptyFactory;
        _storeStatus = storeStatus;
        _identifiers = identifiers;
        _history = history ?? new RunHistoryStore();
        _asc = asc;

        Sections = new ObservableCollection<ProjectSectionViewModel>
        {
            new(ProjectSection.Lanes, "Lanes", "🚀"),
            new(ProjectSection.Fastfile, "Fastfile", "🧩"),
            new(ProjectSection.Signing, "Signing", "🔐"),
            new(ProjectSection.Secrets, "Secrets", "🔑"),
            new(ProjectSection.TestFlight, "TestFlight", "✈"),
            new(ProjectSection.Screenshots, "Screenshots", "🖼"),
            new(ProjectSection.BuildTest, "Build & Test", "🧪"),
            new(ProjectSection.StoreListing, "Store Listing", "🏷"),
            new(ProjectSection.WhatsNew, "What's New", "📝"),
            new(ProjectSection.Release, "Release", "📦"),
            new(ProjectSection.History, "History", "🕘"),
            new(ProjectSection.AndroidSigning, "Android Signing", "🔏"),
        };

        SelectSection(ProjectSection.Lanes);
    }

    /// <summary>Set by the shell; invoked by the Back command to return to the launcher.</summary>
    public Action? GoBack { get; set; }

    /// <summary>
    /// Set by the shell; opens the setup wizard for this project. The bool is the
    /// install flag (false = add a lane/platform to the existing setup), so the
    /// Fastfile section's "Add lane / platform" reaches the root shell through here.
    /// </summary>
    public Action<bool>? OpenWizard { get; set; }

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
        ProjectSection.Lanes => Lanes,
        ProjectSection.Fastfile => BuildFastfile(),
        ProjectSection.Signing => BuildSigning(),
        ProjectSection.Secrets => BuildSecrets(),
        ProjectSection.TestFlight => BuildTestFlight(),
        ProjectSection.Screenshots => BuildScreenshots(),
        ProjectSection.BuildTest => BuildBuildTest(),
        ProjectSection.StoreListing => BuildStoreListing(),
        ProjectSection.WhatsNew => BuildWhatsNew(),
        ProjectSection.Release => BuildRelease(),
        ProjectSection.History => BuildHistory(),
        ProjectSection.AndroidSigning => BuildAndroidSigning(),
        _ => Placeholder(section.ToString()),
    };

    ProjectDetailViewModel? _lanes;

    /// <summary>
    /// The Lanes content VM, lazily built and shared. It owns the real lane runner;
    /// the Signing/TestFlight shells route their primary-action buttons through it
    /// via <see cref="RunLane"/>.
    /// </summary>
    ProjectDetailViewModel Lanes
    {
        get
        {
            if (_lanes is not null) return _lanes;
            _lanes = new ProjectDetailViewModel(
                _project, _secrets, _ptyFactory, _storeStatus, _identifiers, _history);
            _lanes.Load();
            return _lanes;
        }
    }

    FastfileSectionViewModel BuildFastfile() =>
        new(_project, RunLane, openWizard: install => OpenWizard?.Invoke(install));

    SigningSectionViewModel BuildSigning() =>
        new(_project, _secrets,
            runLane: RunLane,
            hasSyncLane: () => Lanes.HasLane(Platform.Ios, "sync_certificates"));

    TestFlightSectionViewModel BuildTestFlight() =>
        new(_project, _asc, RunLane, () => Lanes.HasLane(Platform.Ios, "beta"));

    SecretsSectionViewModel BuildSecrets() =>
        new(_project, _secrets);

    ScreenshotsSectionViewModel BuildScreenshots() =>
        new(_project, RunLane, () => Lanes.HasLane(Platform.Ios, "screenshots"));

    BuildTestSectionViewModel BuildBuildTest() =>
        new(_project, RunLane,
            hasTestLane: () => Lanes.HasLane(Platform.Ios, "test"),
            hasBuildLane: () => Lanes.HasLane(Platform.Ios, "build"));

    StoreListingSectionViewModel BuildStoreListing() =>
        new(_project);

    WhatsNewSectionViewModel BuildWhatsNew() =>
        new(_project);

    ReleaseSectionViewModel BuildRelease() =>
        new(_project, RunLane, (p, lane) => Lanes.HasLane(p, lane));

    RunHistorySectionViewModel BuildHistory() =>
        new(_history, _project.Path, RunLane, () => DateTime.UtcNow);

    AndroidSigningSectionViewModel BuildAndroidSigning() =>
        new(_project, _secrets, RunLane,
            hasBuildLane: () => Lanes.HasLane(Platform.Android, "build"));

    /// <summary>
    /// Runs a lane on behalf of a section screen: switches to the Lanes section so
    /// the user sees the live run panel, then delegates to the Lanes VM (which keeps
    /// its preflight / gating / one-run guards). No-op when the lane is absent.
    /// </summary>
    public void RunLane(Platform platform, string laneName)
    {
        SelectSection(ProjectSection.Lanes);
        Lanes.TryRunLane(platform, laneName);
    }

    static SectionPlaceholderViewModel Placeholder(string title) =>
        new(title, "Coming up — not wired yet");

    [RelayCommand]
    void Back() => GoBack?.Invoke();
}
