using LaunchFast.Core.Models;

namespace LaunchFast.App.ViewModels;

/// <summary>
/// A single fastlane lane plus the directory from which it must be executed
/// (the parent of the fastlane dir, where the Gemfile lives).
/// </summary>
public sealed class LaneViewModel(Lane lane, string platformDir)
{
    public Lane Lane => lane;
    public string Name => lane.Name;
    public string Description => lane.Description;
    public Platform Platform => lane.Platform;

    /// <summary>Working directory for the run (e.g. the <c>ios/</c> or <c>android/</c> dir).</summary>
    public string PlatformDir => platformDir;
}
