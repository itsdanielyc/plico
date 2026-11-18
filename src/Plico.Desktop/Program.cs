using Avalonia;

namespace Plico.Desktop;

internal static class Program
{
    private const string VerifyUiThreadRenderingVariable = "PLICO_VERIFY_UI_THREAD_RENDERING";

    [STAThread]
    public static int Main(string[] arguments)
    {
        if (!OperatingSystem.IsMacOS())
        {
            var incoming = PdfFileActivation.ReadPaths(arguments);
            if (incoming.Length != 0 && PdfFileActivation.ForwardAsync(PdfFileActivation.UserPipeName, incoming).GetAwaiter().GetResult())
                return 0;
        }
        var builder = BuildAvaloniaApp();
        if (OperatingSystem.IsLinux() && arguments is [App.VerifySwitch, _, _] &&
            Environment.GetEnvironmentVariable(VerifyUiThreadRenderingVariable) == "1")
        {
            // Serialize the verification snapshots with composition when the render thread is emulated.
            builder.With(new X11PlatformOptions { ShouldRenderOnUIThread = true });
        }

        return builder.StartWithClassicDesktopLifetime(arguments);
    }

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>().UsePlatformDetect().WithInterFont();
}
