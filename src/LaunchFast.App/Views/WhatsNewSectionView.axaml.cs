using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace LaunchFast.App.Views;

public partial class WhatsNewSectionView : UserControl
{
    public WhatsNewSectionView()
    {
        InitializeComponent();
    }

    void InitializeComponent() => AvaloniaXamlLoader.Load(this);
}
