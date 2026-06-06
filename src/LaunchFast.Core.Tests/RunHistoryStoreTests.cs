using LaunchFast.Core.History;
using LaunchFast.Core.Models;

namespace LaunchFast.Core.Tests;

public class RunHistoryStoreTests
{
    static string TempDir() =>
        Path.Combine(Path.GetTempPath(), "lf-history-" + Guid.NewGuid().ToString("N"));

    static RunRecord Record(
        string lane,
        RunStatus status,
        DateTime startedUtc,
        TimeSpan duration,
        Platform platform = Platform.Ios) =>
        new()
        {
            Platform = platform,
            LaneName = lane,
            Status = status,
            ExitCode = status == RunStatus.Succeeded ? 0 : 1,
            StartedUtc = startedUtc,
            Duration = duration,
            ResultSummary = status == RunStatus.Succeeded ? "done" : "failed",
            OutputTail = $"line for {lane}",
        };

    [Test]
    public void Append_and_list_roundtrip_newest_first()
    {
        var store = new RunHistoryStore(TempDir());
        const string proj = "/projects/demo";

        var t0 = new DateTime(2026, 1, 1, 10, 0, 0, DateTimeKind.Utc);
        store.Append(proj, Record("beta", RunStatus.Succeeded, t0, TimeSpan.FromSeconds(30)));
        store.Append(proj, Record("release", RunStatus.Failed, t0.AddMinutes(5), TimeSpan.FromSeconds(60)));

        var list = store.List(proj);
        Assert.That(list, Has.Count.EqualTo(2));
        // Newest-first: the second appended record comes first.
        Assert.That(list[0].LaneName, Is.EqualTo("release"));
        Assert.That(list[1].LaneName, Is.EqualTo("beta"));
        Assert.That(list[0].OutputTail, Is.EqualTo("line for release"));
        Assert.That(list[0].ExitCode, Is.EqualTo(1));
        Assert.That(list[1].Platform, Is.EqualTo(Platform.Ios));
    }

    [Test]
    public void List_reads_back_after_reconstructing_the_store()
    {
        var dir = TempDir();
        const string proj = "/projects/persist";
        var first = new RunHistoryStore(dir);
        first.Append(proj, Record("beta", RunStatus.Succeeded,
            new DateTime(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc), TimeSpan.FromSeconds(10)));

        var reloaded = new RunHistoryStore(dir);
        Assert.That(reloaded.List(proj), Has.Count.EqualTo(1));
        Assert.That(reloaded.List(proj)[0].LaneName, Is.EqualTo("beta"));
    }

    [Test]
    public void Distinct_projects_do_not_share_history()
    {
        var store = new RunHistoryStore(TempDir());
        var t = new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc);
        store.Append("/a/one", Record("beta", RunStatus.Succeeded, t, TimeSpan.FromSeconds(5)));
        store.Append("/b/two", Record("release", RunStatus.Failed, t, TimeSpan.FromSeconds(5)));

        Assert.That(store.List("/a/one"), Has.Count.EqualTo(1));
        Assert.That(store.List("/a/one")[0].LaneName, Is.EqualTo("beta"));
        Assert.That(store.List("/b/two"), Has.Count.EqualTo(1));
        Assert.That(store.List("/b/two")[0].LaneName, Is.EqualTo("release"));
    }

    [Test]
    public void Stats_computes_success_rate_median_window_and_last_failure()
    {
        var store = new RunHistoryStore(TempDir());
        const string proj = "/projects/stats";
        var now = new DateTime(2026, 6, 1, 12, 0, 0, DateTimeKind.Utc);

        // 5 records: 3 succeeded, 2 failed. Durations 10,20,30,40,50s.
        // Two records fall OUTSIDE the trailing 30-day window (40 and 45 days ago).
        store.Append(proj, Record("a", RunStatus.Succeeded, now.AddDays(-1), TimeSpan.FromSeconds(10)));
        store.Append(proj, Record("b", RunStatus.Failed, now.AddDays(-2), TimeSpan.FromSeconds(20)));
        store.Append(proj, Record("c", RunStatus.Succeeded, now.AddDays(-5), TimeSpan.FromSeconds(30)));
        store.Append(proj, Record("d", RunStatus.Failed, now.AddDays(-40), TimeSpan.FromSeconds(40)));
        store.Append(proj, Record("e", RunStatus.Succeeded, now.AddDays(-45), TimeSpan.FromSeconds(50)));

        var stats = store.Stats(proj, now);

        // 3 of 5 are within the last 30 days.
        Assert.That(stats.RunsLast30Days, Is.EqualTo(3));
        // 3 of 5 succeeded = 60%.
        Assert.That(stats.SuccessRatePercent, Is.EqualTo(60).Within(0.001));
        // Median of {10,20,30,40,50} = 30s.
        Assert.That(stats.MedianDuration, Is.EqualTo(TimeSpan.FromSeconds(30)));
        // Most recent failure is record "b" at now-2d.
        Assert.That(stats.LastFailureUtc, Is.EqualTo(now.AddDays(-2)));
    }

    [Test]
    public void Stats_median_with_even_count_averages_the_two_middle_values()
    {
        var store = new RunHistoryStore(TempDir());
        const string proj = "/projects/median";
        var now = new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc);

        store.Append(proj, Record("a", RunStatus.Succeeded, now, TimeSpan.FromSeconds(10)));
        store.Append(proj, Record("b", RunStatus.Succeeded, now, TimeSpan.FromSeconds(20)));
        store.Append(proj, Record("c", RunStatus.Succeeded, now, TimeSpan.FromSeconds(30)));
        store.Append(proj, Record("d", RunStatus.Succeeded, now, TimeSpan.FromSeconds(40)));

        var stats = store.Stats(proj, now);
        // Median of {10,20,30,40} = (20+30)/2 = 25s.
        Assert.That(stats.MedianDuration, Is.EqualTo(TimeSpan.FromSeconds(25)));
        Assert.That(stats.LastFailureUtc, Is.Null);
        Assert.That(stats.SuccessRatePercent, Is.EqualTo(100).Within(0.001));
    }

    [Test]
    public void Missing_file_yields_empty_list_and_zero_stats()
    {
        var store = new RunHistoryStore(TempDir());
        Assert.That(store.List("/never/written"), Is.Empty);

        var stats = store.Stats("/never/written", DateTime.UtcNow);
        Assert.That(stats.RunsLast30Days, Is.EqualTo(0));
        Assert.That(stats.SuccessRatePercent, Is.EqualTo(0));
        Assert.That(stats.MedianDuration, Is.Null);
        Assert.That(stats.LastFailureUtc, Is.Null);
    }

    [Test]
    public void Corrupt_file_reads_as_empty_and_never_throws()
    {
        var dir = TempDir();
        Directory.CreateDirectory(dir);
        var store = new RunHistoryStore(dir);

        // Write garbage to the exact file the store would use, by appending then
        // clobbering. First append establishes the file path; then corrupt it.
        const string proj = "/projects/corrupt";
        store.Append(proj, Record("beta", RunStatus.Succeeded,
            new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc), TimeSpan.FromSeconds(1)));

        var file = Directory.GetFiles(dir, "*.json").Single();
        File.WriteAllText(file, "{ not valid json ][");

        Assert.That(store.List(proj), Is.Empty);
        Assert.That(store.Stats(proj, DateTime.UtcNow).RunsLast30Days, Is.EqualTo(0));
    }

    [Test]
    public void Append_caps_stored_records_at_the_maximum()
    {
        var store = new RunHistoryStore(TempDir());
        const string proj = "/projects/cap";
        var t = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        for (var i = 0; i < 520; i++)
            store.Append(proj, Record($"lane{i}", RunStatus.Succeeded, t.AddMinutes(i), TimeSpan.FromSeconds(1)));

        var list = store.List(proj);
        Assert.That(list, Has.Count.EqualTo(500));
        // Newest (last appended) is retained at the head.
        Assert.That(list[0].LaneName, Is.EqualTo("lane519"));
    }
}
