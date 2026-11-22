using Avalonia.Input;
using Avalonia.Interactivity;

namespace Plico.Desktop;

/// <summary>
/// The filing card's fields are edited where they are. The title's heading becomes a text field (E); Enter or leaving
/// it saves and Escape restores. The view model saves, offers Undo and rolls back what it cannot save.
/// </summary>
public partial class MainWindow
{
    private void WireFieldEditing()
    {
        // The field's own handlers run first; Enter and Escape still belong to the edit.
        TitleEditBox.AddHandler(KeyDownEvent, TitleEdit_KeyDown, RoutingStrategies.Bubble, handledEventsToo: true);
        TitleEditBox.LostFocus += (_, _) => ViewModel?.CommitTitleEdit();
    }

    /// <summary>Turns the heading into a field with the whole title selected, ready to be typed over.</summary>
    internal void BeginTitleEdit()
    {
        if (ViewModel is not { } model || !model.BeginTitleEdit()) return;
        // The binding has shown the field by the time the model returns, so it can take the keyboard now.
        TitleEditBox.Focus();
        TitleEditBox.SelectAll();
    }

    private void EditTitle_Click(object? sender, RoutedEventArgs e) => BeginTitleEdit();

    private void TitleEdit_KeyDown(object? sender, KeyEventArgs e)
    {
        if (ViewModel is not { } model) return;
        switch (e.Key)
        {
            case Key.Enter: model.CommitTitleEdit(); break;
            case Key.Escape: model.CancelTitleEdit(); break;
            default: return;
        }
        e.Handled = true;
        // Focus lands on the heading again, where E or Enter reopens it.
        EditTitleButton.Focus();
    }
}
