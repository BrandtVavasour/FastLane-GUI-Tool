using LaunchFast.App.ViewModels;
using LaunchFast.Core.Env;
using LaunchFast.Core.Running;
using LaunchFast.Core.Scanning;

namespace LaunchFast.App.Services;

/// <summary>
/// Minimal composition root: constructs the shared <see cref="ProjectStore"/>,
/// the root <see cref="LauncherViewModel"/>, and the navigation
/// <see cref="ShellViewModel"/> wired with the real Keychain + process backends.
/// </summary>
public static class AppServices
{
    public static LauncherViewModel CreateLauncher()
    {
        var store = new ProjectStore(ProjectStore.DefaultPath);
        return new LauncherViewModel(store);
    }

    public static ShellViewModel CreateShell()
    {
        var launcher = CreateLauncher();
        var secrets = new KeychainSecretStore();
        var ptyFactory = new DefaultPtyFactory();
        return new ShellViewModel(launcher, secrets, ptyFactory);
    }
}
