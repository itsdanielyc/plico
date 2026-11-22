using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using Xunit;

namespace Plico.Tests;

/// <summary>
/// Input as a person gives it: keys pressed and released on the window, text typed one character at a time into
/// whatever has the keyboard, and clicks made of a press and a release at a control's centre. These go through the
/// same routing as the shipped app (focus changes on press, the focused field's own key handling, the window's
/// shortcuts), so a test that uses them covers the path a person takes.
/// </summary>
public sealed partial class InboxWindowTests
{
    /// <summary>Types <paramref name="text"/> into the focused control, one character at a time.</summary>
    private static void TypeText(Window window, string text)
    {
        foreach (char character in text)
        {
            window.KeyTextInput(character.ToString());
            Dispatcher.UIThread.RunJobs();
        }
    }

    /// <summary>Presses and releases one key on the window, with <paramref name="modifiers"/> held.</summary>
    private static void PressKey(Window window, Key key, PhysicalKey physical, string? symbol = null,
        RawInputModifiers modifiers = RawInputModifiers.None)
    {
        window.KeyPress(key, modifiers, physical, symbol);
        Dispatcher.UIThread.RunJobs();
        window.KeyRelease(key, modifiers, physical, symbol);
        Dispatcher.UIThread.RunJobs();
    }

    /// <summary>Ctrl+Z, or ⌘Z on macOS, pressed on the window wherever the keyboard is.</summary>
    private static void PressUndoChord(Window window) => PressKey(window, Key.Z, PhysicalKey.Z, "z",
        OperatingSystem.IsMacOS() ? RawInputModifiers.Meta : RawInputModifiers.Control);

    /// <summary>A left click at the centre of <paramref name="control"/>: the press, then the release.</summary>
    private static void ClickAt(Window window, Control control)
    {
        window.UpdateLayout();
        var centre = Assert.IsType<Point>(control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), window));
        window.MouseDown(centre, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
        window.MouseUp(centre, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
    }
}
