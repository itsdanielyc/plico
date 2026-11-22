using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using CommunityToolkit.Mvvm.Input;
using Plico.Core;

namespace Plico.Desktop;

internal static partial class NativeVerification
{
    private static async Task VerifyTrashAsync(Window window, InboxViewModel model, FixtureDialogs dialogs, string evidenceDirectory)
    {
        model.ShowAllCommand.Execute(null);
        var list = Control<ListBox>(window, "DocumentList");
        var document = model.Documents[0];
        var next = model.Documents[1];
        list.SetCurrentValue(ListBox.SelectedItemProperty, document);
        list.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.X, KeySymbol = "x" });
        list.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyUpEvent, Key = Key.X, KeySymbol = "x" });
        await UntilAsync(() => !model.IsBusy && model.SelectedDocument?.Id == next.Id, () => model.Status);
        Require(model.Documents.All(result => result.Id != document.Id), "Trash left the current PDF in the review results.");
        Require(model.ToastTitle == "Moved to Trash" && model.ToastBody == document.Title && model.ToastOffersUndo,
            $"The Trash toast did not name the action with Undo. {model.ToastTitle} {model.ToastBody}");
        await UntilAsync(() => model.Preview is not null && !model.IsReadingInBackground, () => model.Status);
        await CaptureAsync(window, evidenceDirectory, "trash-undo.png");

        dialogs.TrashChoice = async documents =>
        {
            var dialog = new TrashDialog(documents);
            var completion = dialog.ShowDialog<TrashDecision?>(window);
            try
            {
                var file = documents.Single(item => item.DocumentId == document.Id);
                Require(file.OriginalPath == document.RelativePath, "Trash lost the PDF’s original path.");
                Control<ListBox>(dialog, "TrashedFiles").SelectedItem = file;
                await CaptureAsync(dialog, evidenceDirectory, "trash.png");
                Control<Button>(dialog, "RestoreTrashButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                return await completion;
            }
            finally
            {
                if (dialog.IsVisible) dialog.Close();
                await completion;
            }
        };
        try
        {
            var openTrash = (IAsyncRelayCommand)Control<Button>(window, "OpenTrashButton").Command!;
            await openTrash.ExecuteAsync(null);
            Require(model.SelectedDocument?.Id == document.Id && model.SelectedDocument.RelativePath == document.RelativePath,
                "Restoring from Trash did not open the original PDF.");
        }
        finally
        {
            dialogs.TrashChoice = null;
        }
    }
}
