using System;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Platform.Storage;
using LaunchFast.App.ViewModels;

namespace LaunchFast.App.Views;

public partial class LauncherView : UserControl
{
    public LauncherView()
    {
        InitializeComponent();
    }

    void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    async void OnOpenProject(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        var path = await PickFolderAsync("Open project");
        if (path is null || DataContext is not LauncherViewModel vm) return;
        vm.Store.AddRecent(path);
        vm.Load();
    }

    async void OnRegisterWorkspace(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        var path = await PickFolderAsync("Register workspace");
        if (path is null || DataContext is not LauncherViewModel vm) return;
        vm.Store.AddWorkspace(path);
        vm.Load();
    }

    private void OnOpenUpdate(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is not LauncherViewModel vm) return;
        // Only open a well-formed https release URL (the value comes from GitHub's API
        // html_url, but validate the scheme defensively before handing it to the OS).
        if (!Uri.TryCreate(vm.UpdateUrl, UriKind.Absolute, out var uri) ||
            uri.Scheme != Uri.UriSchemeHttps)
        {
            return;
        }
        var top = TopLevel.GetTopLevel(this);
        if (top is null) return;
        _ = top.Launcher.LaunchUriAsync(uri);
    }

    async System.Threading.Tasks.Task<string?> PickFolderAsync(string title)
    {
        var top = TopLevel.GetTopLevel(this);
        if (top is null) return null;

        var folders = await top.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = title,
            AllowMultiple = false,
        });

        if (folders.Count == 0) return null;
        return folders[0].TryGetLocalPath();
    }
}
