using LaunchFast.Core.Models;

namespace LaunchFast.Core.Stores;

public interface IAppStoreConnectClient
{
    Task<StoreStatus> GetStatusAsync(string bundleId, Destination destination, CancellationToken ct = default);

    /// <summary>
    /// Reads the project's TestFlight state (newest build, beta groups, beta
    /// testers) from App Store Connect. Implementations degrade gracefully:
    /// an unresolvable app yields <see cref="TestFlightInfo.Empty"/>.
    /// </summary>
    Task<TestFlightInfo> GetTestFlightAsync(string bundleId, CancellationToken ct = default);
}
