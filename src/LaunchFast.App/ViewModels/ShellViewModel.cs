using CommunityToolkit.Mvvm.ComponentModel;
using LaunchFast.Core.Env;
using LaunchFast.Core.Models;
using LaunchFast.Core.Running;

namespace LaunchFast.App.ViewModels;

/// <summary>
/// Root navigation host. Swaps <see cref="CurrentView"/> between the launcher
/// and a project-detail view-model. Owns the production secret store + PTY
/// factory so detail view-models are wired with the real backends.
/// </summary>
public partial class ShellViewModel : ObservableObject
{
    readonly ISecretStore _secrets;
    readonly IPtyFactory _ptyFactory;

    public LauncherViewModel Launcher { get; }

    [ObservableProperty]
    private object _currentView;

    public ShellViewModel(LauncherViewModel launcher, ISecretStore secrets, IPtyFactory ptyFactory)
    {
        Launcher = launcher;
        _secrets = secrets;
        _ptyFactory = ptyFactory;
        _currentView = launcher;
        launcher.OpenDetailRequested = OpenDetail;
    }

    public void OpenDetail(Project project)
    {
        var detail = new ProjectDetailViewModel(project, _secrets, _ptyFactory)
        {
            GoBack = GoHome,
        };
        detail.Load();
        CurrentView = detail;
    }

    public void GoHome() => CurrentView = Launcher;
}
