using CommunityToolkit.Mvvm.ComponentModel;
using LaunchFast.Core.Models;

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
    private StoreStatus? _store;

    public string? StoreLine => Store?.Line;
    public string? StoreSecondary => Store?.Secondary;

    /// <summary>True when a store status is available and meaningful.</summary>
    public bool HasStore => Store is { Available: true };

    /// <summary>
    /// True when the lane has a real store destination but its status could not be
    /// fetched (missing creds / failure) — drives the muted "unavailable" hint.
    /// </summary>
    public bool StoreUnavailable =>
        Store is { Available: false } && Store.Destination != Destination.None;
}
