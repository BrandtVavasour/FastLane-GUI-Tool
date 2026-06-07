using System.IO;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Platform.Storage;
using LaunchFast.App.ViewModels;

namespace LaunchFast.App.Views;

public partial class ScreenshotsSectionView : UserControl
{
    public ScreenshotsSectionView()
    {
        InitializeComponent();
    }

    void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    async void OnPreviewAll(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not ScreenshotsSectionViewModel vm) return;
        if (vm.ScreenshotsFolder is not { } folder) return;

        var launcher = TopLevel.GetTopLevel(this)?.Launcher;
        if (launcher is null) return;

        await launcher.LaunchDirectoryInfoAsync(new DirectoryInfo(folder));
    }
}
