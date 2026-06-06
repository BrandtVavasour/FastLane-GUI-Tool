using LaunchFast.App.ViewModels;
using LaunchFast.Core.Models;
using LaunchFast.Core.Stores;

namespace LaunchFast.App.Tests;

public class TestFlightSectionViewModelTests
{
    static TestFlightInfo SampleInfo() => new(
        new BuildInfo(
            Version: "1.4.2",
            BuildNumber: "18",
            ProcessingState: "VALID",
            ExpiredCompliance: false,
            ExpiresText: "expires in 90 days",
            WhatsToTest: "Focus on the new onboarding flow."),
        Groups:
        [
            new BetaGroup("App Store Connect Users", IsInternal: true, TesterCount: 2),
            new BetaGroup("Beta Crew", IsInternal: false, TesterCount: 3),
        ],
        Testers:
        [
            new BetaTester("Ada", "Lovelace", "ada@example.com", "Installed", GroupName: "App Store Connect Users"),
            new BetaTester("Grace", "Hopper", "grace@example.com", "Accepted", GroupName: "Beta Crew"),
            new BetaTester("Alan", "Turing", "alan@example.com", "Invited", GroupName: "Beta Crew"),
        ]);

    static FakeAscClient AvailableClient() =>
        new(new StoreStatus(Destination.TestFlight, true, null, null), SampleInfo());

    [Test]
    public async Task With_available_client_surfaces_real_build_groups_and_testers()
    {
        var (project, _) = TestProjects.MakeProjectWithIosSigning();
        var vm = new TestFlightSectionViewModel(project, AvailableClient(), hasBetaLane: () => true);
        await vm.LoadAsync();

        Assert.Multiple(() =>
        {
            Assert.That(vm.IsAvailable, Is.True);
            Assert.That(vm.IsUnavailable, Is.False);
            Assert.That(vm.BuildVersion, Is.EqualTo("1.4.2 (18)"));
            Assert.That(vm.Processing, Is.EqualTo("Done"));
            Assert.That(vm.ExportCompliance, Is.EqualTo("Provided"));
            Assert.That(vm.DistributedTo, Does.Contain("Internal + External"));
            Assert.That(vm.ReleaseNotes, Does.Contain("onboarding"));
            Assert.That(vm.Groups, Has.Count.EqualTo(2));
            Assert.That(vm.Testers, Has.Count.EqualTo(3));
            Assert.That(vm.Testers[0].Name, Is.EqualTo("Ada Lovelace"));
            Assert.That(vm.Testers[0].Email, Is.EqualTo("ada@example.com"));
        });
    }

    [Test]
    public async Task Filter_counts_derive_from_real_testers()
    {
        var (project, _) = TestProjects.MakeProjectWithIosSigning();
        var vm = new TestFlightSectionViewModel(project, AvailableClient());
        await vm.LoadAsync();

        Assert.Multiple(() =>
        {
            Assert.That(vm.CountAll, Is.EqualTo(3));
            // Ada is in an internal group.
            Assert.That(vm.CountInternal, Is.EqualTo(1));
            // Grace (external, accepted) counts as external; Alan is pending (invited).
            Assert.That(vm.CountExternal, Is.EqualTo(1));
            Assert.That(vm.CountPending, Is.EqualTo(1));
        });
    }

    [Test]
    public async Task Null_client_yields_honest_unavailable_state_with_no_testers()
    {
        var (project, _) = TestProjects.MakeProjectWithIosSigning();
        var vm = new TestFlightSectionViewModel(project, asc: null);
        await vm.LoadAsync();

        Assert.Multiple(() =>
        {
            Assert.That(vm.IsAvailable, Is.False);
            Assert.That(vm.IsUnavailable, Is.True);
            Assert.That(vm.Testers, Is.Empty);
            Assert.That(vm.Groups, Is.Empty);
            Assert.That(vm.UnavailableMessage, Does.Contain("APP_STORE_CONNECT_API_KEY_PATH"));
        });
    }

    [Test]
    public async Task Throwing_client_collapses_to_unavailable_state()
    {
        var (project, _) = TestProjects.MakeProjectWithIosSigning();
        var vm = new TestFlightSectionViewModel(project, new ThrowingAscClient());
        await vm.LoadAsync();

        Assert.Multiple(() =>
        {
            Assert.That(vm.IsAvailable, Is.False);
            Assert.That(vm.Testers, Is.Empty);
        });
    }

    [Test]
    public void CanDistribute_reflects_whether_the_beta_lane_exists()
    {
        var project = TestProjects.MakeFlutterProjectWithRealFastfiles();

        var present = new TestFlightSectionViewModel(project, hasBetaLane: () => true);
        Assert.That(present.CanDistribute, Is.True);

        var absent = new TestFlightSectionViewModel(project, hasBetaLane: () => false);
        Assert.That(absent.CanDistribute, Is.False);
    }

    [Test]
    public void Distribute_invokes_the_run_delegate_with_beta_and_does_not_throw()
    {
        var project = TestProjects.MakeFlutterProjectWithRealFastfiles();
        (Platform Platform, string Lane)? called = null;

        var vm = new TestFlightSectionViewModel(project,
            runLane: (p, l) => called = (p, l),
            hasBetaLane: () => true);

        Assert.DoesNotThrow(() => vm.DistributeCommand.Execute(null));

        Assert.That(called, Is.Not.Null);
        Assert.That(called!.Value.Platform, Is.EqualTo(Platform.Ios));
        Assert.That(called.Value.Lane, Is.EqualTo("beta"));
    }

    [Test]
    public void Distribute_is_a_no_op_when_the_lane_is_absent()
    {
        var project = TestProjects.MakeFlutterProjectWithRealFastfiles();
        var calls = 0;

        var vm = new TestFlightSectionViewModel(project,
            runLane: (_, _) => calls++,
            hasBetaLane: () => false);

        vm.DistributeCommand.Execute(null);
        Assert.That(calls, Is.EqualTo(0));
    }

    [Test]
    public async Task Filter_segmented_control_is_mutually_exclusive()
    {
        var (project, _) = TestProjects.MakeProjectWithIosSigning();
        var vm = new TestFlightSectionViewModel(project, AvailableClient());
        await vm.LoadAsync();

        Assert.That(vm.FilterAll, Is.True);

        vm.FilterInternal = true;
        Assert.That(vm.Filter, Is.EqualTo(TesterFilter.Internal));
        Assert.That(vm.FilterAll, Is.False);
        Assert.That(vm.FilterInternal, Is.True);
    }

    sealed class ThrowingAscClient : IAppStoreConnectClient
    {
        public Task<StoreStatus> GetStatusAsync(string bundleId, Destination destination, CancellationToken ct = default) =>
            throw new InvalidOperationException("boom");

        public Task<TestFlightInfo> GetTestFlightAsync(string bundleId, CancellationToken ct = default) =>
            throw new InvalidOperationException("boom");
    }
}
