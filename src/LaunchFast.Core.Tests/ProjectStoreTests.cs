using LaunchFast.Core.Scanning;

namespace LaunchFast.Core.Tests;

public class ProjectStoreTests
{
    [Test]
    public void Roundtrips_recent_paths_and_workspaces()
    {
        var file = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".json");
        var store = new ProjectStore(file);
        store.AddRecent("/a/proj1");
        store.AddWorkspace("/home/work");
        var reloaded = new ProjectStore(file);
        Assert.That(reloaded.RecentPaths, Does.Contain("/a/proj1"));
        Assert.That(reloaded.Workspaces, Does.Contain("/home/work"));
    }

    [Test]
    public void AddRecent_is_unique_and_most_recent_first()
    {
        var file = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".json");
        var store = new ProjectStore(file);
        store.AddRecent("/a"); store.AddRecent("/b"); store.AddRecent("/a");
        Assert.That(store.RecentPaths, Is.EqualTo(new[] { "/a", "/b" }));
    }
}
