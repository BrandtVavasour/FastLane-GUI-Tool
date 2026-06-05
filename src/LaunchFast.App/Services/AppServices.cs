using LaunchFast.App.ViewModels;
using LaunchFast.Core.Scanning;

namespace LaunchFast.App.Services;

/// <summary>
/// Minimal composition root: constructs the shared <see cref="ProjectStore"/>
/// and the root <see cref="LauncherViewModel"/>.
/// </summary>
public static class AppServices
{
    public static LauncherViewModel CreateLauncher()
    {
        var store = new ProjectStore(ProjectStore.DefaultPath);
        return new LauncherViewModel(store);
    }
}
