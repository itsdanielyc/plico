using Avalonia.Controls;
using Avalonia.Interactivity;
using CommunityToolkit.Mvvm.Input;

namespace Plico.Desktop;

internal static partial class NativeVerification
{
    private static async Task VerifyPdfVersionFlowAsync(MainWindow window, InboxViewModel model, FixtureDialogs dialogs,
        string evidenceDirectory, string fixture)
    {
        const string mixedTitle = "Mixed Pages";
        string source = Path.Combine(Path.GetDirectoryName(fixture)!, mixedTitle + ".pdf");
        await model.ImportDroppedFilesAsync([source], hasSupportedFile: true);
        model.ShowAllCommand.Execute(null);
        var original = model.Documents.Single(document => document.Title == mixedTitle && document.PageCount == 2);
        Control<ListBox>(window, "DocumentList").SelectedItem = original;
        await UntilAsync(() => model.SelectedDocument?.Id == original.Id && model.Preview is not null, () => model.Status);
        string activePath = Path.Combine(model.LibraryPath, "PDFs", original.RelativePath);
        byte[] originalBytes = File.ReadAllBytes(activePath);
        var tools = Control<Button>(window, "PdfToolsButton").Flyout as MenuFlyout;
        Require(tools is not null, "PDF tools did not open a menu.");
        var edit = tools!.Items.OfType<MenuItem>().Single(item => item.Name == "EditPdfPagesItem");
        var history = tools.Items.OfType<MenuItem>().Single(item => item.Name == "PdfVersionsItem");
        tools.ShowAt(Control<Button>(window, "PdfToolsButton"));
        await DispatcherReadyForPdfMenuAsync();
        Require(edit.Command is IAsyncRelayCommand && history.Command is IAsyncRelayCommand, "Page editing commands were not bound.");
        tools.Hide();
        dialogs.PageEditChoice = async (document, currentPage) =>
        {
            var dialog = new PdfPageEditor(document.PageCount, currentPage);
            var completion = dialog.ShowDialog<PdfPageEditRequest?>(window);
            Control<ComboBox>(dialog, "ActionBox").SelectedIndex = 1;
            Control<TextBox>(dialog, "PagesBox").Text = "1";
            await CaptureAsync(dialog, evidenceDirectory, "pdf-delete-page.png");
            Control<Button>(dialog, "ApplyPageEdit").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            return await completion;
        };
        dialogs.VersionChoice = async (document, versions) =>
        {
            Require(versions.Count == 1 && versions[0].PageCount == 2, "The previous two-page PDF did not appear in history.");
            var dialog = new PdfVersionHistoryDialog(document, versions);
            var completion = dialog.ShowDialog<PdfVersionDecision?>(window);
            await CaptureAsync(dialog, evidenceDirectory, "pdf-version-history.png");
            Control<Button>(dialog, "RestoreVersion").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            return await completion;
        };
        try
        {
            await model.EditPdfPagesCommand.ExecuteAsync(null);
            Require(model.SelectedDocument?.Id == original.Id && model.SelectedDocument.PageCount == 1 && model.LibraryPath.Length > 0,
                $"The one-page revision did not keep its document ID. {model.Status}");
            await model.ShowPdfVersionsCommand.ExecuteAsync(null);
            Require(model.SelectedDocument?.Id == original.Id && model.SelectedDocument.PageCount == 2 &&
                originalBytes.AsSpan().SequenceEqual(File.ReadAllBytes(activePath)),
                $"Restoration did not make the original PDF current. {model.Status}");
            await UntilAsync(() => model.Preview is not null && model.PageLabel == "1 / 2", () => model.Status);
            await CaptureAsync(window, evidenceDirectory, "pdf-version-restored.png");
        }
        finally
        {
            dialogs.PageEditChoice = null;
            dialogs.VersionChoice = null;
        }
    }

    private static async Task DispatcherReadyForPdfMenuAsync() =>
        await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(static () => { }, Avalonia.Threading.DispatcherPriority.Background);
}
