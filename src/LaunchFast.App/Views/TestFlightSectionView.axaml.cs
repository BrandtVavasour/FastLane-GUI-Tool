using System;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;

namespace LaunchFast.App.Views;

public partial class TestFlightSectionView : UserControl
{
    public TestFlightSectionView()
    {
        InitializeComponent();
    }

    void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    async void OnAddTesters(object? sender, RoutedEventArgs e)
    {
        var launcher = TopLevel.GetTopLevel(this)?.Launcher;
        if (launcher is null) return;

        await launcher.LaunchUriAsync(new Uri("https://appstoreconnect.apple.com/apps"));
    }
}
