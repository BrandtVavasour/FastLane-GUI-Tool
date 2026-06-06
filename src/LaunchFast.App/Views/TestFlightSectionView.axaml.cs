using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace LaunchFast.App.Views;

public partial class TestFlightSectionView : UserControl
{
    public TestFlightSectionView()
    {
        InitializeComponent();
    }

    void InitializeComponent() => AvaloniaXamlLoader.Load(this);
}
