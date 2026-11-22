using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace Plico.Desktop;

/// <summary>
/// The filing card's fields are edited where they are. The title's heading becomes a text field (E) and the date is a
/// field with a calendar (D); Enter or leaving a field saves and Escape restores. The view model saves, offers Undo and
/// rolls back or explains what it cannot save.
/// </summary>
public partial class MainWindow
{
    private bool _showingDocumentDate;

    private void WireFieldEditing()
    {
        // The fields' own handlers run first; Enter and Escape still belong to the edit.
        TitleEditBox.AddHandler(KeyDownEvent, TitleEdit_KeyDown, RoutingStrategies.Bubble, handledEventsToo: true);
        TitleEditBox.LostFocus += (_, _) => ViewModel?.CommitTitleEdit();
        DocumentDateField.AddHandler(KeyDownEvent, DocumentDate_KeyDown, RoutingStrategies.Bubble, handledEventsToo: true);
        // The suggestion list takes Enter for itself; the tag is added after it has filled in its choice.
        TagBox.AddHandler(KeyDownEvent, TagBox_KeyDown, RoutingStrategies.Bubble, handledEventsToo: true);
        DocumentDateField.LostFocus += (_, e) =>
        {
            // The field's own buttons lose focus too, and the calendar takes it while a day is being chosen.
            if (ReferenceEquals(e.Source, DocumentDateField) && !DocumentDatePicker.IsOpen) ViewModel?.CommitDocumentDate();
        };
        DocumentDatePicker.PlacementTarget = DocumentDateField;
        DocumentDateCalendar.PropertyChanged += (_, change) =>
        {
            if (change.Property == Calendar.SelectedDateProperty) DocumentDateChosen();
        };
        DocumentDateCalendar.KeyDown += (_, e) =>
        {
            if (e.Key != Key.Escape) return;
            DocumentDatePicker.IsOpen = false;
            DocumentDateField.Focus();
            e.Handled = true;
        };
    }

    /// <summary>Turns the heading into a field with the whole title selected, ready to be typed over.</summary>
    internal void BeginTitleEdit()
    {
        if (ViewModel is not { } model || !model.BeginTitleEdit()) return;
        // The binding has shown the field by the time the model returns, so it can take the keyboard now.
        TitleEditBox.Focus();
        TitleEditBox.SelectAll();
    }

    /// <summary>Puts the keyboard in the date field with its date selected, ready to be typed over.</summary>
    internal void FocusDocumentDate()
    {
        if (ViewModel is not { CanWork: true, ShowFilingCard: true }) return;
        DocumentDateField.Focus();
        DocumentDateField.SelectAll();
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

    private void DocumentDate_KeyDown(object? sender, KeyEventArgs e)
    {
        if (ViewModel is not { } model) return;
        switch (e.Key)
        {
            case Key.Enter: model.CommitDocumentDate(); break;
            case Key.Escape: model.RestoreDocumentDateText(); break;
            default: return;
        }
        e.Handled = true;
        // The keyboard stays in the field with the date selected, so a second try is one more typed date.
        DocumentDateField.SelectAll();
    }

    /// <summary>Opens the calendar at the saved date, or at the suggested date or today when there is none.</summary>
    private void OpenDocumentDateCalendar_Click(object? sender, RoutedEventArgs e)
    {
        if (ViewModel is not { CanWork: true, SelectedDocument: { } document } model) return;
        var shown = document.DocumentDate ?? model.SuggestedDate ?? DateOnly.FromDateTime(DateTime.Today);
        _showingDocumentDate = true;
        try
        {
            DocumentDateCalendar.SelectedDate = document.DocumentDate?.ToDateTime(TimeOnly.MinValue);
            DocumentDateCalendar.DisplayDate = shown.ToDateTime(TimeOnly.MinValue);
        }
        finally
        {
            _showingDocumentDate = false;
        }
        DocumentDatePicker.IsOpen = true;
    }

    /// <summary>A day chosen in the open calendar saves at once, and the keyboard goes back to the field.</summary>
    private void DocumentDateChosen()
    {
        if (_showingDocumentDate || !DocumentDatePicker.IsOpen || DocumentDateCalendar.SelectedDate is not { } chosen) return;
        DocumentDatePicker.IsOpen = false;
        ViewModel?.PickDocumentDate(DateOnly.FromDateTime(chosen));
        DocumentDateField.Focus();
    }
}
