using LaunchFast.Core.Models;
using LaunchFast.Core.Stores;

namespace LaunchFast.Core.Tests;

[TestFixture]
public sealed class LaneDestinationTests
{
    private static Lane Lane(string name, Platform platform) => new(name, "desc", platform);

    [Test]
    public void Ios_beta_maps_to_TestFlight() =>
        Assert.That(LaneDestination.For(Lane("beta", Platform.Ios)), Is.EqualTo(Destination.TestFlight));

    [Test]
    public void Ios_release_maps_to_AppStore() =>
        Assert.That(LaneDestination.For(Lane("release", Platform.Ios)), Is.EqualTo(Destination.AppStore));

    [Test]
    public void Ios_screenshots_maps_to_None() =>
        Assert.That(LaneDestination.For(Lane("screenshots", Platform.Ios)), Is.EqualTo(Destination.None));

    [Test]
    public void Android_internal_maps_to_PlayInternal() =>
        Assert.That(LaneDestination.For(Lane("internal", Platform.Android)), Is.EqualTo(Destination.PlayInternal));

    [Test]
    public void Android_beta_maps_to_PlayBeta() =>
        Assert.That(LaneDestination.For(Lane("beta", Platform.Android)), Is.EqualTo(Destination.PlayBeta));

    [Test]
    public void Android_production_maps_to_PlayProduction() =>
        Assert.That(LaneDestination.For(Lane("production", Platform.Android)), Is.EqualTo(Destination.PlayProduction));
}
