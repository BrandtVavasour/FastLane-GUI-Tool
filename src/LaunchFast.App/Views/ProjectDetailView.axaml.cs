using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using LaunchFast.App.ViewModels;

namespace LaunchFast.App.Views;

public partial class ProjectDetailView : UserControl
{
    public ProjectDetailView()
    {
        InitializeComponent();
    }

    void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    async void OnAddSecrets(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not ProjectDetailViewModel vm) return;
        if (TopLevel.GetTopLevel(this) is not Window owner) return;

        var dialogVm = new SecretsDialogViewModel(vm.Secrets, vm.ProjectId, vm.MissingSecrets);
        var dialog = new SecretsDialog { DataContext = dialogVm };

        await dialog.ShowDialog<bool>(owner);

        // Refresh banner + gating with whatever was just written.
        vm.Load();
    }

    async void OnCopyOutput(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not ProjectDetailViewModel vm) return;

        var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
        if (clipboard is null) return;

        await clipboard.SetTextAsync(vm.Run.AllText);
    }
}
