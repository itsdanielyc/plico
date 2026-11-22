using CultureInfo = System.Globalization.CultureInfo;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;

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
        // Leaving a field saves it. The keyboard leaves as a click begins, so the results reload after that click. Leaving
        // for the toast drops the edit instead: saving it first would make it the offer that the toast's Undo then reverses.
        TitleEditBox.LostFocus += (_, e) =>
        {
            if (MovesIntoToast(e)) ViewModel?.CancelTitleEdit();
            else ViewModel?.CommitTitleEdit(reloadAfterInput: true);
        };
        DocumentDateField.AddHandler(KeyDownEvent, DocumentDate_KeyDown, RoutingStrategies.Bubble, handledEventsToo: true);
        // The suggestion list takes Enter for itself; the tag is added after it has filled in its choice.
        TagBox.AddHandler(KeyDownEvent, TagBox_KeyDown, RoutingStrategies.Bubble, handledEventsToo: true);
        DocumentDateField.LostFocus += (_, e) =>
        {
            // A day in the open calendar takes the keyboard while it is chosen, and saves itself. Anywhere else, the
            // calendar closes with the field and what was typed is saved, as when it is closed.
            if (!ReferenceEquals(e.Source, DocumentDateField) || MovesIntoCalendar(e)) return;
            DocumentDatePicker.IsOpen = false;
            if (MovesIntoToast(e)) ViewModel?.RestoreDocumentDateText();
            else ViewModel?.CommitDocumentDate(reloadAfterInput: true);
        };
        DocumentDatePicker.PlacementTarget = DocumentDateField;
        DocumentDatePicker.Closed += (_, _) => RestoreCultureAfterCalendar();
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
        // Seen on the way down, before the focused field's own undo can take the chord.
        AddHandler(KeyDownEvent, SettledFieldUndo_KeyDown, RoutingStrategies.Tunnel);
    }

    /// <summary>
    /// Ctrl+Z (⌘Z) in a field holding nothing new is Plico's Undo, not the field's. Enter saves a date or adds a tag and
    /// leaves the keyboard in the field, so the toast's Undo is what the chord reaches next; the field's own undo would
    /// bring back half of what was typed instead, unsaved. While something new is typed, the chord edits the text.
    /// </summary>
    private void SettledFieldUndo_KeyDown(object? sender, KeyEventArgs e)
    {
        bool command = e.KeyModifiers is KeyModifiers.Control or KeyModifiers.Meta;
        if (e.Key != Key.Z || !command || IsOverlayOpen || ViewModel is not { } model || !InSettledField(model)) return;
        Run(model.UndoCommand);
        e.Handled = true;
    }

    /// <summary>True when the keyboard is going to one of the toast's buttons, such as Undo, by a click or by Tab.</summary>
    private bool MovesIntoToast(FocusChangedEventArgs e) => e.NewFocusedElement is Visual next && Toast.IsVisualAncestorOf(next);

    /// <summary>True when the keyboard is going into the date's open calendar, to a day being chosen.</summary>
    private bool MovesIntoCalendar(FocusChangedEventArgs e) =>
        DocumentDatePicker is { IsOpen: true, Child: Visual popover } && e.NewFocusedElement is Visual next &&
        (ReferenceEquals(next, popover) || popover.IsVisualAncestorOf(next));

    private bool InSettledField(InboxViewModel model) => FocusManager?.GetFocusedElement() switch
    {
        TextBox field when ReferenceEquals(field, DocumentDateField) => model.DocumentDateTextIsSaved,
        Visual visual when ReferenceEquals(visual, TagBox) || ReferenceEquals(visual.FindAncestorOfType<AutoCompleteBox>(), TagBox) =>
            string.IsNullOrWhiteSpace(TagBox.Text),
        _ => false
    };

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
        if (ViewModel is not { HasLibrary: true, ShowFilingCard: true }) return;
        // A field without the keyboard shows its date as a value, never as selected text.
        if (DocumentDateField.Focus()) DocumentDateField.SelectAll();
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
            // Escape closes the open calendar first, the topmost layer; with it closed, Escape puts the saved date back.
            case Key.Escape when DocumentDatePicker.IsOpen: DocumentDatePicker.IsOpen = false; break;
            case Key.Escape: model.RestoreDocumentDateText(); break;
            case Key.Enter:
                DocumentDatePicker.IsOpen = false;
                // The suggested date sitting in the empty field is filed by Enter, as by a click; anything typed is saved.
                if (model.ShowSuggestedDateInField) Run(model.AcceptSuggestedDateCommand);
                else model.CommitDocumentDate();
                break;
            default: return;
        }
        e.Handled = true;
        // The keyboard stays in the field with the date selected, so a second try is one more typed date. A date entered
        // without the keyboard there (by an automation client) is left as a value.
        if (DocumentDateField.IsFocused) DocumentDateField.SelectAll();
        else DocumentDateField.ClearSelection();
    }

    /// <summary>
    /// Files the suggested date from its chip in the field. The keyboard stays in the field with that date selected, as
    /// after Enter, so a different date is one more typed over it.
    /// </summary>
    private void AcceptSuggestedDate_Click(object? sender, RoutedEventArgs e)
    {
        if (ViewModel is not { } model) return;
        Run(model.AcceptSuggestedDateCommand);
        FocusDocumentDate();
    }

    /// <summary>
    /// Opens the calendar at the saved date, or at the suggested date or today when there is none. The keyboard stays in
    /// the field with its date selected, so a date can be typed instead of chosen, and Escape closes the calendar.
    /// </summary>
    private void OpenDocumentDateCalendar_Click(object? sender, RoutedEventArgs e)
    {
        if (ViewModel is not { HasLibrary: true, SelectedDocument: { } document } model) return;
        var shown = document.DocumentDate ?? model.SuggestedDate ?? DateOnly.FromDateTime(DateTime.Today);
        UseCultureForCalendar();
        _showingDocumentDate = true;
        try
        {
            DocumentDateCalendar.SelectedDate = document.DocumentDate?.ToDateTime(TimeOnly.MinValue);
            // Moving the calendar away and back, and setting its first day again, makes it name the month and the days
            // again in the culture it now reads.
            DocumentDateCalendar.DisplayDate = shown.AddMonths(1).ToDateTime(TimeOnly.MinValue);
            DocumentDateCalendar.DisplayDate = shown.ToDateTime(TimeOnly.MinValue);
            DocumentDateCalendar.FirstDayOfWeek = DayOfWeek.Sunday;
            DocumentDateCalendar.FirstDayOfWeek = DayOfWeek.Monday;
        }
        finally
        {
            _showingDocumentDate = false;
        }
        FocusDocumentDate();
        DocumentDatePicker.IsOpen = true;
    }

    // The culture the window had before the calendar opened, while the calendar is open.
    private CultureInfo? _cultureBeforeCalendar;

    /// <summary>
    /// Avalonia's calendar names its months and days in the thread's culture. While it is open that culture is the
    /// interface's own (French in the French interface, whatever the system speaks), and it goes back when the calendar
    /// closes. The week starts on Monday in both languages, as the interface writes dates day first ("22 Nov 2026").
    /// </summary>
    private void UseCultureForCalendar()
    {
        _cultureBeforeCalendar ??= CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = DisplayDates.Culture;
    }

    private void RestoreCultureAfterCalendar()
    {
        if (_cultureBeforeCalendar is not { } before) return;
        CultureInfo.CurrentCulture = before;
        _cultureBeforeCalendar = null;
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
