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
    public async Task Android_destinations_are_unavailable_when_no_play_client()
    {
        var provider = new StoreStatusProvider(new CountingClient());

        var status = await provider.GetAsync("com.example.app", Lane("internal", Platform.Android));

        Assert.Multiple(() =>
        {
            Assert.That(status.Available, Is.False);
            Assert.That(status.Destination, Is.EqualTo(Destination.PlayInternal));
        });
    }

    [Test]
    public async Task Android_lane_uses_play_client()
    {
        var play = new CountingPlayClient();
        var provider = new StoreStatusProvider(asc: null, play: play);

        var status = await provider.GetAsync("com.example.app", Lane("beta", Platform.Android));

        Assert.Multiple(() =>
        {
            Assert.That(status.Available, Is.True);
            Assert.That(status.Destination, Is.EqualTo(Destination.PlayBeta));
            Assert.That(status.Line, Is.EqualTo("1.4.0 (15)"));
        });
    }

    [Test]
    public async Task Android_success_is_cached()
    {
        var play = new CountingPlayClient();
        var provider = new StoreStatusProvider(asc: null, play: play);

        var first = await provider.GetAsync("com.example.app", Lane("internal", Platform.Android));
        var second = await provider.GetAsync("com.example.app", Lane("internal", Platform.Android));

        Assert.Multiple(() =>
        {
            Assert.That(first.Available, Is.True);
            Assert.That(second, Is.EqualTo(first));
            Assert.That(play.Calls, Is.EqualTo(1));
        });
    }

    [Test]
    public async Task Android_throwing_play_client_is_unavailable()
    {
        var provider = new StoreStatusProvider(asc: null, play: new ThrowingPlayClient());

        var status = await provider.GetAsync("com.example.app", Lane("production", Platform.Android));

        Assert.Multiple(() =>
        {
            Assert.That(status.Available, Is.False);
            Assert.That(status.Destination, Is.EqualTo(Destination.PlayProduction));
        });
    }

    [Test]
    public async Task Unavailable_result_is_not_cached_so_a_transient_failure_retries()
    {
        var client = new FailThenSucceedClient();
        var provider = new StoreStatusProvider(client);

        var first = await provider.GetAsync("com.example.app", Lane("beta", Platform.Ios));
        var second = await provider.GetAsync("com.example.app", Lane("beta", Platform.Ios));

        Assert.Multiple(() =>
        {
            Assert.That(first.Available, Is.False, "first call fails and must not be cached");
            Assert.That(second.Available, Is.True, "second call retries and succeeds");
            Assert.That(client.Calls, Is.EqualTo(2));
        });
    }

    private sealed class ThrowingClient : IAppStoreConnectClient
    {
        public Task<StoreStatus> GetStatusAsync(string bundleId, Destination destination, CancellationToken ct = default) =>
            throw new InvalidOperationException("boom");
    }

    private sealed class FailThenSucceedClient : IAppStoreConnectClient
    {
        public int Calls { get; private set; }

        public Task<StoreStatus> GetStatusAsync(string bundleId, Destination destination, CancellationToken ct = default)
        {
            Calls++;
            if (Calls == 1)
            {
                throw new InvalidOperationException("transient");
            }

            return Task.FromResult(new StoreStatus(destination, true, "1.0.0 live", null));
        }
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

    private sealed class CountingPlayClient : IPlayStoreClient
    {
        public int Calls { get; private set; }

        public Task<StoreStatus> GetStatusAsync(string packageName, Destination destination, CancellationToken ct = default)
        {
            Calls++;
            return Task.FromResult(new StoreStatus(destination, true, "1.4.0 (15)", null));
        }
    }

    private sealed class ThrowingPlayClient : IPlayStoreClient
    {
        public Task<StoreStatus> GetStatusAsync(string packageName, Destination destination, CancellationToken ct = default) =>
            throw new InvalidOperationException("boom");
    }
}
