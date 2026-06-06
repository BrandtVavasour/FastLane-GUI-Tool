using LaunchFast.App.ViewModels;
using LaunchFast.Core.History;
using LaunchFast.Core.Models;
using LaunchFast.Core.Stores;

namespace LaunchFast.App.Tests;

/// <summary>
/// End-to-end recording: a lane run driven through the shell's Lanes VM with the
/// RecordingPtyFactory must append a terminal <see cref="RunRecord"/> to the SAME
/// shared <see cref="RunHistoryStore"/> the History section reads from.
/// </summary>
public class RunHistoryRecordingTests
{
    static string TempDir() =>
        Path.Combine(Path.GetTempPath(), "lf-recording-" + Guid.NewGuid().ToString("N"));

    static (ProjectDetailViewModel lanes, RecordingPtyFactory factory, RunHistoryStore store, string projectId)
        MakeRunnableLanes()
    {
        var project = TestProjects.MakeFlutterProjectWithRealFastfiles();
        var factory = new RecordingPtyFactory();

        // Discover + satisfy the required secrets so a run is not gated.
        var probe = ProjectDetailViewModel.ForTest(project);
        probe.Load();
        var secrets = new FakeSecretStore().Satisfy(project.Path, probe.MissingSecrets);

        var store = new RunHistoryStore(TempDir());
        var lanes = new ProjectDetailViewModel(
            project, secrets, factory,
            storeStatus: new StoreStatusProvider(null, null),
            identifiers: new StoreIdentifiers(null, null),
            history: store);
        lanes.Load();

        return (lanes, factory, store, project.Path);
    }

    [Test]
    public void Successful_run_appends_a_succeeded_record_for_the_right_lane()
    {
        var (lanes, factory, store, projectId) = MakeRunnableLanes();

        var beta = lanes.IosLanes.First(l => l.Name == "beta");
        lanes.RunLaneCommand.Execute(beta);
        factory.Emit("Running lane ios beta");
        factory.Emit("Built ipa");
        factory.Finish(0);

        var records = store.List(projectId);
        Assert.That(records, Has.Count.EqualTo(1));
        Assert.That(records[0].Status, Is.EqualTo(RunStatus.Succeeded));
        Assert.That(records[0].LaneName, Is.EqualTo("beta"));
        Assert.That(records[0].Platform, Is.EqualTo(Platform.Ios));
        Assert.That(records[0].ExitCode, Is.EqualTo(0));
        Assert.That(records[0].Trigger, Is.EqualTo("Local"));
        Assert.That(records[0].ResultSummary, Is.EqualTo("Built ipa"));
        Assert.That(records[0].OutputTail, Does.Contain("Built ipa"));
    }

    [Test]
    public void Failed_run_appends_a_failed_record_with_the_exit_code()
    {
        var (lanes, factory, store, projectId) = MakeRunnableLanes();

        var beta = lanes.IosLanes.First(l => l.Name == "beta");
        lanes.RunLaneCommand.Execute(beta);
        factory.Emit("boom");
        factory.Finish(7);

        var records = store.List(projectId);
        Assert.That(records, Has.Count.EqualTo(1));
        Assert.That(records[0].Status, Is.EqualTo(RunStatus.Failed));
        Assert.That(records[0].ExitCode, Is.EqualTo(7));
        Assert.That(records[0].LaneName, Is.EqualTo("beta"));
    }

    [Test]
    public void Recorded_run_surfaces_in_the_history_section_sharing_the_store()
    {
        var (lanes, factory, store, projectId) = MakeRunnableLanes();

        var beta = lanes.IosLanes.First(l => l.Name == "beta");
        lanes.RunLaneCommand.Execute(beta);
        factory.Emit("Built ipa");
        factory.Finish(0);

        // A history section reading the SAME store sees the run.
        var history = new RunHistorySectionViewModel(store, projectId, nowUtc: () => DateTime.UtcNow);
        Assert.That(history.IsEmpty, Is.False);
        Assert.That(history.Rows, Has.Count.EqualTo(1));
        Assert.That(history.Rows[0].LaneName, Is.EqualTo("beta"));
        Assert.That(history.Rows[0].Succeeded, Is.True);
    }

    [Test]
    public void Shell_records_runs_into_the_history_section_store()
    {
        // Drive the real shell: satisfy secrets so the lane is runnable, then run via
        // the Lanes VM and confirm the History section (built from the SAME store the
        // shell injects) shows the run.
        var project = TestProjects.MakeFlutterProjectWithRealFastfiles();
        var factory = new RecordingPtyFactory();

        var probe = ProjectDetailViewModel.ForTest(project);
        probe.Load();
        var secrets = new FakeSecretStore().Satisfy(project.Path, probe.MissingSecrets);

        var store = new RunHistoryStore(TempDir());
        var shell = new ProjectShellViewModel(
            project, secrets, factory,
            new StoreStatusProvider(null, null),
            new StoreIdentifiers(null, null),
            store);

        var lanes = (ProjectDetailViewModel)shell.CurrentContent!;
        var beta = lanes.IosLanes.First(l => l.Name == "beta");
        lanes.RunLaneCommand.Execute(beta);
        factory.Emit("Built ipa");
        factory.Finish(0);

        shell.SelectSectionCommand.Execute(ProjectSection.History);
        var history = (RunHistorySectionViewModel)shell.CurrentContent!;
        Assert.That(history.Rows, Has.Count.EqualTo(1));
        Assert.That(history.Rows[0].LaneName, Is.EqualTo("beta"));
    }
}
