using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Plico.Core;

namespace Plico.Desktop;

internal static partial class NativeVerification
{
    private static async Task VerifyDocumentDateAsync(MainWindow window, InboxViewModel model, string directory)
    {
        var leapDay = new DateOnly(2024, 2, 29);
        var packet = model.Documents.Single(document => document.Title == "Paperwork packet");
        Control<ListBox>(window, "DocumentList").SetCurrentValue(ListBox.SelectedItemProperty, packet);
        var field = Control<TextBox>(window, "DocumentDateField");
        Require(field.IsEffectivelyVisible && field.IsEnabled && packet.DocumentDate is null && string.IsNullOrEmpty(field.Text),
            "The undated PDF did not offer an empty date field.");

        // The calendar opens under the field as a popover drawn in the window.
        Control<Button>(window, "DocumentDateCalendarButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        var picker = Control<Popup>(window, "DocumentDatePicker");
        await UntilAsync(() => picker.IsOpen && Control<Calendar>(window, "DocumentDateCalendar").IsEffectivelyVisible,
            () => "The date field's calendar did not open.");
        await CaptureAsync(window, directory, "document-date-picker.png");
        Control<Calendar>(window, "DocumentDateCalendar").RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Escape });
        Require(!picker.IsOpen && model.SelectedDocument?.DocumentDate is null, "Escape did not close the calendar without a date.");

        // A date typed as the interface writes it saves on Enter.
        TypeDocumentDate(window, "29 Feb 2024");
        Require(model.SelectedDocument is { } dated && dated.DocumentDate == leapDay && dated.AddedAt == packet.AddedAt &&
            model.UndoLabel == "Date set", model.Status);

        var sort = Control<ComboBox>(window, "DocumentSortBox");
        sort.SetCurrentValue(ComboBox.SelectedItemProperty, model.SortChoices.Single(choice => choice.Order == DocumentSort.DocumentDate));
        Control<TextBox>(window, "SearchBox").SetCurrentValue(TextBox.TextProperty, model.Query + " dated:2024-02");
        await UntilAsync(() => model.Documents.Count == 1 && model.Documents[0].Id == packet.Id && model.Preview is not null &&
            model.Documents[0].Match.Contains(ReceiptKeyword, StringComparison.OrdinalIgnoreCase), () => model.Status);
        Require(model.SortOption?.Order == DocumentSort.DocumentDate && model.Status == "Document date saved." && field.Text == "29 Feb 2024",
            $"The field did not keep the stored date. {model.Status} {field.Text}");
        await CaptureAsync(window, directory, "document-date.png");

        var clear = Control<Button>(window, "ClearDocumentDateButton");
        Require(clear.IsEffectivelyVisible, "The dated PDF did not offer to clear its date.");
        clear.Command!.Execute(clear.CommandParameter);
        Require(model.Documents.Count == 0 && model.Status == "Document date cleared.", model.Status);
    }

    /// <summary>Types a date into the open document's date field and presses Enter, as a person would.</summary>
    private static void TypeDocumentDate(MainWindow window, string text)
    {
        var field = Control<TextBox>(window, "DocumentDateField");
        Require(field.IsEffectivelyVisible && field.IsEnabled, "The open PDF did not offer its date field.");
        field.SetCurrentValue(TextBox.TextProperty, text);
        field.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Enter });
    }

    private static async Task VerifyBatchDocumentDatesAsync(Window window, InboxViewModel model, FixtureDialogs dialogs, string directory)
    {
        const string leapDayInput = "2024-02-29";
        const int selectedCount = 2;
        var leapDay = new DateOnly(2024, 2, 29);
        int loadedCount = model.Documents.Count;
        Require(loadedCount > InboxLibrary.DefaultResultPageSize, "The batch date check needs both loaded result pages.");
        var first = model.Documents[0];
        var last = model.Documents[^1];
        var selectedIds = new HashSet<long> { first.Id, last.Id };
        var list = Control<ListBox>(window, "DocumentList");
        list.SelectedItems = new List<DocumentSummary> { first, last };
        var edit = Control<Button>(window, "EditBatchDocumentDateButton");
        Require(edit.IsEffectivelyVisible && edit.IsEnabled && list.Selection.Count == selectedCount,
            "The PDFs on both pages did not offer a batch date edit.");
        dialogs.BatchDocumentDateChoice = (current, count) =>
        {
            Require(current is null && count == selectedCount, "The batch editor did not receive both selected, undated PDFs.");
            return ConfirmDocumentDateAsync(window, current, leapDayInput, directory, count);
        };
        try
        {
            edit.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await UntilAsync(() => model.Status == "Document date saved for 2 documents." && model.HasBatchSelection, () => model.Status);
            Require(model.Documents.Count == loadedCount && model.Documents.All(document =>
                    document.DocumentDate == (selectedIds.Contains(document.Id) ? leapDay : null)) &&
                    selectedIds.SetEquals(list.Selection.SelectedItems.OfType<DocumentSummary>().Select(document => document.Id)),
                "The batch date edit lost a loaded result or affected the wrong PDF.");
            await UntilAsync(() => model.Preview is not null, () => model.PreviewMessage);
            await CaptureAsync(window, directory, "batch-document-dates.png");

            string datedQuery = $"{model.Query} dated:{leapDayInput}";
            Control<TextBox>(window, "SearchBox").SetCurrentValue(TextBox.TextProperty, datedQuery);
            await UntilAsync(() => selectedIds.SetEquals(model.Documents.Select(document => document.Id)), () => model.Status);
            list.SelectedItems = model.Documents.ToList();
            dialogs.BatchDocumentDateChoice = (current, count) =>
            {
                Require(current == leapDay && count == selectedCount, "The shared date was missing when the editor reopened.");
                return ConfirmDocumentDateAsync(window, current, "", directory: null, documentCount: count);
            };
            edit.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await UntilAsync(() => model.Status == "Document dates cleared for 2 documents." && model.Documents.Count == 0, () => model.Status);
            Require(model.Query == datedQuery, "Clearing the dates changed the active search.");
        }
        finally
        {
            dialogs.BatchDocumentDateChoice = null;
        }
    }

    private static async Task<DocumentDateEdit?> ConfirmDocumentDateAsync(Window owner, DateOnly? current, string input, string? directory,
        int documentCount = 1)
    {
        var dialog = new DocumentDateDialog(current, documentCount);
        var result = dialog.ShowDialog<DocumentDateEdit?>(owner);
        try
        {
            bool clearBatch = documentCount > 1 && input.Length == 0;
            if (!clearBatch) Control<TextBox>(dialog, "DocumentDateBox").Text = input;
            if (directory is not null)
            {
                string filename = documentCount > 1 ? "batch-document-date-dialog.png" : "document-date-dialog.png";
                await CaptureAsync(dialog, directory, filename);
            }
            string action = clearBatch ? "ClearDocumentDatesButton" : "ConfirmDocumentDateButton";
            Control<Button>(dialog, action).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            return await result;
        }
        finally
        {
            if (dialog.IsVisible) dialog.Close();
            await result;
        }
    }
}
