using CommunityToolkit.Mvvm.ComponentModel;
using LaunchFast.Core.Scaffolding;

namespace LaunchFast.App.ViewModels.Wizard;

/// <summary>
/// Final step: shows the built <see cref="ScaffoldPlan"/> (the file changes the
/// user is about to apply). The wizard sets <see cref="Plan"/> when advancing in.
/// </summary>
public sealed partial class WizardReviewStepViewModel : ObservableObject
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Files))]
    private ScaffoldPlan? _plan;

    public IReadOnlyList<FileChange> Files => Plan?.Files ?? [];
}
