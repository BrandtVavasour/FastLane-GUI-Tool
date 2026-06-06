using LaunchFast.App.ViewModels;
using LaunchFast.App.ViewModels.Wizard;
using LaunchFast.Core.Models;
using LaunchFast.Core.Scanning;

namespace LaunchFast.App.Tests;

public class ShellViewModelTests
{
    static ShellViewModel MakeShell(out LauncherViewModel launcher)
    {
        var storeFile = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".json");
        var store = new ProjectStore(storeFile);
        launcher = new LauncherViewModel(store);
        return new ShellViewModel(launcher, new FakeSecretStore(), new RecordingPtyFactory());
    }

    /// <summary>A fastlane-less temp Flutter project (ios/ + android/ + pubspec).</summary>
    static Project SetupCandidate()
    {
        var root = Path.Combine(Path.GetTempPath(), "lf-shell-" + Guid.NewGuid().ToString("N"), "demo");
        Directory.CreateDirectory(Path.Combine(root, "ios"));
        Directory.CreateDirectory(Path.Combine(root, "android"));
        File.WriteAllText(Path.Combine(root, "pubspec.yaml"), "name: demo\nversion: 1.0.0+1\n");
        return ProjectScanner.TryScanRoot(root)!;
    }

    [Test]
    public void OpenSetupWizard_install_shows_an_install_wizard()
    {
        var shell = MakeShell(out _);

        shell.OpenSetupWizard(SetupCandidate(), install: true);

        Assert.That(shell.CurrentView, Is.InstanceOf<SetupWizardViewModel>());
        Assert.That(((SetupWizardViewModel)shell.CurrentView).Mode, Is.EqualTo(WizardMode.Install));
    }

    [Test]
    public void OpenSetupWizard_add_shows_an_add_to_existing_wizard()
    {
        var shell = MakeShell(out _);
        var project = TestProjects.MakeFlutterProjectWithRealFastfiles();

        shell.OpenSetupWizard(project, install: false);

        Assert.That(shell.CurrentView, Is.InstanceOf<SetupWizardViewModel>());
        Assert.That(((SetupWizardViewModel)shell.CurrentView).Mode, Is.EqualTo(WizardMode.AddToExisting));
    }

    [Test]
    public void Closing_the_wizard_returns_to_the_launcher_and_rescans()
    {
        var shell = MakeShell(out var launcher);

        // A fresh candidate that gains a real Fastfile while the wizard is open.
        var candidate = SetupCandidate();
        var store = new ProjectStore(
            Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".json"));
        store.AddRecent(candidate.Path);
        launcher = new LauncherViewModel(store);
        shell = new ShellViewModel(launcher, new FakeSecretStore(), new RecordingPtyFactory());
        launcher.Load();
        Assert.That(launcher.Cards.Single().NeedsSetup, Is.True);

        shell.OpenSetupWizard(candidate, install: true);
        var wizard = (SetupWizardViewModel)shell.CurrentView;

        // Simulate a successful setup: a Fastfile now exists on disk.
        var fastlaneDir = Path.Combine(candidate.Path, "ios", "fastlane");
        Directory.CreateDirectory(fastlaneDir);
        File.WriteAllText(Path.Combine(fastlaneDir, "Fastfile"),
            "platform :ios do\n  lane :beta do\n  end\nend\n");

        // Closing the wizard returns home AND re-scans the launcher.
        wizard.Closed!.Invoke();

        Assert.That(shell.CurrentView, Is.SameAs(launcher));
        Assert.That(launcher.Cards.Single().NeedsSetup, Is.False,
            "launcher should have re-scanned and seen the now-configured project");
    }

    [Test]
    public void Cancelling_the_wizard_returns_to_the_launcher()
    {
        var shell = MakeShell(out var launcher);

        shell.OpenSetupWizard(SetupCandidate(), install: true);
        var wizard = (SetupWizardViewModel)shell.CurrentView;

        wizard.CancelCommand.Execute(null);

        Assert.That(shell.CurrentView, Is.SameAs(launcher));
    }
}
