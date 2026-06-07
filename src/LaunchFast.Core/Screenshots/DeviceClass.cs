namespace LaunchFast.Core.Screenshots;

/// <summary>
/// Pure classifier that maps a raw device string — either a Snapfile
/// <c>devices([...])</c> entry or a screenshot device label parsed by
/// <see cref="ScreenshotDevice.Label(string)"/> — to one of the standard device-class
/// buckets surfaced as toggles on the Screenshots section. Total — never throws.
/// </summary>
public static class DeviceClass
{
    // Class keys for the 5 standard device buckets the Screenshots UI shows.
    public const string IPhone69 = "iphone-6.9";   // 16/17 Pro Max class
    public const string IPhone65 = "iphone-6.5";   // 11 Pro Max / XS Max
    public const string IPhone55 = "iphone-5.5";   // 8 Plus
    public const string IPad13 = "ipad-13";        // iPad Pro 12.9/13"
    public const string IPad11 = "ipad-11";        // iPad Pro 11"

    /// <summary>
    /// Maps a raw device string to one of the class keys, or <c>null</c> when it does
    /// not map. Case-insensitive and total.
    /// </summary>
    public static string? Classify(string device)
    {
        if (string.IsNullOrWhiteSpace(device))
        {
            return null;
        }

        bool Has(string token) => device.Contains(token, StringComparison.OrdinalIgnoreCase);

        if (Has("ipad"))
        {
            return Has("11") ? IPad11 : IPad13;
        }

        if (Has("11 pro max") || Has("xs max") || Has("6.5"))
        {
            return IPhone65;
        }

        if (Has("8 plus") || Has("7 plus") || Has("6 plus") || Has("5.5"))
        {
            return IPhone55;
        }

        if (Has("pro max") || Has("plus") || Has("6.9")
            || Has("15 pro") || Has("16 pro") || Has("17 pro") || Has("18 pro"))
        {
            return IPhone69;
        }

        return null;
    }
}
