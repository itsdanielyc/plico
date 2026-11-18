using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Media.Imaging;

namespace Plico.Desktop;

internal static class NativeVerification
{
    internal static void Run(IClassicDesktopStyleApplicationLifetime desktop, MainWindow window, string evidenceDirectory)
    {
        try
        {
            Directory.CreateDirectory(evidenceDirectory);
            if (!window.IsVisible || window.FindControl<TextBlock>("InboxHeading")?.Text != "Inbox")
            {
                throw new InvalidOperationException("The native inbox did not open.");
            }

            using var bitmap = new RenderTargetBitmap(PixelSize.FromSize(window.Bounds.Size, 1), new Vector(96, 96));
            bitmap.Render(window);
            bitmap.Save(Path.Combine(evidenceDirectory, "inbox.png"), PngBitmapEncoderOptions.Default);
            File.WriteAllText(Path.Combine(evidenceDirectory, "passed.txt"), "Native inbox opened");
            desktop.Shutdown(0);
        }
        catch (Exception error)
        {
            File.WriteAllText(Path.Combine(evidenceDirectory, "error.txt"), error.ToString());
            desktop.Shutdown(1);
        }
    }
}
