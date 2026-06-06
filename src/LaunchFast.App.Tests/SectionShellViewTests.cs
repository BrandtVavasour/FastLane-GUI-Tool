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
}
