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

    /// <summary>
    /// Types <paramref name="text"/> as a keyboard does: each character's key goes down, its text arrives and the key comes
    /// up. Unlike <see cref="TypeText"/>, the keys also reach the window's single-key shortcuts when no field has the
    /// keyboard, so a test sees what a person's typing would do wherever the keyboard is. Letters, digits and spaces only.
    /// </summary>
    private static void TypeKeys(Window window, string text)
    {
        foreach (char character in text)
        {
            var (key, physical) = character switch
            {
                ' ' => (Key.Space, PhysicalKey.Space),
                _ when char.IsAsciiDigit(character) => (Enum.Parse<Key>("D" + character), Enum.Parse<PhysicalKey>("Digit" + character)),
                _ when char.IsAsciiLetter(character) => (Enum.Parse<Key>(char.ToUpperInvariant(character).ToString()),
                    Enum.Parse<PhysicalKey>(char.ToUpperInvariant(character).ToString())),
                _ => throw new ArgumentException($"“{character}” has no key in this helper.", nameof(text))
            };
            string symbol = character.ToString();
            window.KeyPress(key, RawInputModifiers.None, physical, symbol);
            Dispatcher.UIThread.RunJobs();
            window.KeyTextInput(symbol);
            Dispatcher.UIThread.RunJobs();
            window.KeyRelease(key, RawInputModifiers.None, physical, symbol);
            Dispatcher.UIThread.RunJobs();
        }
    }

    /// <summary>The control that has the keyboard, as “Type#Name”, or “nothing” when no control has it.</summary>
    private static string KeyboardOwner(Window window) => window.FocusManager?.GetFocusedElement() switch
    {
        null => "nothing",
        Control control => $"{control.GetType().Name}#{control.Name}",
        var other => other.GetType().Name
    };

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

    /// <summary>
    /// A left click on <paramref name="control"/>: the press, then the release, at its centre or at <paramref name="at"/>
    /// in its own coordinates.
    /// </summary>
    private static void ClickAt(Window window, Control control, Point? at = null)
    {
        window.UpdateLayout();
        var point = Assert.IsType<Point>(control.TranslatePoint(at ?? new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), window));
        window.MouseDown(point, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
        window.MouseUp(point, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
    }

    /// <summary>
    /// Presses Tab until <paramref name="control"/> has the keyboard, as a person tabs to it, and fails after
    /// <paramref name="limit"/> presses.
    /// </summary>
    private static void TabTo(Window window, Control control, int limit = 80)
    {
        for (int presses = 0; presses < limit && !control.IsFocused; presses++) PressKey(window, Key.Tab, PhysicalKey.Tab);
        Assert.True(control.IsFocused, $"Tab never reached {control.Name}; the keyboard is on {KeyboardOwner(window)}.");
    }
}
