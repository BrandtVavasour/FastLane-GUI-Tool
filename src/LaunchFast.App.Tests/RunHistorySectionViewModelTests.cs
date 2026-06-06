using LaunchFast.App.ViewModels;
using LaunchFast.Core.History;
using LaunchFast.Core.Models;

namespace LaunchFast.App.Tests;

public class RunHistorySectionViewModelTests
{
    static string TempDir() =>
        Path.Combine(Path.GetTempPath(), "lf-historyvm-" + Guid.NewGuid().ToString("N"));

    static RunRecord Record(
        string lane,
        RunStatus status,
        DateTime startedUtc,
        TimeSpan duration,
        Platform platform = Platform.Ios,
        string trigger = "Local",
        string summary = "ok",
        string tail = "line one\nline two") =>
        new()
        {
            Platform = platform,
            LaneName = lane,
            Status = status,
            ExitCode = status == RunStatus.Succeeded ? 0 : 7,
            StartedUtc = startedUtc,
            Duration = duration,
            Trigger = trigger,
            ResultSummary = summary,
            OutputTail = tail,
        };

    [Test]
    public void Rows_and_stats_surface_from_a_seeded_store()
    {
        var store = new RunHistoryStore(TempDir());
        const string proj = "/projects/demo";
        var now = new DateTime(2026, 6, 1, 12, 0, 0, DateTimeKind.Utc);

        store.Append(proj, Record("beta", RunStatus.Succeeded, now.AddMinutes(-12), TimeSpan.FromSeconds(107)));
        store.Append(proj, Record("release", RunStatus.Failed, now.AddDays(-2), TimeSpan.FromSeconds(38),
            summary: "supply: 403"));

        var vm = new RunHistorySectionViewModel(store, proj, runLane: (_, _) => { }, nowUtc: () => now);

        Assert.That(vm.IsEmpty, Is.False);
        Assert.That(vm.Rows, Has.Count.EqualTo(2));
        // Newest first.
        Assert.That(vm.Rows[0].LaneName, Is.EqualTo("release"));
        Assert.That(vm.Rows[0].LaneLabel, Is.EqualTo("ios release"));
        Assert.That(vm.Rows[0].Failed, Is.True);
        Assert.That(vm.Rows[0].WhenText, Is.EqualTo("2d ago"));
        Assert.That(vm.Rows[1].LaneName, Is.EqualTo("beta"));
        Assert.That(vm.Rows[1].WhenText, Is.EqualTo("12m ago"));
        Assert.That(vm.Rows[1].DurationText, Is.EqualTo("1m 47s"));

        Assert.That(vm.RunsLast30Days, Is.EqualTo("2"));
        Assert.That(vm.SuccessRate, Is.EqualTo("50%"));
        Assert.That(vm.MedianDuration, Is.Not.EqualTo("—"));
        Assert.That(vm.LastFailure, Is.EqualTo("2d ago"));
    }

    [Test]
    public void Empty_state_when_no_history()
    {
        var store = new RunHistoryStore(TempDir());
        var vm = new RunHistorySectionViewModel(store, "/projects/empty", nowUtc: () => DateTime.UtcNow);

        Assert.That(vm.IsEmpty, Is.True);
        Assert.That(vm.Rows, Is.Empty);
        Assert.That(vm.EmptyStateText, Does.Contain("No runs recorded yet"));
    }

    [Test]
    public void Lane_filter_narrows_the_list()
    {
        var store = new RunHistoryStore(TempDir());
        const string proj = "/projects/filter";
        var now = new DateTime(2026, 6, 1, 12, 0, 0, DateTimeKind.Utc);

        store.Append(proj, Record("beta", RunStatus.Succeeded, now.AddMinutes(-1), TimeSpan.FromSeconds(10)));
        store.Append(proj, Record("release", RunStatus.Failed, now.AddMinutes(-2), TimeSpan.FromSeconds(20)));
        store.Append(proj, Record("beta", RunStatus.Succeeded, now.AddMinutes(-3), TimeSpan.FromSeconds(30)));

        var vm = new RunHistorySectionViewModel(store, proj, nowUtc: () => now);
        Assert.That(vm.Rows, Has.Count.EqualTo(3));

        vm.SelectedLaneFilter = "ios release";
        Assert.That(vm.Rows, Has.Count.EqualTo(1));
        Assert.That(vm.Rows[0].LaneName, Is.EqualTo("release"));

        vm.SelectedLaneFilter = "ios beta";
        Assert.That(vm.Rows, Has.Count.EqualTo(2));
    }

    [Test]
    public void Status_filter_narrows_the_list()
    {
        var store = new RunHistoryStore(TempDir());
        const string proj = "/projects/status";
        var now = new DateTime(2026, 6, 1, 12, 0, 0, DateTimeKind.Utc);

        store.Append(proj, Record("beta", RunStatus.Succeeded, now.AddMinutes(-1), TimeSpan.FromSeconds(10)));
        store.Append(proj, Record("release", RunStatus.Failed, now.AddMinutes(-2), TimeSpan.FromSeconds(20)));

        var vm = new RunHistorySectionViewModel(store, proj, nowUtc: () => now);

        vm.SelectedStatusFilter = "Failed";
        Assert.That(vm.Rows, Has.Count.EqualTo(1));
        Assert.That(vm.Rows[0].Failed, Is.True);

        vm.SelectedStatusFilter = "Succeeded";
        Assert.That(vm.Rows, Has.Count.EqualTo(1));
        Assert.That(vm.Rows[0].Succeeded, Is.True);
    }

    [Test]
    public void Rerun_routes_the_rows_lane_to_the_runLane_delegate()
    {
        var store = new RunHistoryStore(TempDir());
        const string proj = "/projects/rerun";
        var now = new DateTime(2026, 6, 1, 12, 0, 0, DateTimeKind.Utc);
        store.Append(proj, Record("beta", RunStatus.Succeeded, now, TimeSpan.FromSeconds(10)));

        (Platform, string)? ran = null;
        var vm = new RunHistorySectionViewModel(store, proj,
            runLane: (p, l) => ran = (p, l), nowUtc: () => now);

        Assert.That(vm.CanRerun, Is.True);
        vm.RerunCommand.Execute(vm.Rows[0]);

        Assert.That(ran, Is.EqualTo((Platform.Ios, "beta")));
    }

    [Test]
    public void Toggle_row_expands_and_collapses_detail()
    {
        var store = new RunHistoryStore(TempDir());
        const string proj = "/projects/toggle";
        var now = new DateTime(2026, 6, 1, 12, 0, 0, DateTimeKind.Utc);
        store.Append(proj, Record("beta", RunStatus.Succeeded, now, TimeSpan.FromSeconds(10)));

        var vm = new RunHistorySectionViewModel(store, proj, nowUtc: () => now);
        var row = vm.Rows[0];
        Assert.That(row.IsExpanded, Is.False);

        vm.ToggleRowCommand.Execute(row);
        Assert.That(row.IsExpanded, Is.True);
        vm.ToggleRowCommand.Execute(row);
        Assert.That(row.IsExpanded, Is.False);
    }

    [Test]
    public void Export_logs_writes_visible_rows_to_a_file()
    {
        var store = new RunHistoryStore(TempDir());
        const string proj = "/projects/export";
        var now = new DateTime(2026, 6, 1, 12, 0, 0, DateTimeKind.Utc);
        store.Append(proj, Record("beta", RunStatus.Succeeded, now, TimeSpan.FromSeconds(10),
            tail: "built ipa"));

        var vm = new RunHistorySectionViewModel(store, proj, nowUtc: () => now);
        // No RequestExport registered → falls back to temp-dir write.
        vm.ExportLogsCommand.Execute(null);

        Assert.That(vm.ExportStatus, Does.StartWith("Exported"));
    }

    // ---- BuildExportContent ---------------------------------------------------

    [Test]
    public void BuildExportContent_returns_header_only_when_no_rows()
    {
        var store = new RunHistoryStore(TempDir());
        var vm = new RunHistorySectionViewModel(store, "/projects/empty-export",
            nowUtc: () => DateTime.UtcNow);

        var content = vm.BuildExportContent();

        Assert.That(content, Does.Contain("LaunchFast Run History Export"));
        Assert.That(content, Does.Contain("(no runs match the current filter)"));
    }

    [Test]
    public void BuildExportContent_contains_lane_status_when_and_duration_for_each_row()
    {
        var store = new RunHistoryStore(TempDir());
        const string proj = "/projects/build-export";
        var now = new DateTime(2026, 6, 1, 12, 0, 0, DateTimeKind.Utc);

        store.Append(proj, Record("beta", RunStatus.Succeeded, now.AddMinutes(-5),
            TimeSpan.FromSeconds(107), tail: "built ipa"));
        store.Append(proj, Record("release", RunStatus.Failed, now.AddDays(-1),
            TimeSpan.FromSeconds(38), tail: "sign failed"));

        var vm = new RunHistorySectionViewModel(store, proj, nowUtc: () => now);

        var content = vm.BuildExportContent();

        // Header present
        Assert.That(content, Does.Contain("LaunchFast Run History Export"));

        // Row for "beta"
        Assert.That(content, Does.Contain("ios beta"));
        Assert.That(content, Does.Contain("Succeeded"));
        Assert.That(content, Does.Contain("built ipa"));
        Assert.That(content, Does.Contain("1m 47s"));

        // Row for "release"
        Assert.That(content, Does.Contain("ios release"));
        Assert.That(content, Does.Contain("Failed"));
        Assert.That(content, Does.Contain("sign failed"));
        Assert.That(content, Does.Contain("38s"));
    }

    [Test]
    public void BuildExportContent_respects_active_filter()
    {
        var store = new RunHistoryStore(TempDir());
        const string proj = "/projects/filter-export";
        var now = new DateTime(2026, 6, 1, 12, 0, 0, DateTimeKind.Utc);

        store.Append(proj, Record("beta", RunStatus.Succeeded, now.AddMinutes(-1),
            TimeSpan.FromSeconds(10), tail: "beta output"));
        store.Append(proj, Record("release", RunStatus.Failed, now.AddMinutes(-2),
            TimeSpan.FromSeconds(20), tail: "release output"));

        var vm = new RunHistorySectionViewModel(store, proj, nowUtc: () => now);
        vm.SelectedLaneFilter = "ios beta";

        var content = vm.BuildExportContent();

        Assert.That(content, Does.Contain("beta output"));
        Assert.That(content, Does.Not.Contain("release output"));
    }

    // ---- WriteExport ----------------------------------------------------------

    [Test]
    public void WriteExport_writes_content_to_the_given_path_and_sets_success_status()
    {
        var store = new RunHistoryStore(TempDir());
        const string proj = "/projects/write-export";
        var now = new DateTime(2026, 6, 1, 12, 0, 0, DateTimeKind.Utc);

        store.Append(proj, Record("beta", RunStatus.Succeeded, now, TimeSpan.FromSeconds(10),
            tail: "all good"));

        var vm = new RunHistorySectionViewModel(store, proj, nowUtc: () => now);

        var dest = Path.Combine(TempDir(), "out.txt");
        Directory.CreateDirectory(Path.GetDirectoryName(dest)!);

        vm.WriteExport(dest);

        Assert.That(File.Exists(dest), Is.True);
        var written = File.ReadAllText(dest);
        Assert.That(written, Does.Contain("all good"));
        Assert.That(vm.ExportStatus, Does.StartWith("Exported"));
        Assert.That(vm.ExportStatus, Does.Contain(dest));
    }

    [Test]
    public void WriteExport_sets_failure_status_when_path_is_invalid()
    {
        var store = new RunHistoryStore(TempDir());
        const string proj = "/projects/write-fail";
        var now = new DateTime(2026, 6, 1, 12, 0, 0, DateTimeKind.Utc);
        store.Append(proj, Record("beta", RunStatus.Succeeded, now, TimeSpan.FromSeconds(10)));

        var vm = new RunHistorySectionViewModel(store, proj, nowUtc: () => now);

        // A path to a non-existent nested directory should cause an IOException.
        var badPath = Path.Combine(TempDir(), "nonexistent", "subdir", "out.txt");
        vm.WriteExport(badPath);

        Assert.That(vm.ExportStatus, Does.StartWith("Export failed:"));
    }

    // NOTE: The save-as file picker (StorageProvider / TopLevel) is Avalonia view-layer
    // and requires a real window host, so it is not tested here.  The picker lives
    // in RunHistorySectionView.axaml.cs and drives WriteExport after the user picks a path.
}
