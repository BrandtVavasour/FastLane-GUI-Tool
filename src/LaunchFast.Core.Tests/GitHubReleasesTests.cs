using LaunchFast.Core.Updates;

namespace LaunchFast.Core.Tests;

public class GitHubReleasesTests
{
    [TestCase("0.1.0", "0.2.0", true)]
    [TestCase("0.1.0", "v0.2.0", true)]      // 'v' prefix tolerated
    [TestCase("0.1.0", "0.1.1", true)]
    [TestCase("1.0.0", "1.0.0", false)]      // equal is not newer
    [TestCase("0.2.0", "0.1.9", false)]      // older
    [TestCase("0.1.0", "0.1.0.0", false)]    // 4th component ignored, equal
    [TestCase("0.1.0", "v1", true)]          // missing components treated as 0
    [TestCase("0.1.0", "garbage", false)]    // non-numeric -> not newer
    [TestCase("0.1.0", "0.2.0-beta", true)]  // prerelease suffix stripped
    public void IsNewer_compares_semver(string current, string latest, bool expected)
    {
        Assert.That(GitHubReleases.IsNewer(current, latest), Is.EqualTo(expected));
    }

    [Test]
    public void ParseLatest_reads_tag_and_url()
    {
        var json = """
            { "tag_name": "v0.3.0", "html_url": "https://github.com/o/r/releases/tag/v0.3.0", "name": "0.3.0" }
            """;
        var rel = GitHubReleases.ParseLatest(json);
        Assert.That(rel, Is.Not.Null);
        Assert.That(rel!.TagName, Is.EqualTo("v0.3.0"));
        Assert.That(rel.HtmlUrl, Is.EqualTo("https://github.com/o/r/releases/tag/v0.3.0"));
    }

    [Test]
    public void ParseLatest_returns_null_without_tag()
    {
        Assert.That(GitHubReleases.ParseLatest("""{ "message": "Not Found" }"""), Is.Null);
    }

    [Test]
    public void ParseLatest_returns_null_on_garbage()
    {
        Assert.That(GitHubReleases.ParseLatest("not json"), Is.Null);
    }
}
