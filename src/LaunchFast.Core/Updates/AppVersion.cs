using System.Reflection;

namespace LaunchFast.Core.Updates;

/// <summary>The running application's version, read from the entry assembly. In a dev
/// build this is the <c>Version</c> from Directory.Build.props; in a released build it
/// is the tag the release was built from.</summary>
public static class AppVersion
{
    public static string Current =>
        Assembly.GetEntryAssembly()?.GetName().Version?.ToString(3) ?? "0.0.0";
}
