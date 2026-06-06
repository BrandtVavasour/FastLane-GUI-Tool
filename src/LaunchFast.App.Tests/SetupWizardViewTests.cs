using Avalonia.Controls;
using Avalonia.Headless.NUnit;
using LaunchFast.App.ViewModels.Wizard;
using LaunchFast.Core.Models;
using LaunchFast.Core.Scanning;
using LaunchFast.App.Views;

namespace LaunchFast.App.Tests;

public class SetupWizardViewTests
{
    [AvaloniaTest]
    public void SetupWizardView_renders_across_steps_without_throwing()
    {
        var vm = SetupWizardViewModel.ForInstall(SetupCandidate());
        vm.Platforms.Ios = true;
        vm.Platforms.Android = false;

        var window = new Window { Content = new SetupWizardView { DataContext = vm } };
        window.Show();
        Assert.That(window.IsVisible, Is.True);

        // Platforms step shows first.
        Assert.That(vm.StepTitles[vm.StepIndex], Is.EqualTo("Platforms"));

        // Advance into the iOS step and render it.
        vm.Next();
        window.UpdateLayout();
        Assert.That(vm.CurrentStep, Is.InstanceOf<WizardIosStepViewModel>());

        // Fill iOS, advance to Lanes, then Review — each renders in the host.
        vm.Ios.BundleId = "com.acme.demo";
        vm.Ios.TeamId = "ABCDE12345";
        vm.Next();                              // Lanes
        window.UpdateLayout();
        Assert.That(vm.CurrentStep, Is.InstanceOf<WizardLanesStepViewModel>());

        vm.Next();                              // Review (builds the plan)
        window.UpdateLayout();
        Assert.That(vm.CurrentStep, Is.InstanceOf<WizardReviewStepViewModel>());
        Assert.That(vm.Review.Files, Is.Not.Empty);
        Assert.That(window.IsVisible, Is.True);

        window.Close();
    }

    /// <summary>A fastlane-less temp Flutter project (ios/ + android/ + pubspec).</summary>
    static Project SetupCandidate()
    {
        var root = Path.Combine(Path.GetTempPath(), "lf-wizview-" + Guid.NewGuid().ToString("N"), "demo");
        Directory.CreateDirectory(Path.Combine(root, "ios"));
        Directory.CreateDirectory(Path.Combine(root, "android"));
        File.WriteAllText(Path.Combine(root, "pubspec.yaml"), "name: demo\nversion: 1.0.0+1\n");
        return ProjectScanner.TryScanRoot(root)!;
    }
}
