using LaunchFast.Core.Models;

namespace LaunchFast.Core.Stores;

public static class LaneDestination
{
    public static Destination For(Lane lane) => lane.Platform switch
    {
        Platform.Ios => lane.Name switch
        {
            "beta" => Destination.TestFlight,
            "release" => Destination.AppStore,
            _ => Destination.None,
        },
        Platform.Android => lane.Name switch
        {
            "internal" => Destination.PlayInternal,
            "beta" => Destination.PlayBeta,
            "production" => Destination.PlayProduction,
            _ => Destination.None,
        },
        _ => Destination.None,
    };
}
