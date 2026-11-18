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

    private static async Task<DocumentDateEdit?> ConfirmDocumentDateAsync(Window owner, DateOnly? current, string input, string? directory)
    {
        var dialog = new DocumentDateDialog(current);
        var result = dialog.ShowDialog<DocumentDateEdit?>(owner);
        try
        {
            Control<TextBox>(dialog, "DocumentDateBox").Text = input;
            if (directory is not null) await CaptureAsync(dialog, directory, "document-date-dialog.png");
            Control<Button>(dialog, "ConfirmDocumentDateButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            return await result;
        }
        finally
        {
            if (dialog.IsVisible) dialog.Close();
            await result;
        }
    }
}
