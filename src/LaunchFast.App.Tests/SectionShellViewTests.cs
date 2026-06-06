using Avalonia.Controls;
using Avalonia.Headless.NUnit;
using LaunchFast.App.ViewModels;
using LaunchFast.App.Views;

namespace LaunchFast.App.Tests;

public class SectionShellViewTests
{
    [AvaloniaTest]
    public void SigningSectionView_renders_with_placeholder_vm_without_throwing()
    {
        var project = TestProjects.MakeFlutterProjectWithRealFastfiles();
        var vm = new SigningSectionViewModel(project, hasSyncLane: () => true);

        var window = new Window { Content = new SigningSectionView { DataContext = vm } };
        window.Show();

        Assert.That(window.IsVisible, Is.True);
        Assert.That(vm.Certificates, Is.Not.Empty);

        window.Close();
    }

    [AvaloniaTest]
    public void TestFlightSectionView_renders_with_placeholder_vm_without_throwing()
    {
        var project = TestProjects.MakeFlutterProjectWithRealFastfiles();
        var vm = new TestFlightSectionViewModel(project, hasBetaLane: () => true);

        var window = new Window { Content = new TestFlightSectionView { DataContext = vm } };
        window.Show();

        Assert.That(window.IsVisible, Is.True);
        Assert.That(vm.Testers, Is.Not.Empty);

        window.Close();
    }

    [AvaloniaTest]
    public void ScreenshotsSectionView_renders_with_placeholder_vm_without_throwing()
    {
        var project = TestProjects.MakeFlutterProjectWithRealFastfiles();
        var vm = new ScreenshotsSectionViewModel(project, hasScreenshotsLane: () => true);

        var window = new Window { Content = new ScreenshotsSectionView { DataContext = vm } };
        window.Show();

        Assert.That(window.IsVisible, Is.True);
        Assert.That(vm.Devices, Is.Not.Empty);

        window.Close();
    }

    [AvaloniaTest]
    public void StoreListingSectionView_renders_with_real_metadata_without_throwing()
    {
        var project = TestProjects.MakeProjectWithStoreMetadata();
        var vm = new StoreListingSectionViewModel(project);

        var window = new Window { Content = new StoreListingSectionView { DataContext = vm } };
        window.Show();

        Assert.That(window.IsVisible, Is.True);
        Assert.That(vm.Fields, Is.Not.Empty);

        window.Close();
    }

    [AvaloniaTest]
    public void StoreListingSectionView_renders_empty_state_without_throwing()
    {
        var root = TestProjects.MakeFlutterProject();
        var project = LaunchFast.Core.Scanning.ProjectScanner.TryScanRoot(root)!;
        var vm = new StoreListingSectionViewModel(project);

        var window = new Window { Content = new StoreListingSectionView { DataContext = vm } };
        window.Show();

        Assert.That(window.IsVisible, Is.True);
        Assert.That(vm.IsEmpty, Is.True);

        window.Close();
    }

    [AvaloniaTest]
    public void WhatsNewSectionView_renders_with_real_release_notes_without_throwing()
    {
        var project = TestProjects.MakeProjectWithStoreMetadata();
        var vm = new WhatsNewSectionViewModel(project);

        var window = new Window { Content = new WhatsNewSectionView { DataContext = vm } };
        window.Show();

        Assert.That(window.IsVisible, Is.True);
        Assert.That(vm.Locales, Is.Not.Empty);
        Assert.That(vm.NoteText, Does.Contain("Faster sync"));

        window.Close();
    }

    [AvaloniaTest]
    public void WhatsNewSectionView_renders_empty_state_without_throwing()
    {
        var root = TestProjects.MakeFlutterProject();
        var project = LaunchFast.Core.Scanning.ProjectScanner.TryScanRoot(root)!;
        var vm = new WhatsNewSectionViewModel(project);

        var window = new Window { Content = new WhatsNewSectionView { DataContext = vm } };
        window.Show();

        Assert.That(window.IsVisible, Is.True);
        Assert.That(vm.IsEmpty, Is.True);

        window.Close();
    }

    [AvaloniaTest]
    public void ReleaseSectionView_renders_with_real_checks_without_throwing()
    {
        var project = TestProjects.MakeFlutterProjectWithRealFastfiles();
        var vm = new ReleaseSectionViewModel(project,
            runLane: (_, _) => { }, hasLane: (_, _) => true);

        var window = new Window { Content = new ReleaseSectionView { DataContext = vm } };
        window.Show();

        Assert.That(window.IsVisible, Is.True);
        Assert.That(vm.Checks, Is.Not.Empty);

        window.Close();
    }

    [AvaloniaTest]
    public void BuildTestSectionView_renders_with_placeholder_vm_without_throwing()
    {
        var project = TestProjects.MakeFlutterProjectWithRealFastfiles();
        var vm = new BuildTestSectionViewModel(project,
            hasTestLane: () => false, hasBuildLane: () => false);

        var window = new Window { Content = new BuildTestSectionView { DataContext = vm } };
        window.Show();

        Assert.That(window.IsVisible, Is.True);
        Assert.That(vm.Results, Is.Not.Empty);

        window.Close();
    }
}
