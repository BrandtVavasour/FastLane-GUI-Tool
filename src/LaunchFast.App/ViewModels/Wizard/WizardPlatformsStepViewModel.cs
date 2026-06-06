using CommunityToolkit.Mvvm.ComponentModel;

namespace LaunchFast.App.ViewModels.Wizard;

/// <summary>
/// First wizard step: which platforms to scaffold. In add-to-existing mode the
/// <c>*AlreadyPresent</c> flags tell the view a platform already has fastlane (so
/// the user is adding lanes rather than a fresh platform).
/// </summary>
public sealed partial class WizardPlatformsStepViewModel : ObservableObject
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsValid))]
    private bool _ios;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsValid))]
    private bool _android;

    public bool IosAlreadyPresent { get; set; }

    public bool AndroidAlreadyPresent { get; set; }

    public bool IsValid => Ios || Android;
}
