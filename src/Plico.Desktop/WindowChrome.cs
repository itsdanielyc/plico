using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;

namespace Plico.Desktop;

/// <summary>
/// Tints the native Windows 11 caption to the desk (<c>Base</c>) with <c>Fg</c> text, so the title bar and the
/// sidebar read as one grey surface. Applies to every window, including Settings and the dialogs, and follows
/// theme changes. Elsewhere the native caption is left alone.
/// </summary>
internal static class WindowChrome
{
    private const int BorderColorAttribute = 34;
    private const int CaptionColorAttribute = 35;
    private const int TextColorAttribute = 36;
    private const int FirstWindows11Build = 22000;
    private static readonly ConditionalWeakTable<Window, object> Attached = [];

    /// <summary>Tints each window as it opens. Call once when the application starts.</summary>
    internal static void Register() =>
        Window.WindowOpenedEvent.AddClassHandler<Window>((window, _) => Attach(window), RoutingStrategies.Direct | RoutingStrategies.Bubble);

    private static void Attach(Window window)
    {
        Apply(window);
        if (!Attached.TryAdd(window, window)) return;
        // Avalonia updates the frame's own dark mode when the theme changes, so tint again once it has.
        window.ActualThemeVariantChanged += (_, _) => Dispatcher.UIThread.Post(() => Apply(window), DispatcherPriority.Background);
    }

    /// <summary>Sets the caption, border and caption text colours from the window's current theme.</summary>
    internal static void Apply(Window window)
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, FirstWindows11Build)) return;
        if (window.TryGetPlatformHandle() is not { HandleDescriptor: "HWND", Handle: var handle } || handle == IntPtr.Zero) return;
        var theme = window.ActualThemeVariant;
        if (!window.TryFindResource("Base", theme, out var desk) || desk is not ISolidColorBrush caption) return;
        if (!window.TryFindResource("Fg", theme, out var ink) || ink is not ISolidColorBrush text) return;
        try
        {
            SetColor(handle, CaptionColorAttribute, caption.Color);
            SetColor(handle, BorderColorAttribute, caption.Color);
            SetColor(handle, TextColorAttribute, text.Color);
        }
        catch (Exception error) when (error is DllNotFoundException or EntryPointNotFoundException)
        {
            // The caption is decoration; a system without the DWM attributes keeps its own colours.
        }
    }

    /// <summary>A Win32 COLORREF is 0x00BBGGRR.</summary>
    internal static uint ColorRef(Color color) => (uint)(color.R | color.G << 8 | color.B << 16);

    private static void SetColor(IntPtr window, int attribute, Color color)
    {
        uint value = ColorRef(color);
        _ = DwmSetWindowAttribute(window, attribute, ref value, sizeof(uint));
    }

    // DllImport keeps the desktop project free of unsafe code; the call has only blittable arguments.
    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref uint value, int size);
}
