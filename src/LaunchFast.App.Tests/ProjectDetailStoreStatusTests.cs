using LaunchFast.App.ViewModels;
using LaunchFast.Core.Models;
using LaunchFast.Core.Stores;

namespace LaunchFast.App.Tests;

public class ProjectDetailStoreStatusTests
{
    private static StoreIdentifiers Ids() =>
        new("au.com.jabtech.vendingMachineTracker", "au.com.jabtech.vending_machine_tracker");

    [Test]
    public async Task Fetches_per_lane_store_version_and_skips_none_destinations()
    {
        var project = TestProjects.MakeFlutterProjectWithRealFastfiles();
        var canned = new StoreStatus(Destination.TestFlight, Available: true, Line: "1.4.2 (17)", Secondary: null);
        var provider = new StoreStatusProvider(new FakeAscClient(canned), new FakePlayClient(canned));

        var vm = new ProjectDetailViewModel(
            project, new FakeSecretStore(), new RecordingPtyFactory(), provider, Ids());
        vm.Load();
        await vm.RefreshStoreStatusAsync();

        var beta = vm.IosLanes.First(l => l.Name == "beta");
        Assert.Multiple(() =>
        {
            Assert.That(beta.Store, Is.Not.Null);
            Assert.That(beta.Store!.Line, Is.EqualTo("1.4.2 (17)"));
            Assert.That(beta.HasStore, Is.True);
        });

        // A lane mapping to Destination.None (e.g. screenshots) gets no store status.
        var none = vm.IosLanes.FirstOrDefault(l => LaneDestination.For(l.Lane) == Destination.None);
        Assert.That(none, Is.Not.Null, "expected at least one non-store iOS lane in fixtures");
        Assert.That(none!.HasStore, Is.False);
    }

    [Test]
    public async Task Null_clients_yield_graceful_unavailable()
    {
        var project = TestProjects.MakeFlutterProjectWithRealFastfiles();
        var provider = new StoreStatusProvider(null, null);

        var vm = new ProjectDetailViewModel(
            project, new FakeSecretStore(), new RecordingPtyFactory(), provider, Ids());
        vm.Load();
        await vm.RefreshStoreStatusAsync();

        var beta = vm.IosLanes.First(l => l.Name == "beta");
        Assert.That(beta.StoreUnavailable, Is.True);
    }

    [Test]
    public async Task ForTest_default_provider_leaves_lanes_without_identifiers()
    {
        var project = TestProjects.MakeFlutterProjectWithRealFastfiles();
        var vm = ProjectDetailViewModel.ForTest(project);
        vm.Load();
        await vm.RefreshStoreStatusAsync();

        // No identifiers => never resolved => no available store status anywhere.
        Assert.That(vm.IosLanes.All(l => !l.HasStore), Is.True);
    }
}
