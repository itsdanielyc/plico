using System.Runtime.InteropServices;
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
    private const string ExpectedRuntimeVariable = "PLICO_EXPECTED_NATIVE_RID";
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
            string runtime = RuntimeInformation.RuntimeIdentifier;
            string? expectedRuntime = Environment.GetEnvironmentVariable(ExpectedRuntimeVariable);
            Require(string.IsNullOrEmpty(expectedRuntime) || runtime == expectedRuntime,
                $"Expected runtime {expectedRuntime}. Running on {runtime}.");
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
            await VerifySortAsync(window, model, evidenceDirectory);
            await VerifyBatchReviewAsync(window, model, evidenceDirectory);
            await VerifyOcrLanguageAsync(window, model, dialogs, fixture, evidenceDirectory);
            await VerifyCopiedTextAsync(window, model, dialogs);
            await VerifyLibraryBackupAsync(window, model, dialogs, evidenceDirectory);
            await VerifyImportErrorsAsync(window, model, dialogs, fixture, evidenceDirectory);
            await VerifyPreviewFitAsync(window, model, evidenceDirectory);
            await VerifyGoToMatchAsync(window, model, evidenceDirectory);
            await VerifySearchableCopyAsync(window, model, fixture, evidenceDirectory);
            await VerifyScannerDialogAsync(window, evidenceDirectory);

            File.WriteAllText(Path.Combine(evidenceDirectory, "passed.txt"),
                $"Verified {runtime}. Native window completed multilingual OCR, phrase search, result sorting, fit-width preview, match navigation, duplicate drop, folder watch, title edit, relocation, batch actions, library backup, text copy, searchable PDF copy, and import retry.");
            desktop.Shutdown(0);
        }
        catch (Exception error)
        {
            File.WriteAllText(Path.Combine(evidenceDirectory, "error.txt"), error.ToString());
            desktop.Shutdown(1);
        }
    }

    private static async Task VerifyPreviewFitAsync(MainWindow window, InboxViewModel model, string directory)
    {
        var fit = Control<Button>(window, "PreviewFitButton");
        var scroll = Control<ScrollViewer>(window, "PreviewScroll");
        var firstPage = model.Preview;
        Require(firstPage is not null && model.CanGoForward && fit.IsEnabled,
            "Fit width requires the first page of a multipage preview.");
        Require(scroll.Extent.Height <= scroll.Viewport.Height, "The initial preview did not fit the whole page.");

        fit.Command!.Execute(null);
        await UntilAsync(() => scroll.Extent.Height > scroll.Viewport.Height,
            () => $"The fit-width image did not extend vertically. Extent {scroll.Extent}, viewport {scroll.Viewport}.");
        Require(ReferenceEquals(model.Preview, firstPage) && scroll.Extent.Width <= scroll.Viewport.Width,
            "Fit width replaced the existing render or caused sideways scrolling.");
        await CaptureAsync(window, directory, "preview-fit-width.png");

        scroll.Offset = new Vector(0, scroll.Extent.Height - scroll.Viewport.Height);
        Require(scroll.Offset.Y > 0, "The zoomed page did not scroll.");
        model.NextPageCommand.Execute(null);
        await UntilAsync(() => model.Preview is not null, () => model.PreviewMessage);
        Require(scroll.Offset.Y == 0 && model.FitPreviewWidth, "The next page did not start at the top in fit-width mode.");

        fit.Command.Execute(null);
        await UntilAsync(() => scroll.Extent.Height <= scroll.Viewport.Height, () => "The preview did not return to fit-page mode.");
    }

    private static async Task VerifyCopiedTextAsync(MainWindow window, InboxViewModel model, FixtureDialogs dialogs)
    {
        var copy = Control<Button>(window, "CopyDocumentTextButton");
        Require(copy.IsVisible && copy.IsEnabled && copy.Command is IAsyncRelayCommand,
            "The selected OCR PDF did not offer its text for copying.");
        await ((IAsyncRelayCommand)copy.Command!).ExecuteAsync(copy.CommandParameter);
        Require(model.Status == "Document text copied." &&
            dialogs.LastCopiedText?.Contains("Règlement", StringComparison.OrdinalIgnoreCase) == true,
            "The copied text did not contain the selected PDF's accented text.");
    }

    private static async Task VerifyGoToMatchAsync(MainWindow window, InboxViewModel model, string directory)
    {
        var document = model.SelectedDocument ?? throw new InvalidOperationException("The mixed PDF was not selected.");
        model.PreviousPageCommand.Execute(null);
        Require(model.PageLabel == "1 / 2", "Search match navigation needs the first page of the mixed PDF.");
        Control<TextBox>(window, "SearchBox").SetCurrentValue(TextBox.TextProperty, ReceiptKeyword);
        var jump = Control<Button>(window, "GoToMatchButton");
        await UntilAsync(() => jump.IsVisible && jump.IsEnabled, () => model.Status);
        Require(model.SelectedDocument?.Id == document.Id && jump.Command is IAsyncRelayCommand,
            "The mixed PDF’s search result did not offer a matching page.");

        await ((IAsyncRelayCommand)jump.Command!).ExecuteAsync(jump.CommandParameter);
        await UntilAsync(() => model.PageLabel == "2 / 2" && model.Preview is not null, () => model.Status);
        await CaptureAsync(window, directory, "matching-page.png");
    }

    private static async Task VerifySearchableCopyAsync(MainWindow window, InboxViewModel model, string fixture, string directory)
    {
        const string scanName = "Pending OCR.pdf";
        string scanPath = Path.Combine(model.LibraryPath, "PDFs", scanName);
        string scannedFixture = Path.Combine(Path.GetDirectoryName(fixture)!, "Scanned Receipt.pdf");
        byte[] sourceBytes = File.ReadAllBytes(scannedFixture);
        File.WriteAllBytes(scanPath, sourceBytes);
        model.ShowInboxCommand.Execute(null);
        await model.RefreshIndexCommand.ExecuteAsync(null);
        var original = model.Documents.Single(document => document.RelativePath == scanName);
        Control<ListBox>(window, "DocumentList").SetCurrentValue(ListBox.SelectedItemProperty, original);
        await UntilAsync(() => model.SelectedDocument?.Id == original.Id && model.Preview is not null, () => model.Status);
        Control<TextBox>(window, "TagBox").SetCurrentValue(TextBox.TextProperty, TestTag);
        Control<Button>(window, "AddTagButton").Command!.Execute(null);
        var copy = Control<Button>(window, "CreateSearchableCopyButton");
        Require(!original.HasText && copy.IsEffectivelyVisible && copy.IsEnabled && copy.Command is IAsyncRelayCommand,
            "The indexed scan did not offer an OCR copy.");
        await CaptureAsync(window, directory, "indexed-scan.png");

        await ((IAsyncRelayCommand)copy.Command!).ExecuteAsync(copy.CommandParameter);
        var searchable = model.SelectedDocument;
        Require(searchable is { HasText: true, Reviewed: false } && searchable.Id != original.Id && searchable.Tags.Contains(TestTag),
            $"The searchable PDF did not open with the source tags. {model.Status}");
        Require(sourceBytes.AsSpan().SequenceEqual(File.ReadAllBytes(scanPath)), "OCR copied over the source scan.");

        Control<TextBox>(window, "SearchBox").SetCurrentValue(TextBox.TextProperty, ReceiptKeyword);
        await UntilAsync(() => model.SelectedDocument is { } current && current.Id == searchable!.Id &&
            model.Preview is not null && current.Match.Contains(ReceiptKeyword, StringComparison.OrdinalIgnoreCase), () => model.Status);
        await CaptureAsync(window, directory, "searchable-copy.png");
    }

    private static async Task VerifyImportErrorsAsync(MainWindow window, InboxViewModel model, FixtureDialogs dialogs,
        string fixture, string directory)
    {
        var input = Directory.CreateTempSubdirectory("plico-verify-retry-");
        try
        {
            string damaged = Path.Combine(input.FullName, "Check import.pdf");
            File.WriteAllText(damaged, "Invalid test PDF");
            dialogs.SelectedFiles = [damaged];
            await ((IAsyncRelayCommand)Control<Button>(window, "AddFilesButton").Command!).ExecuteAsync(null);
            var errors = Control<Button>(window, "ImportErrorsButton");
            Require(errors.IsVisible && model.ImportFailures.Single().SourcePath == damaged,
                "The native window did not report the damaged input.");

            var dialog = new ImportErrorsDialog(model.ImportFailures);
            var closed = dialog.ShowDialog<ImportErrorsAction>(window);
            try
            {
                Require(Control<ListBox>(dialog, "FailuresList").ItemCount == 1, "The detail window did not list the source.");
                await CaptureAsync(dialog, directory, "import-errors.png");
            }
            finally
            {
                dialog.Close();
                await closed;
            }

            File.Copy(Path.Combine(Path.GetDirectoryName(fixture)!, "Mixed Pages.pdf"), damaged, overwrite: true);
            Control<ComboBox>(window, "OcrLanguageBox").SetCurrentValue(ComboBox.SelectedItemProperty, OcrLanguage.English);
            dialogs.ImportErrorsChoice = ImportErrorsAction.Retry;
            await ((IAsyncRelayCommand)errors.Command!).ExecuteAsync(null);
            await UntilAsync(() => model.SelectedDocument?.Title == "Check import" && model.Preview is not null, () => model.Status);
            Require(model.ImportFailures.Count == 0 && !errors.IsVisible, "The repaired file remained in the error list.");
        }
        finally
        {
            input.Delete(recursive: true);
        }
    }

    private static async Task VerifyLibraryBackupAsync(MainWindow window, InboxViewModel model, FixtureDialogs dialogs,
        string evidenceDirectory)
    {
        var destination = Directory.CreateTempSubdirectory("plico-verify-backup-");
        try
        {
            dialogs.FolderPath = destination.FullName;
            var button = Control<Button>(window, "BackUpLibraryButton");
            Require(button.IsEnabled && button.Command is IAsyncRelayCommand, "The library backup action was unavailable.");
            await ((IAsyncRelayCommand)button.Command!).ExecuteAsync(null);
            var backupFolder = Directory.EnumerateDirectories(destination.FullName).Single();
            using var backup = InboxLibrary.Open(backupFolder);
            var receipt = backup.Find(new DocumentFilter(Query: ReceiptKeyword, Tag: TestTag)).Items.Single();
            var invoice = backup.Find(new DocumentFilter(Query: "Règlement")).Items.Single();
            Require(receipt.Reviewed && File.Exists(backup.ResolvePdf(receipt)) && File.Exists(backup.ResolvePdf(invoice)),
                "The backed-up library lost a PDF or its catalog state.");
            var open = Control<Button>(window, "OpenBackupFolderButton");
            Require(open.IsVisible && open.Command is not null, "The backup action did not offer its folder.");
            open.Command!.Execute(null);
            Require(dialogs.LastOpenedPath == model.LatestBackupLocation && Directory.Exists(dialogs.LastOpenedPath) &&
                    Path.GetFileName(dialogs.LastOpenedPath) == Path.GetFileName(backupFolder),
                "The backup shortcut did not open the new backup folder.");
            await CaptureAsync(window, evidenceDirectory, "library-backup.png");
        }
        finally
        {
            model.LatestBackupLocation = null;
            destination.Delete(recursive: true);
        }
    }

    private static async Task VerifyOcrLanguageAsync(MainWindow window, InboxViewModel model, FixtureDialogs dialogs,
        string fixture, string directory)
    {
        var languages = Control<ComboBox>(window, "OcrLanguageBox");
        languages.SetCurrentValue(ComboBox.SelectedItemProperty, OcrLanguage.French);
        Require(model.RecognitionLanguage == OcrLanguage.French, "The OCR language control did not select French.");
        dialogs.SelectedFiles = [Path.Combine(Path.GetDirectoryName(fixture)!, "Facture française.png")];
        await ((IAsyncRelayCommand)Control<Button>(window, "AddFilesButton").Command!).ExecuteAsync(null);
        Require(model.SelectedDocument is { HasText: true }, model.Status);
        var search = Control<TextBox>(window, "SearchBox");
        search.SetCurrentValue(TextBox.TextProperty, "Règlement");
        await UntilAsync(() => model.SelectedDocument?.Match.Contains("Règlement", StringComparison.OrdinalIgnoreCase) == true &&
            model.Preview is not null, () => model.Status);
        await CaptureAsync(window, directory, "ocr-language.png");
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

    private static async Task VerifySortAsync(MainWindow window, InboxViewModel model, string directory)
    {
        model.ShowAllCommand.Execute(null);
        var list = Control<ListBox>(window, "DocumentList");
        var sort = Control<ComboBox>(window, "DocumentSortBox");
        var openDocument = model.SelectedDocument ?? throw new InvalidOperationException("Select a PDF before sorting.");
        await UntilAsync(() => model.Preview is not null, () => model.PreviewMessage);
        var openPreview = model.Preview;
        Require(model.Documents.Count == 2 && model.Documents[0].Id == openDocument.Id,
            "The newer document should appear first in the default view.");

        var byTitle = model.SortChoices.Single(choice => choice.Order == DocumentSort.Title);
        sort.SetCurrentValue(ComboBox.SelectedItemProperty, byTitle);
        Require(model.Documents[1].Id == openDocument.Id && list.SelectedItem is DocumentSummary selected &&
            selected.Id == openDocument.Id && ReferenceEquals(model.Preview, openPreview),
            "Title order did not move the open PDF while keeping its preview in place.");
        await CaptureAsync(window, directory, "sort-results.png");

        var newest = model.SortChoices.Single(choice => choice.Order == DocumentSort.Newest);
        sort.SetCurrentValue(ComboBox.SelectedItemProperty, newest);
        Require(model.Documents[0].Id == openDocument.Id, "Newest order did not restore the most recent PDF to the top.");
        sort.SetCurrentValue(ComboBox.SelectedItemProperty, model.SortChoices[0]);
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
            var options = new ScanOptionsPreference(settingsDirectory.FullName);
            options.Save(new CaptureSettings(ScanFeed.Feeder, ScanPaper.Letter, ScanResolution.Detailed));
            await CaptureScanDialogAsync(owner, directory, "saved-scanner.png", settings, options);
        }
        finally
        {
            settingsDirectory.Delete(recursive: true);
        }
    }

    private static async Task CaptureScanDialogAsync(Window owner, string directory, string filename,
        SavedScannerStore? settings = null, ScanOptionsPreference? captureOptions = null)
    {
        var dialog = new ScanDialog(_ => Task.FromResult(new ScanDiscovery([], [])),
            settings: settings, captureOptions: captureOptions);
        var result = dialog.ShowDialog<ScanRequest?>(owner);
        try
        {
            var address = Control<TextBox>(dialog, "AddressBox");
            var connect = Control<Button>(dialog, "ConnectButton");
            var resolution = Control<ComboBox>(dialog, "ResolutionBox");
            bool hasSavedScanner = settings is not null;
            string expectedResolution = captureOptions is null ? "300 dpi" : "600 dpi";
            Require(address.IsVisible && connect.IsEnabled && Control<Button>(dialog, "StartButton").IsEnabled == hasSavedScanner &&
                    Control<Button>(dialog, "ForgetButton").IsVisible == hasSavedScanner &&
                    resolution.ItemCount == 3 && resolution.SelectedItem?.ToString() == expectedResolution,
                "The native scanner dialog did not offer the expected scanner actions.");
            if (captureOptions is not null)
            {
                Require(Control<ComboBox>(dialog, "SourceBox").SelectedItem?.ToString() == "Feeder · one-sided" &&
                    Equals(Control<ComboBox>(dialog, "PaperBox").SelectedItem, ScanPaper.Letter),
                    "The native scanner dialog did not restore the saved paper options.");
            }
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
        var existing = Control<Button>(window, "ImportWatchFolderFilesButton");
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
        string originalFolder = model.WatchedFolderPath;
        await ((IAsyncRelayCommand)stop.Command!).ExecuteAsync(null);
        Require(!model.IsWatchingFolder && !existing.IsVisible, "The folder watch did not stop.");
        var resume = Control<Button>(window, "ResumeFolderWatchButton");
        Require(resume.IsVisible && resume.Command is IAsyncRelayCommand, "The native window did not offer the remembered folder.");
        await CaptureAsync(window, directory, "resume-watch.png");
        await ((IAsyncRelayCommand)resume.Command!).ExecuteAsync(null);
        Require(model.IsWatchingFolder && model.WatchedFolderPath == originalFolder, "The watch did not resume its previous folder.");
        Require(existing.IsVisible && existing.IsEnabled && existing.Command is IAsyncRelayCommand,
            "The resumed watch did not offer to import existing files.");
        await ((IAsyncRelayCommand)existing.Command!).ExecuteAsync(null);
        await UntilAsync(() => !model.IsBusy && model.Status == "1 document already in the library.", () => model.Status);
        Require(model.Query == LetterKeyword && model.Documents.Count == 1 && File.Exists(incoming),
            "Importing the existing file changed the current search or consumed its source.");
        await CaptureAsync(window, directory, "watch-existing-files.png");
        await ((IAsyncRelayCommand)stop.Command!).ExecuteAsync(null);
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
    internal string? FolderPath { get; set; }
    internal string? EditedTitle { get; init; }
    internal string? LastOpenedPath { get; private set; }
    internal string? LastCopiedText { get; private set; }
    internal ImportErrorsAction ImportErrorsChoice { get; set; }
    internal IReadOnlyList<ImportFailure> InspectedImportFailures { get; private set; } = [];
    internal IReadOnlyList<string> SelectedFiles { get; set; } = documents;
    public Task<IReadOnlyList<string>> PickDocumentsAsync() => Task.FromResult(SelectedFiles);
    public Task<string?> PickFolderAsync(string title) => Task.FromResult(FolderPath);
    public Task<ScanRequest?> PickScannerAsync() => Task.FromResult<ScanRequest?>(null);
    public Task<string?> EditTitleAsync(string currentTitle) => Task.FromResult(EditedTitle);
    public Task<ImportErrorsAction> ShowImportErrorsAsync(IReadOnlyList<ImportFailure> failures)
    {
        InspectedImportFailures = failures;
        return Task.FromResult(ImportErrorsChoice);
    }
    public void OpenPath(string path) => LastOpenedPath = path;

    public Task CopyTextAsync(string text)
    {
        LastCopiedText = text;
        return Task.CompletedTask;
    }
}
