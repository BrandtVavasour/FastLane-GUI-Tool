using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Data.Core;
using Avalonia.Data.Core.Plugins;
using System.Linq;
using Avalonia.Markup.Xaml;
using LaunchFast.App.Services;
using LaunchFast.App.Views;

namespace LaunchFast.App;

public partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var launcher = AppServices.CreateLauncher();
            launcher.Load();
            desktop.MainWindow = new MainWindow
            {
                DataContext = launcher,
            };
        }

        base.OnFrameworkInitializationCompleted();
    }
}