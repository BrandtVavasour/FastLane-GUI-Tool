using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace LaunchFast.App.Views;

public partial class ReleaseSectionView : UserControl
{
    public ReleaseSectionView()
    {
        InitializeComponent();
    }

    void InitializeComponent() => AvaloniaXamlLoader.Load(this);
}
