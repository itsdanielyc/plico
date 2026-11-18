using Avalonia;

namespace Plico.Desktop;

internal static class Program
{
    [STAThread]
    public static int Main(string[] arguments) => BuildAvaloniaApp().StartWithClassicDesktopLifetime(arguments);

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>().UsePlatformDetect().WithInterFont();
}
