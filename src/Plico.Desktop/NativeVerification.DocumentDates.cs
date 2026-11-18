using Avalonia.Controls;
using Avalonia.Interactivity;
using CommunityToolkit.Mvvm.Input;
using Plico.Core;

namespace Plico.Desktop;

internal static partial class NativeVerification
{
    private static async Task VerifyDocumentDateAsync(MainWindow window, InboxViewModel model, FixtureDialogs dialogs, string directory)
    {
        const string leapDayInput = "2024-02-29";
        var leapDay = new DateOnly(2024, 2, 29);
        var packet = model.Documents.Single(document => document.Title == "Paperwork packet");
        Control<ListBox>(window, "DocumentList").SetCurrentValue(ListBox.SelectedItemProperty, packet);
        var edit = Control<Button>(window, "EditDocumentDateButton");
        Require(edit.IsEffectivelyVisible && edit.IsEnabled && packet.DocumentDate is null,
            "The undated PDF did not offer a document date edit.");
        var command = (IAsyncRelayCommand)edit.Command!;
        dialogs.DocumentDateChoice = current =>
        {
            Require(current is null, "The date editor used the intake date as the document date.");
            return ConfirmDocumentDateAsync(window, current, leapDayInput, directory);
        };
        try
        {
            await command.ExecuteAsync(edit.CommandParameter);
            Require(model.SelectedDocument is { } dated && dated.DocumentDate == leapDay && dated.AddedAt == packet.AddedAt, model.Status);

            var sort = Control<ComboBox>(window, "DocumentSortBox");
            sort.SetCurrentValue(ComboBox.SelectedItemProperty, model.SortChoices.Single(choice => choice.Order == DocumentSort.DocumentDate));
            Control<TextBox>(window, "SearchBox").SetCurrentValue(TextBox.TextProperty, model.Query + " dated:2024-02");
            await UntilAsync(() => model.Documents.Count == 1 && model.Documents[0].Id == packet.Id && model.Preview is not null &&
                model.Documents[0].Match.Contains(ReceiptKeyword, StringComparison.OrdinalIgnoreCase), () => model.Status);
            Require(model.SortOption?.Order == DocumentSort.DocumentDate && model.Status == "Document date saved.", model.Status);
            await CaptureAsync(window, directory, "document-date.png");

            dialogs.DocumentDateChoice = current =>
            {
                Require(current == leapDay, "The date editor did not reopen with the stored date.");
                return ConfirmDocumentDateAsync(window, current, "", directory: null);
            };
            await command.ExecuteAsync(edit.CommandParameter);
            Require(model.Documents.Count == 0 && model.Status == "Document date cleared.", model.Status);
        }
        finally
        {
            dialogs.DocumentDateChoice = null;
        }
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
