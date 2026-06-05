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
    private readonly ConcurrentDictionary<(string BundleId, Destination Destination), StoreStatus> _cache = new();

    // Phase 9 will add an IPlayStoreClient? play parameter.
    public StoreStatusProvider(IAppStoreConnectClient? asc) => _asc = asc;

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
        _cache[key] = status;
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

            default:
                // Android destinations: filled in by Phase 9.
                return StoreStatus.Unavailable(destination);
        }
    }

    public void Refresh() => _cache.Clear();
}
