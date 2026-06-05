using System.Collections.Concurrent;
using LaunchFast.Core.Models;

namespace LaunchFast.Core.Stores;

/// <summary>
/// Resolves, per lane, the current store status of an app's destination. Results
/// are cached by (bundleId, destination) and degrade gracefully: a missing client
/// or any client failure yields an <see cref="StoreStatus.Unavailable"/> status
/// rather than throwing.
/// </summary>
public sealed class StoreStatusProvider
{
    private readonly IAppStoreConnectClient? _asc;
    private readonly IPlayStoreClient? _play;
    private readonly ConcurrentDictionary<(string BundleId, Destination Destination), StoreStatus> _cache = new();

    public StoreStatusProvider(IAppStoreConnectClient? asc, IPlayStoreClient? play = null)
    {
        _asc = asc;
        _play = play;
    }

    public async Task<StoreStatus> GetAsync(string bundleId, Lane lane, CancellationToken ct = default)
    {
        var destination = LaneDestination.For(lane);
        if (destination == Destination.None)
        {
            return StoreStatus.None;
        }

        var key = (bundleId, destination);
        if (_cache.TryGetValue(key, out var cached))
        {
            return cached;
        }

        var status = await ResolveAsync(bundleId, destination, ct).ConfigureAwait(false);
        // Only cache successful lookups so a transient failure doesn't permanently
        // suppress retries until Refresh() is called.
        if (status.Available)
        {
            _cache[key] = status;
        }
        return status;
    }

    private async Task<StoreStatus> ResolveAsync(string bundleId, Destination destination, CancellationToken ct)
    {
        switch (destination)
        {
            case Destination.TestFlight:
            case Destination.AppStore:
                if (_asc is null)
                {
                    return StoreStatus.Unavailable(destination);
                }
                try
                {
                    return await _asc.GetStatusAsync(bundleId, destination, ct).ConfigureAwait(false);
                }
                catch
                {
                    return StoreStatus.Unavailable(destination);
                }

            case Destination.PlayInternal:
            case Destination.PlayBeta:
            case Destination.PlayProduction:
                if (_play is null)
                {
                    return StoreStatus.Unavailable(destination);
                }
                try
                {
                    return await _play.GetStatusAsync(bundleId, destination, ct).ConfigureAwait(false);
                }
                catch
                {
                    return StoreStatus.Unavailable(destination);
                }

            default:
                return StoreStatus.Unavailable(destination);
        }
    }

    public void Refresh() => _cache.Clear();
}
