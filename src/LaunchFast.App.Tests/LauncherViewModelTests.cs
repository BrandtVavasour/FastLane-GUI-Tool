using LaunchFast.App.ViewModels;
using LaunchFast.Core.Models;
using LaunchFast.Core.Scanning;

namespace LaunchFast.App.Tests;

public class LauncherViewModelTests
{
    [Test]
    public void Card_without_fastlane_is_a_setup_candidate()
    {
        var project = new Project("New App", "/p", "1.0.0+1", null, null, false, null);
        var vm = new ProjectCardViewModel(project);
        Assert.That(vm.NeedsSetup, Is.True);
        Assert.That(vm.HasIos, Is.False);
    }

    [Test]
    public void Load_populates_cards_from_recents()
    {
        var root = TestProjects.MakeFlutterProject();
        var storeFile = Path.GetTempFileName();
        var store = new ProjectStore(storeFile);
        store.AddRecent(root);

        var vm = new LauncherViewModel(store);
        vm.Load();

        Assert.That(vm.Cards, Has.Count.EqualTo(1));
        Assert.That(vm.Cards[0].Name, Is.EqualTo(new DirectoryInfo(root).Name));
    }

    [Test]
    public void Load_discovers_workspace_children_and_dedupes()
    {
        var workspace = Path.Combine(Path.GetTempPath(), "lf-app-ws-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workspace);

        // Two child projects inside the workspace.
        var childA = TestProjectInside(workspace, "alpha");
        var childB = TestProjectInside(workspace, "beta");

        var storeFile = Path.GetTempFileName();
        var store = new ProjectStore(storeFile);
        // childA is ALSO a recent => should appear only once (dedupe).
        store.AddRecent(childA);
        store.AddWorkspace(workspace);

        var vm = new LauncherViewModel(store);
        vm.Load();

        Assert.That(vm.Cards, Has.Count.EqualTo(2));
        Assert.That(
            vm.Cards.Select(c => c.Name),
            Is.EquivalentTo(new[] { "alpha", "beta" }));
        // childA appears exactly once despite being both a recent and a workspace child.
        Assert.That(vm.Cards.Count(c => c.Project.Path == childA), Is.EqualTo(1));
    }

    [Test]
    public void Opening_a_setup_candidate_card_routes_to_the_setup_request()
    {
        var candidate = new Project("New App", "/p", "1.0.0+1", null, null, false, null);
        var configured = new Project("Done", "/q", "1.0.0+1", "/q/ios/fastlane", null, false, null);

        var storeFile = Path.GetTempFileName();
        var store = new ProjectStore(storeFile);
        var vm = new LauncherViewModel(store);

        var setupRequests = new List<Project>();
        var detailRequests = new List<Project>();
        vm.OpenSetupRequested = setupRequests.Add;
        vm.OpenDetailRequested = detailRequests.Add;

        vm.OpenDetailCommand.Execute(new ProjectCardViewModel(candidate));
        vm.OpenDetailCommand.Execute(new ProjectCardViewModel(configured));

        // The fastlane-less candidate routes to the setup wizard; the configured
        // project opens the normal project shell.
        Assert.That(setupRequests, Is.EqualTo(new[] { candidate }));
        Assert.That(detailRequests, Is.EqualTo(new[] { configured }));
    }

    static string TestProjectInside(string workspace, string name)
    {
        var root = Path.Combine(workspace, name);
        Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root, "pubspec.yaml"), "name: demo\nversion: 1.0.0\n");
        Directory.CreateDirectory(Path.Combine(root, "ios", "fastlane"));
        Directory.CreateDirectory(Path.Combine(root, "android", "fastlane"));
        return root;
    }
}
