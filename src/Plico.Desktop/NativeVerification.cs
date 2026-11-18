using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.Input;
using Plico.Core;

namespace Plico.Desktop;

internal static class NativeVerification
{
    internal const string EditedDocumentTitle = "October housing";
    private const string ReceiptKeyword = "WRENCH470";
    private const string TestTag = "Workshop";
    private const string LetterKeyword = "NORTHSTAR";
    private static readonly TimeSpan StateTimeout = TimeSpan.FromSeconds(6);
    private static readonly TimeSpan StateCheckInterval = TimeSpan.FromMilliseconds(30);

    internal static async Task RunAsync(
        IClassicDesktopStyleApplicationLifetime desktop, MainWindow window, InboxViewModel model,
        FixtureDialogs dialogs, string evidenceDirectory, string fixture)
    {
        try
        {
            Directory.CreateDirectory(evidenceDirectory);
            Require(window.IsVisible && Control<TextBlock>(window, "InboxHeading").Text == "Inbox", "The native inbox did not open.");
            var import = Control<Button>(window, "AddFilesButton");
            Require(import.IsEnabled && import.Command is IAsyncRelayCommand, "The import control was not available.");
            await ((IAsyncRelayCommand)import.Command!).ExecuteAsync(null);
            Require(model.Documents.Count == 1 && model.SelectedDocument is { HasText: true },
                $"The receipt did not import with embedded text. {model.Status}");
            await UntilAsync(() => model.Preview is not null, () => model.PreviewMessage);

            Control<TextBox>(window, "TagBox").SetCurrentValue(TextBox.TextProperty, TestTag);
            Control<Button>(window, "AddTagButton").Command!.Execute(null);
            Require(model.SelectedDocument?.Tags.Contains(TestTag) == true, "The tag did not attach to the imported PDF.");
            await CaptureAsync(window, evidenceDirectory, "inbox.png");
            Control<Button>(window, "ReviewButton").Command!.Execute(null);
            Require(model.Documents.Count == 0, "The reviewed PDF remained in the inbox.");

            Control<TextBox>(window, "SearchBox").SetCurrentValue(TextBox.TextProperty, ReceiptKeyword);
            await UntilAsync(() => model.Documents.Count == 1 && model.Preview is not null, () => model.Status);
            Require(model.SelectedDocument is { Reviewed: true } document && document.Tags.Contains(TestTag) &&
                    document.Match.Contains(ReceiptKeyword, StringComparison.OrdinalIgnoreCase),
                "The reviewed and tagged PDF was not found by OCR text.");
            await CaptureAsync(window, evidenceDirectory, "search.png");
            await VerifyDropAsync(window, model, fixture, evidenceDirectory);
            await VerifyWatchedFolderAsync(window, model, fixture, evidenceDirectory);
            await VerifyTitleEditorAsync(window, model, evidenceDirectory);
            VerifyDocumentLocation(window, dialogs, Path.Combine(model.LibraryPath, "PDFs"));
            await VerifyMovedPdfAsync(window, model, dialogs, evidenceDirectory);
            await VerifyPhraseSearchAsync(window, model, evidenceDirectory);
            await VerifyBatchReviewAsync(window, model, evidenceDirectory);
            await VerifyScannerDialogAsync(window, evidenceDirectory);

            File.WriteAllText(Path.Combine(evidenceDirectory, "passed.txt"),
                "Native window completed OCR, phrase search, duplicate drop, folder watch, title edit, PDF relocation, batch tagging and review, and scanner settings display.");
            desktop.Shutdown(0);
        }
        catch (Exception error)
        {
            File.WriteAllText(Path.Combine(evidenceDirectory, "error.txt"), error.ToString());
            desktop.Shutdown(1);
        }
    }

    private static async Task VerifyPhraseSearchAsync(MainWindow window, InboxViewModel model, string directory)
    {
        var document = model.SelectedDocument ?? throw new InvalidOperationException("Select a PDF before testing phrase search.");
        var search = Control<TextBox>(window, "SearchBox");
        search.SetCurrentValue(TextBox.TextProperty, "\"northstar lease\"");
        await UntilAsync(() => model.Documents.Count == 0, () => model.Status);
        search.SetCurrentValue(TextBox.TextProperty, "\"new term\" norths");
        await UntilAsync(() => model.SelectedDocument?.Id == document.Id && model.Preview is not null, () => model.Status);
        await CaptureAsync(window, directory, "phrase-search.png");
    }

    private static async Task VerifyBatchReviewAsync(MainWindow window, InboxViewModel model, string directory)
    {
        model.ShowAllCommand.Execute(null);
        var select = Control<Button>(window, "SelectShownButton");
        var review = Control<Button>(window, "BatchReviewButton");
        Require(select.IsVisible && model.Documents.Count == 2, "The list did not offer batch selection.");
        select.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Require(model.HasBatchSelection && review.Command is IAsyncRelayCommand && model.BatchCountLabel == "2 selected",
            "The window did not select both documents.");
        await UntilAsync(() => model.Preview is not null, () => model.PreviewMessage);
        const string batchTag = "Verification";
        Control<TextBox>(window, "BatchTagBox").SetCurrentValue(TextBox.TextProperty, batchTag);
        Control<Button>(window, "ApplyBatchTagButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        await UntilAsync(() => model.HasBatchSelection && model.Status == "Tag applied to 2 documents." && model.Preview is not null,
            () => model.Status);
        Require(model.Documents.All(document => document.Tags.Contains(batchTag)),
            "The tag did not attach to both selected PDFs.");
        await CaptureAsync(window, directory, "batch-review.png");
        await ((IAsyncRelayCommand)review.Command!).ExecuteAsync(null);
        model.ShowInboxCommand.Execute(null);
        Require(model.Documents.Count == 0 && model.Status == "2 documents marked reviewed.",
            "Batch review did not remove both selected documents from the inbox.");
    }

    private static void VerifyDocumentLocation(MainWindow window, FixtureDialogs dialogs, string expectedFolder)
    {
        var location = Control<Button>(window, "PdfLocationButton");
        Require(location.IsVisible && location.IsEnabled && location.Command is not null, "The document path did not offer its folder.");
        location.Command!.Execute(null);
        Require(dialogs.LastOpenedPath == expectedFolder && Directory.Exists(dialogs.LastOpenedPath),
            "The document path did not open the PDF's actual containing folder.");
    }

    private static async Task VerifyMovedPdfAsync(MainWindow window, InboxViewModel model, FixtureDialogs dialogs, string directory)
    {
        var selected = model.SelectedDocument ?? throw new InvalidOperationException("Select the watched PDF before moving it.");
        var pdfFolder = Path.Combine(model.LibraryPath, "PDFs");
        var filingFolder = Directory.CreateDirectory(Path.Combine(pdfFolder, "Filed")).FullName;
        var movedPdf = Path.Combine(filingFolder, "Housing renewal.pdf");
        File.Move(Path.Combine(pdfFolder, selected.RelativePath), movedPdf);
        await model.RefreshIndexCommand.ExecuteAsync(null);
        Require(model.Status.Contains("1 document moved.", StringComparison.Ordinal), model.Status);

        var search = Control<TextBox>(window, "SearchBox");
        search.SetCurrentValue(TextBox.TextProperty, EditedDocumentTitle);
        await UntilAsync(() => model.SelectedDocument?.Id == selected.Id && model.Preview is not null, () => model.Status);
        VerifyDocumentLocation(window, dialogs, filingFolder);
        await CaptureAsync(window, directory, "moved-pdf.png");
    }

    private static async Task VerifyTitleEditorAsync(MainWindow window, InboxViewModel model, string directory)
    {
        var selected = model.SelectedDocument ?? throw new InvalidOperationException("Select a document before testing title edits.");
        var dialog = new DocumentTitleDialog(selected.Title);
        var result = dialog.ShowDialog<string?>(window);
        try
        {
            Require(Control<TextBox>(dialog, "TitleBox").Text == selected.Title,
                "The title editor did not show the selected document's title.");
            await CaptureAsync(dialog, directory, "edit-title.png");
        }
        finally
        {
            dialog.Close();
            await result;
        }

        var edit = Control<Button>(window, "EditTitleButton");
        Require(edit.IsEnabled && edit.Command is IAsyncRelayCommand, "The title edit control was unavailable.");
        await ((IAsyncRelayCommand)edit.Command!).ExecuteAsync(null);
        Require(model.SelectedDocument?.Title == EditedDocumentTitle, "The updated title did not appear in the results.");
        var search = Control<TextBox>(window, "SearchBox");
        search.SetCurrentValue(TextBox.TextProperty, "fixture-not-found");
        await UntilAsync(() => model.Documents.Count == 0, () => model.Status);
        search.SetCurrentValue(TextBox.TextProperty, "Scanner output");
        await UntilAsync(() => model.SelectedDocument?.Title == EditedDocumentTitle && model.Preview is not null, () => model.PreviewMessage);
        await CaptureAsync(window, directory, "edited-title.png");
    }

    private static async Task VerifyScannerDialogAsync(Window owner, string directory)
    {
        await CaptureScanDialogAsync(owner, directory, "scanner-address.png");
        var settingsDirectory = Directory.CreateTempSubdirectory("plico-verify-scanners-");
        try
        {
            var settings = new SavedScannerStore(settingsDirectory.FullName);
            settings.AddOrUpdate(Scanners.FromAddress("scanner.test:8080", "Office scanner"));
            await CaptureScanDialogAsync(owner, directory, "saved-scanner.png", settings);
        }
        finally
        {
            settingsDirectory.Delete(recursive: true);
        }
    }

    private static async Task CaptureScanDialogAsync(Window owner, string directory, string filename, SavedScannerStore? settings = null)
    {
        var dialog = new ScanDialog(_ => Task.FromResult(new ScanDiscovery([], [])), settings: settings);
        var result = dialog.ShowDialog<ScanRequest?>(owner);
        try
        {
            var address = Control<TextBox>(dialog, "AddressBox");
            var connect = Control<Button>(dialog, "ConnectButton");
            bool hasSavedScanner = settings is not null;
            Require(address.IsVisible && connect.IsEnabled && Control<Button>(dialog, "StartButton").IsEnabled == hasSavedScanner &&
                    Control<Button>(dialog, "ForgetButton").IsVisible == hasSavedScanner,
                "The native scanner dialog did not offer the expected scanner actions.");
            await CaptureAsync(dialog, directory, filename);
        }
        finally
        {
            dialog.Close();
            await result;
        }
    }

    private static async Task VerifyWatchedFolderAsync(MainWindow window, InboxViewModel model, string fixture, string directory)
    {
        var start = Control<Button>(window, "StartFolderWatchButton");
        var stop = Control<Button>(window, "StopFolderWatchButton");
        Require(start.IsEnabled && start.Command is IAsyncRelayCommand, "The folder watch control was not available.");
        await ((IAsyncRelayCommand)start.Command!).ExecuteAsync(null);
        Require(model.IsWatchingFolder && stop.IsVisible, $"The folder watch did not start. {model.Status}");

        string letter = Path.Combine(Path.GetDirectoryName(fixture)!, "Lease Renewal.pdf");
        string incoming = Path.Combine(model.WatchedFolderPath, "Scanner output.pdf");
        File.Copy(letter, incoming);
        await UntilAsync(() => !model.IsBusy && model.Status == "1 document added.", () => model.Status);
        Require(model.Query == ReceiptKeyword && model.SelectedDocument?.Reviewed == true && File.Exists(incoming),
            "A watched import changed the current view or consumed the source file.");

        Control<TextBox>(window, "SearchBox").SetCurrentValue(TextBox.TextProperty, LetterKeyword);
        await UntilAsync(() => model.SelectedDocument?.Title == "Scanner output" && model.Preview is not null, () => model.Status);
        await CaptureAsync(window, directory, "watched-folder.png");
        await ((IAsyncRelayCommand)stop.Command!).ExecuteAsync(null);
        Require(!model.IsWatchingFolder, "The folder watch did not stop.");
    }

    private static async Task VerifyDropAsync(MainWindow window, InboxViewModel model, string fixture, string directory)
    {
        using var file = await window.StorageProvider.TryGetFileFromPathAsync(new Uri(Path.GetFullPath(fixture)));
        if (file is null) throw new InvalidOperationException("The native file provider could not open the drop fixture.");
        using var transfer = new DataTransfer();
        transfer.Add(DataTransferItem.CreateFile(file));
        var pointer = new Point(window.Bounds.Width / 2, window.Bounds.Height / 2);
        var over = new DragEventArgs(DragDrop.DragOverEvent, transfer, window, pointer, KeyModifiers.None)
        {
            DragEffects = DragDropEffects.Copy
        };
        window.RaiseEvent(over);
        Require(over.DragEffects == DragDropEffects.Copy && Control<Border>(window, "DropTarget").IsVisible,
            "The native drop target did not offer to copy the file.");
        await CaptureAsync(window, directory, "drop-target.png");
        var drop = new DragEventArgs(DragDrop.DropEvent, transfer, window, pointer, KeyModifiers.None)
        {
            DragEffects = DragDropEffects.Copy
        };
        window.RaiseEvent(drop);
        await UntilAsync(() => !model.IsBusy && model.Status.Contains("already in the library", StringComparison.Ordinal),
            () => model.Status);
        Require(model.SelectedDocument is { Reviewed: true } document && document.Tags.Contains(TestTag) &&
                !Control<Border>(window, "DropTarget").IsVisible && File.Exists(fixture),
            "Dropping a repeat altered the existing document or moved the source.");
    }

    private static T Control<T>(Window window, string name) where T : Control =>
        window.FindControl<T>(name) ?? throw new InvalidOperationException($"Missing control {name}.");

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private static async Task UntilAsync(Func<bool> ready, Func<string> details)
    {
        using var timeout = new CancellationTokenSource(StateTimeout);
        try
        {
            while (!ready()) await Task.Delay(StateCheckInterval, timeout.Token);
        }
        catch (OperationCanceledException)
        {
            throw new InvalidOperationException($"The native window did not reach the expected state. {details()}");
        }
    }

    private static async Task CaptureAsync(Window window, string directory, string filename)
    {
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            using var bitmap = new RenderTargetBitmap(PixelSize.FromSize(window.Bounds.Size, 1), new Vector(96, 96));
            bitmap.Render(window);
            bitmap.Save(Path.Combine(directory, filename), PngBitmapEncoderOptions.Default);
        }, DispatcherPriority.Background);
    }
}

/// <summary>A labelled fixture picker for verification with the same real library used by normal launches.</summary>
internal sealed class FixtureDialogs(params string[] documents) : IWorkspaceDialogs
{
    internal string? FolderPath { get; init; }
    internal string? EditedTitle { get; init; }
    internal string? LastOpenedPath { get; private set; }
    public Task<IReadOnlyList<string>> PickDocumentsAsync() => Task.FromResult<IReadOnlyList<string>>(documents);
    public Task<string?> PickFolderAsync(string title) => Task.FromResult(FolderPath);
    public Task<ScanRequest?> PickScannerAsync() => Task.FromResult<ScanRequest?>(null);
    public Task<string?> EditTitleAsync(string currentTitle) => Task.FromResult(EditedTitle);
    public void OpenPath(string path) => LastOpenedPath = path;
}
