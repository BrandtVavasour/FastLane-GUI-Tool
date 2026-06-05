using LaunchFast.Core.Models;

namespace LaunchFast.Core.Stores;

public interface IAppStoreConnectClient
{
    Task<StoreStatus> GetStatusAsync(string bundleId, Destination destination, CancellationToken ct = default);
}
