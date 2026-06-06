using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace LaunchFast.App.Views;

public partial class StoreListingSectionView : UserControl
{
    public StoreListingSectionView()
    {
        InitializeComponent();
    }

    void InitializeComponent() => AvaloniaXamlLoader.Load(this);
}
