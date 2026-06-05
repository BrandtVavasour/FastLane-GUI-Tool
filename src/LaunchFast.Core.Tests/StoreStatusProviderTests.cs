using LaunchFast.Core.Models;
using LaunchFast.Core.Stores;

namespace LaunchFast.Core.Tests;

[TestFixture]
public sealed class StoreStatusProviderTests
{
    private static Lane Lane(string name, Platform platform) => new(name, "desc", platform);

    [Test]
    public async Task Returns_none_for_screenshots_lane()
    {
        var provider = new StoreStatusProvider(new ThrowingClient());

        var status = await provider.GetAsync("com.example.app", Lane("screenshots", Platform.Ios));

        Assert.That(status, Is.EqualTo(StoreStatus.None));
    }

    [Test]
    public async Task Returns_unavailable_when_client_throws()
    {
        var provider = new StoreStatusProvider(new ThrowingClient());

        var status = await provider.GetAsync("com.example.app", Lane("beta", Platform.Ios));

        Assert.Multiple(() =>
        {
            Assert.That(status.Available, Is.False);
            Assert.That(status.Destination, Is.EqualTo(Destination.TestFlight));
        });
    }

    [Test]
    public async Task Returns_unavailable_when_no_ios_client()
    {
        var provider = new StoreStatusProvider(asc: null);

        var status = await provider.GetAsync("com.example.app", Lane("release", Platform.Ios));

        Assert.Multiple(() =>
        {
            Assert.That(status.Available, Is.False);
            Assert.That(status.Destination, Is.EqualTo(Destination.AppStore));
        });
    }

    [Test]
    public async Task Caches_successful_result()
    {
        var client = new CountingClient();
        var provider = new StoreStatusProvider(client);

        var first = await provider.GetAsync("com.example.app", Lane("beta", Platform.Ios));
        var second = await provider.GetAsync("com.example.app", Lane("beta", Platform.Ios));

        Assert.Multiple(() =>
        {
            Assert.That(first.Available, Is.True);
            Assert.That(second, Is.EqualTo(first));
            Assert.That(client.Calls, Is.EqualTo(1));
        });
    }

    [Test]
    public async Task Refresh_clears_the_cache()
    {
        var client = new CountingClient();
        var provider = new StoreStatusProvider(client);

        await provider.GetAsync("com.example.app", Lane("beta", Platform.Ios));
        provider.Refresh();
        await provider.GetAsync("com.example.app", Lane("beta", Platform.Ios));

        Assert.That(client.Calls, Is.EqualTo(2));
    }

    [Test]
    public async Task Android_destinations_are_unavailable_for_now()
    {
        var provider = new StoreStatusProvider(new CountingClient());

        var status = await provider.GetAsync("com.example.app", Lane("internal", Platform.Android));

        Assert.Multiple(() =>
        {
            Assert.That(status.Available, Is.False);
            Assert.That(status.Destination, Is.EqualTo(Destination.PlayInternal));
        });
    }

    private sealed class ThrowingClient : IAppStoreConnectClient
    {
        public Task<StoreStatus> GetStatusAsync(string bundleId, Destination destination, CancellationToken ct = default) =>
            throw new InvalidOperationException("boom");
    }

    private sealed class CountingClient : IAppStoreConnectClient
    {
        public int Calls { get; private set; }

        public Task<StoreStatus> GetStatusAsync(string bundleId, Destination destination, CancellationToken ct = default)
        {
            Calls++;
            return Task.FromResult(new StoreStatus(destination, true, "1.0.0 live", null));
        }
    }
}
