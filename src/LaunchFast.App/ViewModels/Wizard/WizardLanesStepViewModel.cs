using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace LaunchFast.App.ViewModels.Wizard;

/// <summary>One toggleable lane row: a lane name plus whether the user chose it.</summary>
public sealed partial class LaneCheck(string name) : ObservableObject
{
    public string Name => name;

    [ObservableProperty]
    private bool _chosen;
}

/// <summary>
/// Lanes step: the iOS and Android lane checklists. Populated from
/// <c>LaneTemplate.Available</c> (minus any lanes already present, in add mode);
/// all offered lanes default to chosen.
/// </summary>
public sealed class WizardLanesStepViewModel : ObservableObject
{
    public ObservableCollection<LaneCheck> IosLanes { get; } = [];

    public ObservableCollection<LaneCheck> AndroidLanes { get; } = [];

    public IReadOnlyList<string> ChosenIos =>
        IosLanes.Where(l => l.Chosen).Select(l => l.Name).ToList();

    public IReadOnlyList<string> ChosenAndroid =>
        AndroidLanes.Where(l => l.Chosen).Select(l => l.Name).ToList();

    public bool IsValid => true;

    /// <summary>Replaces the iOS lane offering, defaulting every lane to chosen.</summary>
    public void OfferIos(IEnumerable<string> lanes) => Offer(IosLanes, lanes);

    /// <summary>Replaces the Android lane offering, defaulting every lane to chosen.</summary>
    public void OfferAndroid(IEnumerable<string> lanes) => Offer(AndroidLanes, lanes);

    /// <summary>Sets exactly which offered iOS lanes are chosen (others unchecked).</summary>
    public void SetIos(IReadOnlyList<string> chosen) => Set(IosLanes, chosen);

    /// <summary>Sets exactly which offered Android lanes are chosen (others unchecked).</summary>
    public void SetAndroid(IReadOnlyList<string> chosen) => Set(AndroidLanes, chosen);

    static void Offer(ObservableCollection<LaneCheck> target, IEnumerable<string> lanes)
    {
        target.Clear();
        foreach (var name in lanes)
            target.Add(new LaneCheck(name) { Chosen = true });
    }

    static void Set(ObservableCollection<LaneCheck> target, IReadOnlyList<string> chosen)
    {
        foreach (var lane in target)
            lane.Chosen = chosen.Contains(lane.Name);
    }
}
