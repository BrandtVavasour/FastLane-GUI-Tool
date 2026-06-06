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
    public void Shell_navigates_launcher_to_project_shell_then_sections_then_back()
    {
        var project = TestProjects.MakeFlutterProjectWithRealFastfiles();
        var storeFile = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".json");
        var store = new LaunchFast.Core.Scanning.ProjectStore(storeFile);
        store.AddRecent(project.Path);

        var launcher = new LauncherViewModel(store);
        launcher.Load();
        var shell = new ShellViewModel(launcher, new FakeSecretStore(), new RecordingPtyFactory());

        Assert.That(shell.CurrentView, Is.SameAs(launcher));

        // Opening a project shows the per-project shell with the Lanes section
        // (ProjectDetailViewModel) selected by default.
        shell.OpenDetail(project);
        Assert.That(shell.CurrentView, Is.InstanceOf<ProjectShellViewModel>());
        var projectShell = (ProjectShellViewModel)shell.CurrentView;
        Assert.That(projectShell.SelectedSection, Is.EqualTo(ProjectSection.Lanes));
        Assert.That(projectShell.CurrentContent, Is.InstanceOf<ProjectDetailViewModel>());

        // The shell view hosts the Lanes content (ProjectDetailView) without throwing.
        var window = new Window { Content = new ProjectShellView { DataContext = projectShell } };
        window.Show();
        Assert.That(window.IsVisible, Is.True);

        // Selecting the Signing section swaps to its real section view-model.
        projectShell.SelectSectionCommand.Execute(ProjectSection.Signing);
        Assert.That(projectShell.SelectedSection, Is.EqualTo(ProjectSection.Signing));
        Assert.That(projectShell.CurrentContent, Is.InstanceOf<SigningSectionViewModel>());

        // The Screenshots section swaps to its real section view-model and renders
        // in the shell host without throwing.
        projectShell.SelectSectionCommand.Execute(ProjectSection.Screenshots);
        Assert.That(projectShell.CurrentContent, Is.InstanceOf<ScreenshotsSectionViewModel>());
        Assert.That(window.IsVisible, Is.True);

        // The Build & Test section likewise resolves to its real section view-model.
        projectShell.SelectSectionCommand.Execute(ProjectSection.BuildTest);
        Assert.That(projectShell.CurrentContent, Is.InstanceOf<BuildTestSectionViewModel>());
        Assert.That(window.IsVisible, Is.True);

        // The Secrets section has a real screen (SecretsSectionView) that renders
        // in the shell host without throwing.
        projectShell.SelectSectionCommand.Execute(ProjectSection.Secrets);
        Assert.That(projectShell.CurrentContent, Is.InstanceOf<SecretsSectionViewModel>());
        Assert.That(window.IsVisible, Is.True);

        // Back returns to the launcher.
        projectShell.BackCommand.Execute(null);
        Assert.That(shell.CurrentView, Is.SameAs(launcher));

        window.Close();
    }
}
