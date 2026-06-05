using LaunchFast.Core.Models;
using LaunchFast.Core.Stores;

namespace LaunchFast.Core.Tests;

[TestFixture]
public sealed class PlayStoreClientTests
{
    private static string Fixture(string name) =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "fixtures", name));

    [Test]
    public void MapTracks_maps_internal_and_production()
    {
        var result = PlayStoreClient.MapTracks(Fixture("play-tracks.json"));

        Assert.Multiple(() =>
        {
            Assert.That(result.ContainsKey("internal"), Is.True);
            Assert.That(result["internal"].Available, Is.True);
            Assert.That(result["internal"].Line, Is.EqualTo("1.4.2 (17)"));
            Assert.That(result["internal"].Destination, Is.EqualTo(Destination.PlayInternal));

            Assert.That(result["production"].Available, Is.True);
            Assert.That(result["production"].Line, Is.EqualTo("1.4.0 (15)"));
            Assert.That(result["production"].Destination, Is.EqualTo(Destination.PlayProduction));
        });
    }

    [Test]
    public void MapTracks_prefers_completed_release()
    {
        var result = PlayStoreClient.MapTracks(Fixture("play-tracks.json"));

        Assert.Multiple(() =>
        {
            Assert.That(result["beta"].Available, Is.True);
            Assert.That(result["beta"].Line, Is.EqualTo("1.4.0 (15)"));
            Assert.That(result["beta"].Destination, Is.EqualTo(Destination.PlayBeta));
        });
    }

    [Test]
    public void MapTracks_returns_empty_on_garbage()
    {
        var result = PlayStoreClient.MapTracks("not json at all");

        Assert.That(result, Is.Empty);
    }

    [Test]
    public void MapTracks_returns_empty_on_empty_object()
    {
        var result = PlayStoreClient.MapTracks("{}");

        Assert.That(result, Is.Empty);
    }

    [Test]
    public void MapTracks_skips_tracks_without_releases()
    {
        var result = PlayStoreClient.MapTracks("""{"tracks":[{"track":"internal","releases":[]}]}""");

        Assert.That(result.ContainsKey("internal"), Is.False);
    }

    [Test]
    public void TrackName_maps_play_destinations()
    {
        Assert.Multiple(() =>
        {
            Assert.That(PlayStoreClient.TrackName(Destination.PlayInternal), Is.EqualTo("internal"));
            Assert.That(PlayStoreClient.TrackName(Destination.PlayBeta), Is.EqualTo("beta"));
            Assert.That(PlayStoreClient.TrackName(Destination.PlayProduction), Is.EqualTo("production"));
        });
    }
}
