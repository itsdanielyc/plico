using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace Plico.Desktop;

/// <summary>
/// Attached presentation details. <c>Ui.Icon</c> lets a button keep plain-text content (which screen readers
/// and tests read) while its style template draws a leading icon.
/// </summary>
public sealed class Ui : AvaloniaObject
{
    /// <summary>The leading icon for a button styled with the <c>with-icon</c> or <c>action</c> class.</summary>
    public static readonly AttachedProperty<Geometry?> IconProperty =
        AvaloniaProperty.RegisterAttached<Ui, Control, Geometry?>("Icon");

    /// <summary>The shortcut shown as keycaps beside a button label, such as "R" or "Ctrl+K".</summary>
    public static readonly AttachedProperty<string?> ShortcutProperty =
        AvaloniaProperty.RegisterAttached<Ui, Control, string?>("Shortcut");

    public static Geometry? GetIcon(Control element) => element.GetValue(IconProperty);
    public static void SetIcon(Control element, Geometry? value) => element.SetValue(IconProperty, value);
    public static string? GetShortcut(Control element) => element.GetValue(ShortcutProperty);
    public static void SetShortcut(Control element, string? value) => element.SetValue(ShortcutProperty, value);
}

/// <summary>Reads the system preference for reduced motion. Plico keeps every state change and drops the travel.</summary>
internal static class MotionPreference
{
    private const uint ClientAreaAnimation = 0x1042;
    private const string OverrideVariable = "PLICO_REDUCE_MOTION";

    /// <summary>True when the user asked the system to reduce animation, or the override variable is set.</summary>
    internal static bool IsReduced
    {
        get
        {
            if (Environment.GetEnvironmentVariable(OverrideVariable) is { } value) return value == "1";
            if (!OperatingSystem.IsWindows()) return false;
            try
            {
                return SystemParametersInfo(ClientAreaAnimation, 0, out int enabled, 0) && enabled == 0;
            }
            catch (Exception error) when (error is DllNotFoundException or EntryPointNotFoundException)
            {
                return false;
            }
        }
    }

    // DllImport keeps the desktop project free of unsafe code; the call has only blittable arguments.
    [DllImport("user32.dll", EntryPoint = "SystemParametersInfoW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SystemParametersInfo(uint action, uint parameter, out int value, uint update);
}
