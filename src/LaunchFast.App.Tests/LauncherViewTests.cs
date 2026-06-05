using Avalonia.Controls;
using Avalonia.Headless.NUnit;
using LaunchFast.App.ViewModels;
using LaunchFast.App.Views;
using LaunchFast.Core.Scanning;

namespace LaunchFast.App.Tests;

public class LauncherViewTests
{
    [AvaloniaTest]
    public void MainWindow_with_launcher_constructs_and_shows_without_throwing()
    {
        var root = TestProjects.MakeFlutterProject();
        var storeFile = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".json");
        var store = new ProjectStore(storeFile);
        store.AddRecent(root);

        var vm = new LauncherViewModel(store);
        vm.Load();

        var window = new MainWindow { DataContext = vm };
        window.Show();

        Assert.That(window.IsVisible, Is.True);
        Assert.That(vm.Cards, Has.Count.EqualTo(1));

        window.Close();
    }

    [AvaloniaTest]
    public void LauncherView_constructs_standalone()
    {
        var view = new LauncherView();
        Assert.That(view, Is.Not.Null);
    }
}
