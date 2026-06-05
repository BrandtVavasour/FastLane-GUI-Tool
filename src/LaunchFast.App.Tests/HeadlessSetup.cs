using Avalonia;
using Avalonia.Headless;
using LaunchFast.App;
using LaunchFast.App.Tests;

[assembly: AvaloniaTestApplication(typeof(TestAppBuilder))]

namespace LaunchFast.App.Tests;

public static class TestAppBuilder
{
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions());
}
