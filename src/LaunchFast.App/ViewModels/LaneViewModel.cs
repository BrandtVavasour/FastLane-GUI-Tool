using CommunityToolkit.Mvvm.ComponentModel;
using LaunchFast.Core.Models;
using LaunchFast.Core.Stores;

namespace LaunchFast.App.ViewModels;

/// <summary>
/// A single fastlane lane plus the directory from which it must be executed
/// (the parent of the fastlane dir, where the Gemfile lives), and the lane's
/// current store status (fetched asynchronously after load).
/// </summary>
public sealed partial class LaneViewModel : ObservableObject
{
    private readonly Lane _lane;
    private readonly string _platformDir;

    public LaneViewModel(Lane lane, string platformDir)
    {
        _lane = lane;
        _platformDir = platformDir;
    }

    public Lane Lane => _lane;
    public string Name => _lane.Name;
    public string Description => _lane.Description;
    public Platform Platform => _lane.Platform;

    /// <summary>Working directory for the run (e.g. the <c>ios/</c> or <c>android/</c> dir).</summary>
    public string PlatformDir => _platformDir;

    /// <summary>Current store status for this lane's destination, or null until fetched.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StoreLine))]
    [NotifyPropertyChangedFor(nameof(StoreSecondary))]
    [NotifyPropertyChangedFor(nameof(HasStore))]
    [NotifyPropertyChangedFor(nameof(StoreUnavailable))]
    [NotifyPropertyChangedFor(nameof(StoreAmber))]
    private StoreStatus? _store;

    public string? StoreLine => Store?.Line;
    public string? StoreSecondary => Store?.Secondary;

    /// <summary>
    /// True for a production/release lane (App Store / Play production), used to
    /// surface the small RELEASE tag in the lane row. Derived from the lane name
    /// and its store destination — no extra state required.
    /// </summary>
    public bool IsRelease =>
        _lane.Name is "release" or "production"
        || LaneDestination.For(_lane) is Destination.AppStore or Destination.PlayProduction;

    /// <summary>
    /// True when the store reports a secondary state (e.g. "In Review"). Drives the
    /// amber status dot; otherwise the dot is green when a status is available.
    /// </summary>
    public bool StoreAmber => HasStore && !string.IsNullOrEmpty(Store?.Secondary);

    /// <summary>True when a store status is available and meaningful.</summary>
    public bool HasStore => Store is { Available: true };

    /// <summary>
    /// True when the lane has a real store destination but its status could not be
    /// fetched (missing creds / failure) — drives the muted "unavailable" hint.
    /// </summary>
    public bool StoreUnavailable =>
        Store is { Available: false } && Store.Destination != Destination.None;
}
