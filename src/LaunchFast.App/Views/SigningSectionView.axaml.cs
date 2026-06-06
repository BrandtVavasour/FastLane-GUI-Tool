using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace LaunchFast.App.Views;

public partial class SigningSectionView : UserControl
{
    public SigningSectionView()
    {
        InitializeComponent();
    }

    void InitializeComponent() => AvaloniaXamlLoader.Load(this);
}
