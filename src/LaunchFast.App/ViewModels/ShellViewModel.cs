using CommunityToolkit.Mvvm.ComponentModel;
using LaunchFast.App.Services;
using LaunchFast.App.ViewModels.Wizard;
using LaunchFast.Core.Env;
using LaunchFast.Core.Models;
using LaunchFast.Core.Running;
using LaunchFast.Core.Updates;

namespace LaunchFast.App.ViewModels;

/// <summary>
/// Root navigation host. Swaps <see cref="CurrentView"/> between the launcher
/// and a project shell view-model. Owns the production secret store + PTY
/// factory so the per-project shell (and its sections) are wired with the real
/// backends.
/// </summary>
public partial class ShellViewModel : ObservableObject
{
    readonly ISecretStore _secrets;
    readonly IPtyFactory _ptyFactory;
    readonly Func<CancellationToken, Task<ReleaseInfo?>>? _checkForUpdate;

    public LauncherViewModel Launcher { get; }

    [ObservableProperty]
    private object _currentView;

    public ShellViewModel(LauncherViewModel launcher, ISecretStore secrets, IPtyFactory ptyFactory,
        Func<CancellationToken, Task<ReleaseInfo?>>? checkForUpdate = null)
    {
        Launcher = launcher;
        _secrets = secrets;
        _ptyFactory = ptyFactory;
        _checkForUpdate = checkForUpdate;
        _currentView = launcher;
        launcher.OpenDetailRequested = OpenDetail;
        launcher.OpenSetupRequested = project => OpenSetupWizard(project, install: true);
        StartUpdateCheck();
    }

    void StartUpdateCheck()
    {
        if (_checkForUpdate is null) return;
        _ = Task.Run(async () =>
        {
            var rel = await _checkForUpdate(CancellationToken.None);
            if (rel is null) return;
            Avalonia.Threading.Dispatcher.UIThread.Post(() => Launcher.SetAvailableUpdate(rel));
        });
    }

    public void OpenDetail(Project project)
    {
        var env = ProjectDetailViewModel.ResolveProjectEnv(project.Path);
        var (provider, ids) = StoreStatusFactory.Create(project, env);
        var asc = StoreStatusFactory.CreateAscClient(env);

        var shell = new ProjectShellViewModel(project, _secrets, _ptyFactory, provider, ids, asc: asc)
        {
            GoBack = GoHome,
            OpenWizard = install => OpenSetupWizard(project, install),
        };
        CurrentView = shell;
    }

    /// <summary>
    /// Builds and shows the fastlane setup wizard with the REAL apply pipeline. The
    /// shell's own secret store + PTY factory (the production Keychain + process
    /// backends; fakes under test) back the scaffold service, so a successful apply
    /// writes the rendered files, stores the chosen secrets and streams
    /// <c>bundle install</c> into the wizard's apply log. On Cancel OR a successful
    /// apply the wizard returns to the launcher and re-scans it, so a now-configured
    /// project loses its "Set up" CTA and opens the normal project shell next time.
    /// </summary>
    public void OpenSetupWizard(Project project, bool install)
    {
        var svc = new ProjectScaffoldService(_secrets, _ptyFactory, project.Path);
        var wizard = install
            ? SetupWizardViewModel.ForInstall(project, apply: p => svc.ApplyAsync(p, project.Path))
            : SetupWizardViewModel.ForAddToExisting(project, apply: p => svc.ApplyAsync(p, project.Path));
        svc.Output += wizard.AppendApplyLog;
        wizard.Closed = () => { GoHome(); RefreshLauncher(); };
        CurrentView = wizard;
    }

    public void GoHome() => CurrentView = Launcher;

    /// <summary>Re-scans the launcher's projects (e.g. after a setup completes).</summary>
    public void RefreshLauncher() => Launcher.Load();
}
