using System;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using LaunchFast.App.ViewModels;

namespace LaunchFast.App.Views;

public partial class SecretsSectionView : UserControl
{
    public SecretsSectionView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (DataContext is SecretsSectionViewModel vm)
            vm.Editor = EditAsync;
    }

    /// <summary>
    /// Opens the existing secrets dialog to capture a single value and write it to
    /// the section's secret store (the macOS Keychain in production). The section
    /// VM refreshes the row list after this returns.
    /// </summary>
    async Task EditAsync(SecretRowViewModel row)
    {
        if (DataContext is not SecretsSectionViewModel vm) return;
        if (TopLevel.GetTopLevel(this) is not Window owner) return;

        var dialogVm = new SecretsDialogViewModel(vm.Store, vm.ProjectId, new[] { row.Name });
        var dialog = new SecretsDialog { DataContext = dialogVm };
        await dialog.ShowDialog<bool>(owner);
    }
}
