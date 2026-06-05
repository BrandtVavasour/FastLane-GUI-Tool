using LaunchFast.Core.Models;

namespace LaunchFast.Core.Stores;

public interface IPlayStoreClient
{
    Task<StoreStatus> GetStatusAsync(string packageName, Destination destination, CancellationToken ct = default);
}
