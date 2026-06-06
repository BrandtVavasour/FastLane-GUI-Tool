using Avalonia;
using Avalonia.Headless;
using LaunchFast.App.Tests;

[assembly: AvaloniaTestApplication(typeof(TestAppBuilder))]

namespace LaunchFast.App.Tests;

public static class TestAppBuilder
{
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UseSkia()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });
}
