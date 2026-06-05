using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using LaunchFast.App.ViewModels;

namespace LaunchFast.App.Views;

public partial class SecretsDialog : Window
{
    public SecretsDialog()
    {
        InitializeComponent();
    }

    void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    void OnSave(object? sender, RoutedEventArgs e)
    {
        if (DataContext is SecretsDialogViewModel vm) vm.Save();
        Close(true);
    }

    void OnCancel(object? sender, RoutedEventArgs e) => Close(false);
}
