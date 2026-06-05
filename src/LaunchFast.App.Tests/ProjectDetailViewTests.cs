using Avalonia.Controls;
using Avalonia.Headless.NUnit;
using LaunchFast.App.ViewModels;
using LaunchFast.App.Views;

namespace LaunchFast.App.Tests;

public class ProjectDetailViewTests
{
    [AvaloniaTest]
    public void ProjectDetailView_shows_with_populated_vm_without_throwing()
    {
        var project = TestProjects.MakeFlutterProjectWithRealFastfiles();
        var vm = new ProjectDetailViewModel(project, new FakeSecretStore(), new RecordingPtyFactory());
        vm.Load();

        var window = new Window { Content = new ProjectDetailView { DataContext = vm } };
        window.Show();

        Assert.That(window.IsVisible, Is.True);
        Assert.That(vm.IosLanes, Is.Not.Empty);
        Assert.That(vm.AndroidLanes, Is.Not.Empty);

        window.Close();
    }

    [AvaloniaTest]
    public void Shell_navigates_launcher_to_detail_and_back()
    {
        var project = TestProjects.MakeFlutterProjectWithRealFastfiles();
        var storeFile = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".json");
        var store = new LaunchFast.Core.Scanning.ProjectStore(storeFile);
        store.AddRecent(project.Path);

        var launcher = new LauncherViewModel(store);
        launcher.Load();
        var shell = new ShellViewModel(launcher, new FakeSecretStore(), new RecordingPtyFactory());

        Assert.That(shell.CurrentView, Is.SameAs(launcher));

        shell.OpenDetail(project);
        Assert.That(shell.CurrentView, Is.InstanceOf<ProjectDetailViewModel>());

        ((ProjectDetailViewModel)shell.CurrentView).BackCommand.Execute(null);
        Assert.That(shell.CurrentView, Is.SameAs(launcher));
    }
}
