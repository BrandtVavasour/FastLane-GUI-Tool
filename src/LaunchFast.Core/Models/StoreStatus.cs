namespace LaunchFast.Core.Models;

public sealed record StoreStatus(Destination Destination, bool Available, string? Line, string? Secondary)
{
    public static StoreStatus Unavailable(Destination destination) => new(destination, false, null, null);
    public static readonly StoreStatus None = new(Destination.None, false, null, null);
}
