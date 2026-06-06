using System.Text.RegularExpressions;

namespace LaunchFast.Core.Screenshots;

/// <summary>
/// Pure helpers for classifying iOS/Android deliver screenshots by device. Screenshot
/// files follow the fastlane deliver convention <c>&lt;device&gt;-&lt;index&gt;_&lt;...&gt;.png</c>
/// (e.g. <c>iPhone 17 Pro Max-04_map_en.png</c>); Android screenshots are grouped by
/// their fastlane <c>images/</c> sub-folder instead. Total — never throws.
/// </summary>
public static partial class ScreenshotDevice
{
    // device label = the file-name stem up to the first "-<digit>" (non-greedy so
    // "iPad Pro 13-inch (M5)-02_..." yields "iPad Pro 13-inch (M5)").
    [GeneratedRegex(@"^(?<d>.+?)-\d")]
    private static partial Regex LabelRegex();

    /// <summary>
    /// The device label parsed from an iOS deliver screenshot file name — the file-name
    /// stem up to the first <c>-&lt;digit&gt;</c>. Returns <c>"Other"</c> when the name
    /// does not match the convention.
    /// </summary>
    public static string Label(string path)
    {
        var stem = Path.GetFileNameWithoutExtension(path ?? string.Empty);
        var m = LabelRegex().Match(stem);
        if (!m.Success)
        {
            return "Other";
        }

        var device = m.Groups["d"].Value.Trim();
        return device.Length == 0 ? "Other" : device;
    }

    /// <summary>
    /// Whether a screenshot path belongs to the device-class <paramref name="classKey"/>
    /// used by the Store Listing device control. iOS classes (<c>"iPhone"</c>/<c>"iPad"</c>)
    /// match the file name; Android classes (<c>"Phone"</c>/<c>"Tablet"</c>) match the
    /// fastlane image sub-folder in the path. An unknown key returns true (don't hide).
    /// </summary>
    public static bool InClass(string path, string classKey)
    {
        var name = Path.GetFileName(path ?? string.Empty);
        var full = path ?? string.Empty;

        bool NameHas(string s) => name.Contains(s, StringComparison.OrdinalIgnoreCase);
        bool PathHas(string s) => full.Contains(s, StringComparison.OrdinalIgnoreCase);

        return classKey switch
        {
            _ when string.Equals(classKey, "iPhone", StringComparison.OrdinalIgnoreCase) =>
                NameHas("iphone") && !NameHas("ipad"),
            _ when string.Equals(classKey, "iPad", StringComparison.OrdinalIgnoreCase) =>
                NameHas("ipad"),
            _ when string.Equals(classKey, "Phone", StringComparison.OrdinalIgnoreCase) =>
                PathHas("phonescreenshots"),
            _ when string.Equals(classKey, "Tablet", StringComparison.OrdinalIgnoreCase) =>
                PathHas("teninchscreenshots") || PathHas("seveninchscreenshots")
                || PathHas("tabletscreenshots") || PathHas("tvscreenshots"),
            _ => true,
        };
    }
}
