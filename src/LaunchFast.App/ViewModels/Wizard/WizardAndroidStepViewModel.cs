using CommunityToolkit.Mvvm.ComponentModel;

namespace LaunchFast.App.ViewModels.Wizard;

/// <summary>
/// Android step: the application id (package) plus the optional Play service-account
/// JSON key path (referenced from the generated Appfile via <c>ENV[...]</c>).
/// </summary>
public sealed partial class WizardAndroidStepViewModel : ObservableObject
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsValid))]
    private string? _package;

    [ObservableProperty]
    private string? _playJsonKeyPath;

    public bool IsValid => !string.IsNullOrWhiteSpace(Package);
}
