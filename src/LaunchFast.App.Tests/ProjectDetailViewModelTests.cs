using LaunchFast.App.ViewModels;

namespace LaunchFast.App.Tests;

public class ProjectDetailViewModelTests
{
    [Test]
    public void Lists_lanes_and_marks_missing_secrets()
    {
        var project = TestProjects.MakeFlutterProjectWithRealFastfiles();
        var vm = ProjectDetailViewModel.ForTest(project); // empty secret store + null pty factory
        vm.Load();

        Assert.That(vm.IosLanes.Select(l => l.Name), Does.Contain("beta"));
        Assert.That(vm.AndroidLanes.Select(l => l.Name), Does.Contain("internal"));
        Assert.That(vm.MissingSecrets, Is.Not.Empty);
        Assert.That(vm.MissingSecrets, Does.Contain("MATCH_GIT_URL"));
        Assert.That(vm.MissingSecrets, Does.Contain("APPLE_ID"));
        Assert.That(vm.CanRunIos, Is.False);
    }

    [Test]
    public void Control_env_vars_do_not_gate_runs_only_genuine_secrets_do()
    {
        var project = TestProjects.MakeFlutterProjectWithRealFastfiles();

        // Empty store: every genuine secret is missing, but no control var is.
        var probe = ProjectDetailViewModel.ForTest(project);
        probe.Load();

        Assert.That(probe.MissingSecrets, Does.Not.Contain("CI"));
        Assert.That(probe.MissingSecrets, Does.Not.Contain("FASTLANE_ENV"));
        Assert.That(probe.MissingSecrets, Does.Not.Contain("FLUTTER_LOCALE"));
        Assert.That(probe.MissingSecrets, Does.Not.Contain("MATCH_KEYCHAIN_NAME"));

        // Sanity: genuine secrets are still required and still missing.
        Assert.That(probe.MissingSecrets, Does.Contain("MATCH_KEYCHAIN_PASSWORD"));
        Assert.That(probe.CanRunIos, Is.False);

        // Satisfy exactly the (now narrowed) missing secrets and reload.
        var secrets = new FakeSecretStore().Satisfy(project.Path, probe.MissingSecrets);
        var vm = new ProjectDetailViewModel(project, secrets, new RecordingPtyFactory());
        vm.Load();

        Assert.That(vm.MissingSecrets, Is.Empty);
        Assert.That(vm.CanRunIos, Is.True);
    }

    [Test]
    public void Running_a_lane_streams_output_via_factory()
    {
        var project = TestProjects.MakeFlutterProjectWithRealFastfiles();
        var factory = new RecordingPtyFactory();

        // Discover the required set via an empty-store load, then satisfy it all.
        var probe = ProjectDetailViewModel.ForTest(project);
        probe.Load();
        var secrets = new FakeSecretStore().Satisfy(project.Path, probe.MissingSecrets);

        var vm = new ProjectDetailViewModel(project, secrets, factory);
        vm.Load();

        // All required secrets satisfied => runnable.
        Assert.That(vm.MissingSecrets, Is.Empty);
        Assert.That(vm.CanRunIos, Is.True);

        var betaLane = vm.IosLanes.First(l => l.Name == "beta");
        vm.RunLaneCommand.Execute(betaLane);

        Assert.That(vm.IsRunning, Is.True);
        Assert.That(factory.Command, Is.EqualTo("bundle"));
        Assert.That(factory.Args, Is.EqualTo(new[] { "exec", "fastlane", "ios", "beta" }));
        Assert.That(factory.Cwd, Is.EqualTo(Path.Combine(project.Path, "ios")));

        factory.Emit("Running lane ios beta");
        factory.Emit("");
        factory.Emit("Built ipa");
        Assert.That(vm.Run.Lines, Does.Contain("Running lane ios beta"));
        Assert.That(vm.Run.Lines, Does.Contain("Built ipa"));
        Assert.That(vm.Run.CurrentAction, Is.EqualTo("Built ipa"));

        factory.Finish(0);
        Assert.That(vm.IsRunning, Is.False);
        Assert.That(vm.Run.Handle, Is.Null);
    }

    [Test]
    public void Only_one_run_at_a_time()
    {
        var project = TestProjects.MakeFlutterProjectWithRealFastfiles();
        var factory = new RecordingPtyFactory();

        var probe = ProjectDetailViewModel.ForTest(project);
        probe.Load();
        var secrets = new FakeSecretStore().Satisfy(project.Path, probe.MissingSecrets);

        var vm = new ProjectDetailViewModel(project, secrets, factory);
        vm.Load();

        var first = vm.IosLanes.First(l => l.Name == "beta");
        var second = vm.IosLanes.First(l => l.Name == "release");

        vm.RunLaneCommand.Execute(first);
        var firstProcess = factory.Last;
        Assert.That(vm.IsRunning, Is.True);

        // Second run is ignored while the first is in flight.
        vm.RunLaneCommand.Execute(second);
        Assert.That(factory.Last, Is.SameAs(firstProcess));
    }
}
