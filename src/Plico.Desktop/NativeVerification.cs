using System.Globalization;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CommunityToolkit.Mvvm.Input;
using Plico.Core;

namespace Plico.Desktop;

internal static partial class NativeVerification
{
    internal const string EditedDocumentTitle = "October housing";
    private const string ExpectedRuntimeVariable = "PLICO_EXPECTED_NATIVE_RID";
    private const string CaptureProgressFilename = "progress.txt";
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
            await VerifySingleExportAsync(window, model, dialogs, evidenceDirectory);
            await VerifyArchivalExportAsync(window, model, dialogs, evidenceDirectory);

            Control<AutoCompleteBox>(window, "TagBox").SetCurrentValue(AutoCompleteBox.TextProperty, TestTag);
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
            await VerifyBooleanSearchAsync(window, model, evidenceDirectory);
            await VerifyPageTextViewAsync(window, model, evidenceDirectory);
            await VerifySearchQueryFeedbackAsync(window, model, evidenceDirectory);
            await VerifyDropAsync(window, model, fixture, evidenceDirectory);
            await VerifyWatchedFolderAsync(window, model, fixture, evidenceDirectory);
            await VerifyTitleEditorAsync(window, model, evidenceDirectory);
            VerifyDocumentLocation(window, dialogs, Path.Combine(model.LibraryPath, "PDFs"));
            await VerifyMovedPdfAsync(window, model, dialogs, evidenceDirectory);
            await VerifyMoveActionAsync(window, model, dialogs, evidenceDirectory);
            await VerifyPhraseSearchAsync(window, model, evidenceDirectory);
            await VerifySortAsync(window, model, evidenceDirectory);
            await VerifyBatchReviewAsync(window, model, dialogs, evidenceDirectory);
            await VerifyOcrLanguagesAsync(window, model, dialogs, fixture, evidenceDirectory);
            await VerifyCopiedTextAsync(window, model, dialogs);
            await VerifyLibraryBackupAsync(window, model, dialogs, evidenceDirectory);
            await VerifyImportErrorsAsync(window, model, dialogs, fixture, evidenceDirectory);
            await VerifyPreviewFitAsync(window, model, evidenceDirectory);
            await VerifyGoToMatchAsync(window, model, evidenceDirectory);
            await VerifyTagMergeAsync(window, model, dialogs, evidenceDirectory);
            long priorOcrSource = await VerifySearchableCopyAsync(window, model, fixture, evidenceDirectory);
            await VerifyTiffImportAsync(window, model, dialogs, fixture, evidenceDirectory);
            await VerifyTagSuggestionAsync(window, model, evidenceDirectory);
            await VerifyAdjacentMatchesAsync(window, model, dialogs, fixture, evidenceDirectory);
            await VerifyOrientedJpegAsync(window, model, dialogs, fixture, evidenceDirectory);
            await VerifyCombinedPdfsAsync(window, model, dialogs, evidenceDirectory);
            await VerifyBatchMoveAsync(window, model, dialogs, evidenceDirectory);
            await VerifyExtractPagesAsync(window, model, dialogs, evidenceDirectory);
            await VerifyRotatePagesAsync(window, model, dialogs, evidenceDirectory);
            await VerifyScannerDialogAsync(window, evidenceDirectory);
            string scanRecoveryEvidence = await VerifyScanRecoveryAsync(window, model, dialogs, evidenceDirectory);
            await VerifyManualDuplexDialogAsync(window, evidenceDirectory);
            await VerifySavedSearchAsync(window, model, dialogs, evidenceDirectory);
            await VerifyDocumentDateAsync(window, model, dialogs, evidenceDirectory);
            await VerifyBatchOcrAsync(window, model, fixture, evidenceDirectory, priorOcrSource);
            await VerifyExternalSwapAsync(window, model, dialogs, fixture, evidenceDirectory);
            await VerifyFolderSearchPickerAsync(window, model, dialogs, evidenceDirectory);
            await VerifyIntakeFolderAsync(window, model, dialogs, fixture, evidenceDirectory);
            await VerifyBatchTagPagingAsync(window, model, fixture, evidenceDirectory);
            await VerifyBatchDocumentDatesAsync(window, model, dialogs, evidenceDirectory);
            await VerifyReviewPositionAsync(window, model, evidenceDirectory);
            await VerifyWatchCatchUpAsync(window, model, fixture, evidenceDirectory);
            await VerifyWatchCatchUpOnOpenAsync(window, model, dialogs, fixture, evidenceDirectory);
            await VerifyRetryFilingAsync(window, model, dialogs, fixture, evidenceDirectory);
            await VerifyTrashAsync(window, model, dialogs, evidenceDirectory);
            await VerifyFilingSuggestionsAsync(window, model, dialogs, evidenceDirectory);
            await VerifyPortabilityAsync(window, model, dialogs, evidenceDirectory);

            File.WriteAllText(Path.Combine(evidenceDirectory, "passed.txt"),
                $"Verified {runtime}. Native window completed multilingual OCR and correction, oriented JPEG and multi-page TIFF import, PDF combination, page extraction and rotation, phrase, tag, review-status, folder, and text-availability search, query correction, saved search restoration, single and batch document-date editing, date filtering and inheritance, result sorting, continued review across loaded results, fit-width and selectable page text previews, match navigation, tag merging, suggestions, and edits across loaded result pages, duplicate drop, folder watch with catch-up on open and intake routing, inbox counting, title edit, single and batch PDF moves, external relocation and PDF swaps, index reporting and PDF restoration from files and folders, single and batch PDF export, library backup, text copy, single and batch OCR copies, saved import error recovery and retry destinations, per-scanner capture option restoration, and filing through the chosen intake folder. {scanRecoveryEvidence}");
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
        const string reviewTag = "Scan review";
        var tagBox = Control<AutoCompleteBox>(window, "TagBox");
        var addTag = Control<Button>(window, "AddTagButton").Command ?? throw new InvalidOperationException("Tag action unavailable.");
        tagBox.SetCurrentValue(AutoCompleteBox.TextProperty, TestTag);
        addTag.Execute(null);
        tagBox.SetCurrentValue(AutoCompleteBox.TextProperty, reviewTag);
        addTag.Execute(null);
        model.PreviousPageCommand.Execute(null);
        Require(model.PageLabel == "1 / 2", "Search match navigation needs the first page of the mixed PDF.");
        var jump = Control<Button>(window, "GoToMatchButton");
        var search = Control<TextBox>(window, "SearchBox");
        string addedDay = document.AddedAt.LocalDateTime.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        string filters = $"tag:{TestTag} tag:\"{reviewTag}\" added:{addedDay} folder:.";
        search.SetCurrentValue(TextBox.TextProperty, filters);
        await UntilAsync(() => model.Heading == "Search results" && model.Documents.Count == 1, () => model.Status);
        Require(model.SelectedDocument?.Id == document.Id && !jump.IsVisible,
            "Tag, date, and root-folder filters did not isolate the mixed PDF.");

        search.SetCurrentValue(TextBox.TextProperty, $"{ReceiptKeyword} {filters}");
        await UntilAsync(() => model.Documents.Count == 1 && jump.IsVisible && jump.IsEnabled, () => model.Status);
        Require(model.SelectedDocument?.Id == document.Id && jump.Command is IAsyncRelayCommand,
            "The mixed PDF’s search result did not offer a matching page.");

        var read = Control<Button>(window, "ReadPageTextButton");
        read.Command!.Execute(null);
        await UntilAsync(() => model.PageText.Contains(LetterKeyword, StringComparison.OrdinalIgnoreCase), () => model.PreviewMessage);
        await ((IAsyncRelayCommand)jump.Command!).ExecuteAsync(jump.CommandParameter);
        await UntilAsync(() => model.PageLabel == "2 / 2" &&
            model.PageText.Contains(ReceiptKeyword, StringComparison.OrdinalIgnoreCase), () => model.PreviewMessage);
        Require(!model.PageText.Contains(LetterKeyword, StringComparison.OrdinalIgnoreCase),
            "The matching-page text still contained words from the previous page.");
        await CaptureAsync(window, directory, "page-text-match.png");

        read.Command.Execute(null);
        await UntilAsync(() => model.PageLabel == "2 / 2" && model.Preview is not null, () => model.Status);
        await CaptureAsync(window, directory, "matching-page.png");
    }

    private static async Task<long> VerifySearchableCopyAsync(MainWindow window, InboxViewModel model, string fixture, string directory)
    {
        const string scanName = "Pending OCR.pdf";
        Control<ComboBox>(window, "OcrLanguageBox").SetCurrentValue(ComboBox.SelectedItemProperty, OcrLanguage.English);
        string scanPath = Path.Combine(model.LibraryPath, "PDFs", scanName);
        string scannedFixture = Path.Combine(Path.GetDirectoryName(fixture)!, "Scanned Receipt.pdf");
        byte[] sourceBytes = File.ReadAllBytes(scannedFixture);
        File.WriteAllBytes(scanPath, sourceBytes);
        model.ShowInboxCommand.Execute(null);
        await model.RefreshIndexCommand.ExecuteAsync(null);
        var original = model.Documents.Single(document => document.RelativePath == scanName);
        Control<ListBox>(window, "DocumentList").SetCurrentValue(ListBox.SelectedItemProperty, original);
        await UntilAsync(() => model.SelectedDocument?.Id == original.Id && model.Preview is not null, () => model.Status);
        Control<AutoCompleteBox>(window, "TagBox").SetCurrentValue(AutoCompleteBox.TextProperty, TestTag);
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
        return original.Id;
    }

    private static async Task VerifyTagMergeAsync(MainWindow window, InboxViewModel model, FixtureDialogs dialogs, string directory)
    {
        const string source = "Scan review";
        model.FilterByTag(source);
        long sourceDocumentId = model.Documents.Single().Id;
        var dialog = new TagRenameDialog(source, model.Tags);
        var closed = dialog.ShowDialog<TagRenameRequest?>(window);
        TagRenameRequest? request;
        try
        {
            Control<TextBox>(dialog, "TagNameBox").SetCurrentValue(TextBox.TextProperty, TestTag.ToLowerInvariant());
            var merge = Control<Button>(dialog, "SaveTagButton");
            await UntilAsync(() => Equals(merge.Content, "Merge tags"), () => "The rename dialog did not offer a merge.");
            Require(!merge.IsDefault, "The merge action was set as the default submit button.");
            await CaptureAsync(dialog, directory, "tag-merge-dialog.png");
            merge.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            request = await closed;
        }
        finally
        {
            if (dialog.IsVisible) dialog.Close();
            await closed;
        }

        Require(request is { MergeIntoExisting: true }, "The explicit merge action did not return a merge request.");
        dialogs.TagRenameChoice = request;
        try
        {
            var rename = Control<Button>(window, "RenameActiveTagButton");
            Require(rename.IsVisible && rename.IsEnabled && rename.Command is IAsyncRelayCommand,
                "The active tag did not offer a rename action.");
            await ((IAsyncRelayCommand)rename.Command!).ExecuteAsync(rename.CommandParameter);
            Require(model.Heading == TestTag && model.Documents.Count == 2 && model.SelectedDocument?.Id == sourceDocumentId &&
                    !model.Tags.Contains(source) && model.Documents.All(document => document.Tags.Contains(TestTag)),
                $"The merged tag did not open with the combined documents. {model.Status}");
            await UntilAsync(() => model.Preview is not null, () => model.PreviewMessage);
            await CaptureAsync(window, directory, "tag-merged.png");
        }
        finally
        {
            dialogs.TagRenameChoice = null;
        }
    }

    private static async Task VerifyTiffImportAsync(MainWindow window, InboxViewModel model, FixtureDialogs dialogs,
        string fixture, string directory)
    {
        string tiff = Path.Combine(Path.GetDirectoryName(fixture)!, "Scanner pages.TIFF");
        dialogs.SelectedFiles = [tiff];
        await ((IAsyncRelayCommand)Control<Button>(window, "AddFilesButton").Command!).ExecuteAsync(null);
        var imported = model.SelectedDocument ?? throw new InvalidOperationException("The TIFF import did not select a document.");
        Require(imported is { Title: "Scanner pages", PageCount: 2, HasText: true } && Path.GetExtension(imported.RelativePath) == ".pdf",
            $"The TIFF scan did not become one searchable PDF containing both pages. {model.Status}");
        Control<TextBox>(window, "SearchBox").SetCurrentValue(TextBox.TextProperty, ReceiptKeyword);
        var jump = Control<Button>(window, "GoToMatchButton");
        await UntilAsync(() => model.SelectedDocument?.Id == imported.Id && jump.IsVisible && jump.IsEnabled, () => model.Status);
        await ((IAsyncRelayCommand)jump.Command!).ExecuteAsync(jump.CommandParameter);
        await UntilAsync(() => model.PageLabel == "2 / 2" && model.Preview is not null, () => model.Status);
        await CaptureAsync(window, directory, "tiff-import.png");
    }

    private static async Task VerifyAdjacentMatchesAsync(MainWindow window, InboxViewModel model, FixtureDialogs dialogs,
        string fixture, string directory)
    {
        const string query = "\"ledger renewal\"";
        dialogs.SelectedFiles = [Path.Combine(Path.GetDirectoryName(fixture)!, "Match Ledger.pdf")];
        await ((IAsyncRelayCommand)Control<Button>(window, "AddFilesButton").Command!).ExecuteAsync(null);
        var search = Control<TextBox>(window, "SearchBox");
        var next = Control<Button>(window, "GoToMatchButton");
        var previous = Control<Button>(window, "PreviousMatchButton");
        var nextMatch = (IAsyncRelayCommand)next.Command!;
        var previousMatch = (IAsyncRelayCommand)previous.Command!;
        search.SetCurrentValue(TextBox.TextProperty, query);
        await UntilAsync(() => model.SelectedDocument?.Title == "Match Ledger" && next.IsVisible && next.IsEnabled,
            () => model.Status);

        await nextMatch.ExecuteAsync(next.CommandParameter);
        Require(model.PageLabel == "2 / 5" && previous.IsVisible && Equals(next.Content, "Next match"),
            "The first match did not open page two or offer navigation.");
        await UntilAsync(() => model.Preview is not null, () => model.PreviewMessage);
        await nextMatch.ExecuteAsync(next.CommandParameter);
        await UntilAsync(() => model.PageLabel == "4 / 5" && model.Preview is not null, () => model.Status);
        Require(model.Query == query && model.Documents.Count == 1, "Match navigation changed the search results.");
        await CaptureAsync(window, directory, "next-match.png");

        await previousMatch.ExecuteAsync(previous.CommandParameter);
        await UntilAsync(() => model.PageLabel == "2 / 5" && model.Preview is not null, () => model.Status);
    }

    private static async Task VerifyOrientedJpegAsync(MainWindow window, InboxViewModel model, FixtureDialogs dialogs,
        string fixture, string directory)
    {
        const string title = "Sideways receipt";
        dialogs.SelectedFiles = [Path.Combine(Path.GetDirectoryName(fixture)!, $"{title}.JPG")];
        await ((IAsyncRelayCommand)Control<Button>(window, "AddFilesButton").Command!).ExecuteAsync(null);
        var imported = model.SelectedDocument;
        Require(imported is { Title: title, PageCount: 1, HasText: true }, $"The photo did not become a searchable PDF. {model.Status}");

        Control<TextBox>(window, "SearchBox").SetCurrentValue(TextBox.TextProperty, ReceiptKeyword);
        await UntilAsync(() => model.SelectedDocument is { } selected && selected.Id == imported!.Id &&
            selected.Match.Contains(ReceiptKeyword, StringComparison.OrdinalIgnoreCase) && model.Preview is not null, () => model.Status);
        Require(model.Preview!.PixelSize.Width < model.Preview.PixelSize.Height, "The imported photo was still sideways.");
        await CaptureAsync(window, directory, "upright-jpeg.png");
    }

    private static async Task VerifyCombinedPdfsAsync(MainWindow window, InboxViewModel model, FixtureDialogs dialogs, string directory)
    {
        const string receiptTitle = "Sideways receipt";
        const string ledgerTitle = "Match Ledger";
        const string packetTitle = "Paperwork packet";
        const string packetTag = "Packet";
        Require(model.SelectedDocument?.Title == receiptTitle, "The receipt must be open before testing PDF combination.");
        Control<AutoCompleteBox>(window, "TagBox").SetCurrentValue(AutoCompleteBox.TextProperty, packetTag);
        Control<Button>(window, "AddTagButton").Command!.Execute(null);
        model.ShowAllCommand.Execute(null);
        var selected = model.Documents.Where(document => document.Title is receiptTitle or ledgerTitle).ToList();
        Require(selected.Count == 2 && selected[0].Title == receiptTitle, "The ordered PDF fixtures were unavailable.");
        Control<ListBox>(window, "DocumentList").SelectedItems = selected;
        var action = Control<Button>(window, "CombinePdfsButton");
        Require(model.HasBatchSelection && action.IsEffectivelyVisible && action.IsEnabled, "The PDF selection did not offer combination.");

        dialogs.CombineChoice = documents => ConfirmCombineOrderAsync(window, documents, packetTitle, directory);
        try
        {
            await ((IAsyncRelayCommand)action.Command!).ExecuteAsync(action.CommandParameter);
        }
        finally
        {
            dialogs.CombineChoice = null;
        }
        var packet = model.SelectedDocument;
        Require(packet is { Title: packetTitle, HasText: true, PageCount: 6, Reviewed: false } && packet.Tags.Contains(packetTag),
            $"The combined PDF did not open with its source pages and tag. {model.Status}");

        Control<TextBox>(window, "SearchBox").SetCurrentValue(TextBox.TextProperty, ReceiptKeyword);
        var jump = Control<Button>(window, "GoToMatchButton");
        await UntilAsync(() => model.SelectedDocument?.Id == packet!.Id && jump.IsVisible && jump.IsEnabled, () => model.Status);
        await ((IAsyncRelayCommand)jump.Command!).ExecuteAsync(jump.CommandParameter);
        await UntilAsync(() => model.PageLabel == "6 / 6" && model.Preview is not null, () => model.Status);
        await CaptureAsync(window, directory, "combined-pdfs.png");
    }

    private static async Task<CombinePdfsRequest?> ConfirmCombineOrderAsync(Window owner, IReadOnlyList<DocumentSummary> documents,
        string title, string directory)
    {
        var dialog = new CombinePdfsDialog(documents);
        var response = dialog.ShowDialog<CombinePdfsRequest?>(owner);
        try
        {
            var down = Control<Button>(dialog, "MoveDownButton");
            Require(down.IsEnabled, "The first source PDF could not be moved in the order dialog.");
            down.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Control<TextBox>(dialog, "TitleBox").Text = title;
            await CaptureAsync(dialog, directory, "combine-order.png");
            Control<Button>(dialog, "ConfirmCombineButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            return await response;
        }
        finally
        {
            if (dialog.IsVisible) dialog.Close();
            await response;
        }
    }

    private static async Task VerifyBatchMoveAsync(MainWindow window, InboxViewModel model, FixtureDialogs dialogs, string directory)
    {
        var sources = model.Documents.Where(document => document.Title is "Paperwork packet" or "Sideways receipt").ToArray();
        Require(sources.Length == 2, "The PDFs for batch filing were missing from the search.");
        Control<ListBox>(window, "DocumentList").SelectedItems = sources.ToList();
        var action = Control<Button>(window, "MoveSelectedPdfsButton");
        Require(model.HasBatchSelection && action.IsEffectivelyVisible && action.IsEnabled, "The selection did not offer batch filing.");
        await CaptureAsync(window, directory, "batch-move-selection.png");

        string query = model.Query;
        string pdfs = Path.Combine(model.LibraryPath, "PDFs");
        string destination = Directory.CreateDirectory(Path.Combine(pdfs, "Filed", "Packets")).FullName;
        string? previousFolder = dialogs.FolderPath;
        int previousPicks = dialogs.FolderPickerCalls;
        dialogs.FolderPath = destination;
        try
        {
            await ((IAsyncRelayCommand)action.Command!).ExecuteAsync(action.CommandParameter);
        }
        finally
        {
            dialogs.FolderPath = previousFolder;
        }
        Require(dialogs.FolderPickerCalls == previousPicks + 1 && dialogs.LastFolderStart == pdfs && model.Query == query &&
            model.Status == "Moved 2 documents.", model.Status);
        foreach (var source in sources)
        {
            var moved = model.Documents.Single(document => document.Id == source.Id);
            Require(moved.RelativePath.StartsWith("Filed/Packets/", StringComparison.Ordinal) &&
                File.Exists(Path.Combine(pdfs, moved.RelativePath)) && !File.Exists(Path.Combine(pdfs, source.RelativePath)),
                $"The source {source.RelativePath} did not complete its move to {moved.RelativePath}.");
        }

        Control<TextBox>(window, "SearchBox").SetCurrentValue(TextBox.TextProperty, $"{ReceiptKeyword} folder:Filed/Packets");
        await UntilAsync(() => model.ResultCount == "2 documents" && model.Preview is not null, () => model.Status);
        await CaptureAsync(window, directory, "batch-move-result.png");
    }

    private static async Task VerifyExtractPagesAsync(MainWindow window, InboxViewModel model, FixtureDialogs dialogs, string directory)
    {
        const string excerptTitle = "Receipt excerpt";
        var packet = model.Documents.Single(document => document.Title == "Paperwork packet");
        Control<ListBox>(window, "DocumentList").SetCurrentValue(ListBox.SelectedItemProperty, packet);
        await UntilAsync(() => model.SelectedDocument?.Id == packet.Id && model.Preview is not null, () => model.Status);
        var action = Control<Button>(window, "ExtractPdfPagesButton");
        Require(action.IsEffectivelyVisible && action.IsEnabled, "The six-page packet did not offer page extraction.");
        dialogs.PageCopyChoice = (source, page, actionKind) =>
        {
            Require(source.Id == packet.Id && source.PageCount == 6 && page == 1 && actionKind == PdfPageCopyAction.Extract,
                "The extraction dialog did not start at the open page.");
            var dialog = new PdfPageCopyDialog(source.PageCount, page, actionKind);
            return ConfirmPageCopyAsync(window, dialog, "6,2-3,3", excerptTitle, directory, "extract-pages-dialog.png");
        };
        try
        {
            await ((IAsyncRelayCommand)action.Command!).ExecuteAsync(action.CommandParameter);
        }
        finally
        {
            dialogs.PageCopyChoice = null;
        }
        var excerpt = model.SelectedDocument;
        Require(excerpt is { Title: excerptTitle, PageCount: 3, HasText: true, Reviewed: false } &&
            excerpt.Id != packet.Id && excerpt.Tags.Contains("Packet") &&
            File.Exists(Path.Combine(model.LibraryPath, "PDFs", packet.RelativePath)), model.Status);

        Control<TextBox>(window, "SearchBox").SetCurrentValue(TextBox.TextProperty, ReceiptKeyword);
        var jump = Control<Button>(window, "GoToMatchButton");
        await UntilAsync(() => model.SelectedDocument?.Id == excerpt!.Id && jump.IsVisible && jump.IsEnabled, () => model.Status);
        await ((IAsyncRelayCommand)jump.Command!).ExecuteAsync(jump.CommandParameter);
        await UntilAsync(() => model.PageLabel == "3 / 3" && model.Preview is not null, () => model.Status);
        await CaptureAsync(window, directory, "extracted-pdf.png");
    }

    private static async Task VerifyRotatePagesAsync(MainWindow window, InboxViewModel model, FixtureDialogs dialogs, string directory)
    {
        const string title = "Rotated receipt excerpt";
        const string documentDateInput = "2024-02-29";
        var documentDate = new DateOnly(2024, 2, 29);
        var original = model.SelectedDocument ?? throw new InvalidOperationException("Open the excerpt before rotating it.");
        Require(original.PageCount == 3 && model.PageLabel == "3 / 3" && model.Preview!.PixelSize.Width < model.Preview.PixelSize.Height,
            "The receipt page was unavailable for rotation.");
        var dateAction = Control<Button>(window, "EditDocumentDateButton");
        dialogs.DocumentDateChoice = current => ConfirmDocumentDateAsync(window, current, documentDateInput, directory: null);
        try
        {
            await ((IAsyncRelayCommand)dateAction.Command!).ExecuteAsync(dateAction.CommandParameter);
        }
        finally
        {
            dialogs.DocumentDateChoice = null;
        }
        Require(model.SelectedDocument?.DocumentDate == documentDate && model.PageLabel == "3 / 3", model.Status);

        var action = Control<Button>(window, "RotatePdfPagesButton");
        Require(action.IsEffectivelyVisible && action.IsEnabled, "The opened PDF did not offer page rotation.");
        dialogs.PageCopyChoice = (source, page, actionKind) =>
        {
            Require(source.Id == original.Id && page == 3 && actionKind == PdfPageCopyAction.Rotate,
                "The rotation dialog did not start at the open page.");
            var dialog = new PdfPageCopyDialog(source.PageCount, page, actionKind);
            return ConfirmPageCopyAsync(window, dialog, "3", title, directory, "rotate-pages-dialog.png");
        };
        try
        {
            await ((IAsyncRelayCommand)action.Command!).ExecuteAsync(action.CommandParameter);
        }
        finally
        {
            dialogs.PageCopyChoice = null;
        }
        var rotated = model.SelectedDocument;
        Require(rotated is { Title: title, PageCount: 3, HasText: true, Reviewed: false } && rotated.DocumentDate == documentDate &&
            rotated.Id != original.Id && rotated.Tags.Contains("Packet") &&
            File.Exists(Path.Combine(model.LibraryPath, "PDFs", original.RelativePath)), model.Status);

        Control<TextBox>(window, "SearchBox").SetCurrentValue(TextBox.TextProperty, $"{ReceiptKeyword} dated:{documentDateInput}");
        var jump = Control<Button>(window, "GoToMatchButton");
        await UntilAsync(() => model.SelectedDocument?.Id == rotated!.Id && jump.IsVisible && jump.IsEnabled, () => model.Status);
        await ((IAsyncRelayCommand)jump.Command!).ExecuteAsync(jump.CommandParameter);
        await UntilAsync(() => model.PageLabel == "3 / 3" && model.Preview is not null, () => model.Status);
        Require(model.Preview!.PixelSize.Width > model.Preview.PixelSize.Height, "The matching receipt page did not turn into landscape.");
        await CaptureAsync(window, directory, "rotated-pdf.png");
    }

    private static async Task<PdfPageCopyRequest?> ConfirmPageCopyAsync(Window owner, PdfPageCopyDialog dialog, string selection,
        string title, string directory, string capture)
    {
        var response = dialog.ShowDialog<PdfPageCopyRequest?>(owner);
        try
        {
            Control<TextBox>(dialog, "PagesBox").Text = selection;
            Control<TextBox>(dialog, "TitleBox").Text = title;
            await CaptureAsync(dialog, directory, capture);
            Control<Button>(dialog, "ConfirmPageCopyButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            return await response;
        }
        finally
        {
            if (dialog.IsVisible) dialog.Close();
            await response;
        }
    }

    private static async Task VerifyTagSuggestionAsync(MainWindow window, InboxViewModel model, string directory)
    {
        var tagBox = Control<AutoCompleteBox>(window, "TagBox");
        var input = tagBox.GetVisualDescendants().OfType<TextBox>().Single();
        Require(input.Focus(), "The selected PDF's tag field could not receive focus.");
        input.SetCurrentValue(TextBox.TextProperty, "work");
        await UntilAsync(() => tagBox.IsDropDownOpen, () => "The tag field did not suggest an existing tag.");
        Require(tagBox.ItemsSource?.Cast<string>().SequenceEqual([TestTag]) == true, "The suggestion did not use the catalog label.");
        await CaptureAsync(window, directory, "tag-suggestions.png");

        PressTagKey(input, Key.Down);
        PressTagKey(input, Key.Enter);
        Require(model.TagInput == TestTag && model.SelectedDocument?.Tags.Contains(TestTag) == false,
            "Choosing a suggestion did not fill the field without applying it.");
        PressTagKey(input, Key.Enter);
        Require(model.SelectedDocument?.Tags.Contains(TestTag) == true && string.IsNullOrEmpty(model.TagInput),
            "Pressing Enter did not apply the chosen tag and clear the field.");
    }

    private static void PressTagKey(TextBox input, Key key) => input.RaiseEvent(new KeyEventArgs
    {
        RoutedEvent = InputElement.KeyDownEvent,
        Key = key
    });

    private static async Task VerifyImportErrorsAsync(MainWindow window, InboxViewModel model, FixtureDialogs dialogs,
        string fixture, string directory)
    {
        var input = Directory.CreateTempSubdirectory("plico-verify-retry-");
        string libraryRoot = model.LibraryPath;
        string? rememberedFolder = dialogs.FolderPath;
        try
        {
            string damaged = Path.Combine(input.FullName, "Check import.pdf");
            File.WriteAllText(damaged, "Invalid test PDF");
            dialogs.SelectedFiles = [damaged];
            await ((IAsyncRelayCommand)Control<Button>(window, "AddFilesButton").Command!).ExecuteAsync(null);
            var errors = Control<Button>(window, "ImportErrorsButton");
            Require(errors.IsVisible && model.ImportFailures.Single().SourcePath == damaged,
                "The native window did not report the damaged input.");

            string otherLibrary = Directory.CreateDirectory(Path.Combine(libraryRoot, "Verification library")).FullName;
            dialogs.FolderPath = otherLibrary;
            await model.ChooseLibraryCommand.ExecuteAsync(null);
            Require(model.LibraryPath == otherLibrary && model.ImportFailures.Count == 0 && !errors.IsVisible,
                $"The blank library did not open without errors. {model.Status}");
            dialogs.FolderPath = libraryRoot;
            await model.ChooseLibraryCommand.ExecuteAsync(null);
            Require(model.LibraryPath == libraryRoot && errors.IsVisible && model.ImportFailures.Single().SourcePath == damaged,
                $"Reopening the library did not restore its import error. {model.Status}");

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
            dialogs.FolderPath = rememberedFolder;
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

    private static async Task VerifyOcrLanguagesAsync(MainWindow window, InboxViewModel model, FixtureDialogs dialogs,
        string fixture, string directory)
    {
        string fixtures = Path.GetDirectoryName(fixture)!;
        var original = await ImportOcrFixtureAsync(window, model, dialogs, Path.Combine(fixtures, "Spanish Receipt.png"), OcrLanguage.English);
        const string repairTag = "Repairs";
        Control<AutoCompleteBox>(window, "TagBox").SetCurrentValue(AutoCompleteBox.TextProperty, repairTag);
        Control<Button>(window, "AddTagButton").Command!.Execute(null);
        Control<ComboBox>(window, "OcrLanguageBox").SetCurrentValue(ComboBox.SelectedItemProperty, OcrLanguage.Spanish);
        var copy = Control<Button>(window, "CreateSearchableCopyButton");
        Require(model.RecognitionLanguage == OcrLanguage.Spanish && copy.IsEffectivelyVisible && copy.IsEnabled &&
            Equals(copy.Content, "Create OCR copy"), "The searchable receipt did not offer a fresh OCR copy in Spanish.");
        await ((IAsyncRelayCommand)copy.Command!).ExecuteAsync(copy.CommandParameter);
        var corrected = model.SelectedDocument;
        Require(corrected is { HasText: true, Reviewed: false } && corrected.Id != original.Id &&
            corrected.Tags.Contains(repairTag), $"OCR correction did not open a tagged copy. {model.Status}");
        await VerifyOcrResultAsync(window, model, corrected!, "pequeño", directory, "ocr-spanish.png");

        var french = await ImportOcrFixtureAsync(window, model, dialogs, Path.Combine(fixtures, "Facture française.png"), OcrLanguage.French);
        await VerifyOcrResultAsync(window, model, french, "Règlement", directory, "ocr-language.png");
    }

    private static async Task<DocumentSummary> ImportOcrFixtureAsync(MainWindow window, InboxViewModel model, FixtureDialogs dialogs,
        string path, OcrLanguage language)
    {
        Control<ComboBox>(window, "OcrLanguageBox").SetCurrentValue(ComboBox.SelectedItemProperty, language);
        Require(model.RecognitionLanguage == language, $"The OCR language control did not select {language}.");
        dialogs.SelectedFiles = [path];
        await ((IAsyncRelayCommand)Control<Button>(window, "AddFilesButton").Command!).ExecuteAsync(null);
        var imported = model.SelectedDocument;
        Require(imported is { HasText: true } && imported.Title == Path.GetFileNameWithoutExtension(path), model.Status);
        return imported!;
    }

    private static async Task VerifyOcrResultAsync(MainWindow window, InboxViewModel model, DocumentSummary document,
        string keyword, string directory, string capture)
    {
        Control<TextBox>(window, "SearchBox").SetCurrentValue(TextBox.TextProperty, keyword);
        await UntilAsync(() => model.SelectedDocument is { } selected && selected.Id == document.Id &&
            selected.Match.Contains(keyword, StringComparison.OrdinalIgnoreCase) && model.Preview is not null, () => model.Status);
        await CaptureAsync(window, directory, capture);
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

    private static async Task VerifyBatchReviewAsync(MainWindow window, InboxViewModel model, FixtureDialogs dialogs, string directory)
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
        Control<AutoCompleteBox>(window, "BatchTagBox").SetCurrentValue(AutoCompleteBox.TextProperty, batchTag);
        Control<Button>(window, "ApplyBatchTagButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        await UntilAsync(() => model.HasBatchSelection && model.Status == "Tag applied to 2 documents." && model.Preview is not null,
            () => model.Status);
        Require(model.Documents.All(document => document.Tags.Contains(batchTag)),
            "The tag did not attach to both selected PDFs.");
        await CaptureAsync(window, directory, "batch-review.png");
        await VerifyBatchTagRemovalAsync(window, model, dialogs, batchTag, directory);
        await VerifySelectedExportAsync(window, model, dialogs, directory);
        await ((IAsyncRelayCommand)review.Command!).ExecuteAsync(null);
        model.ShowInboxCommand.Execute(null);
        Require(model.Documents.Count == 0 && model.Status == "2 documents marked reviewed.",
            "Batch review did not remove both selected documents from the inbox.");
    }

    private static async Task VerifySelectedExportAsync(MainWindow window, InboxViewModel model, FixtureDialogs dialogs, string directory)
    {
        var destination = Directory.CreateTempSubdirectory("plico-verify-export-");
        string? previousFolder = dialogs.FolderPath;
        try
        {
            dialogs.FolderPath = destination.FullName;
            var export = Control<Button>(window, "ExportSelectedPdfsButton");
            Require(export.IsEffectivelyVisible && export.IsEnabled && export.Command is IAsyncRelayCommand,
                "The selected PDFs did not offer an export action.");
            await ((IAsyncRelayCommand)export.Command!).ExecuteAsync(export.CommandParameter);
            var result = Directory.EnumerateDirectories(destination.FullName).Single();
            Require(model.HasBatchSelection && Directory.EnumerateFileSystemEntries(result).Count() == model.Documents.Count,
                $"Export did not copy the selected PDFs. {model.Status}");
            foreach (var document in model.Documents)
            {
                string source = Path.Combine(model.LibraryPath, "PDFs", document.RelativePath);
                string copied = Path.Combine(result, Path.GetFileName(document.RelativePath));
                Require(File.ReadAllBytes(source).AsSpan().SequenceEqual(File.ReadAllBytes(copied)),
                    "An exported PDF differed from its source.");
            }

            var open = Control<Button>(window, "OpenExportFolderButton");
            Require(open.IsVisible && open.IsEnabled && open.Command is not null, "The export shortcut was unavailable.");
            open.Command!.Execute(null);
            Require(dialogs.LastOpenedPath == model.LatestExportLocation && Directory.Exists(dialogs.LastOpenedPath) &&
                    Path.GetFileName(dialogs.LastOpenedPath) == Path.GetFileName(result),
                "The export shortcut opened a different folder.");
            await CaptureAsync(window, directory, "selected-export.png");
        }
        finally
        {
            model.LatestExportLocation = null;
            dialogs.FolderPath = previousFolder;
            destination.Delete(recursive: true);
        }
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

    private static async Task VerifyMoveActionAsync(MainWindow window, InboxViewModel model, FixtureDialogs dialogs, string directory)
    {
        var selected = model.SelectedDocument ?? throw new InvalidOperationException("Select the filed PDF before using Move to folder.");
        string query = model.Query;
        var preview = model.Preview;
        string pdfs = Path.Combine(model.LibraryPath, "PDFs");
        string source = Path.Combine(pdfs, selected.RelativePath);
        string destination = Directory.CreateDirectory(Path.Combine(pdfs, "Filed", "Property")).FullName;
        var action = Control<Button>(window, "MovePdfButton");
        Require(action.IsEffectivelyVisible && action.IsEnabled, "Move to folder was unavailable for the filed PDF.");
        string? previousFolder = dialogs.FolderPath;
        dialogs.FolderPath = destination;
        try
        {
            await ((IAsyncRelayCommand)action.Command!).ExecuteAsync(action.CommandParameter);
        }
        finally
        {
            dialogs.FolderPath = previousFolder;
        }
        Require(dialogs.LastFolderStart == pdfs && model.Query == query && ReferenceEquals(model.Preview, preview) &&
            model.SelectedDocument is { RelativePath: "Filed/Property/Housing renewal.pdf" } moved && moved.Id == selected.Id &&
            File.Exists(Path.Combine(destination, Path.GetFileName(source))) && !File.Exists(source), model.Status);
        VerifyDocumentLocation(window, dialogs, destination);
        await CaptureAsync(window, directory, "moved-with-plico.png");
        Control<TextBox>(window, "SearchBox").SetCurrentValue(TextBox.TextProperty, $"{LetterKeyword} folder:Filed/Property");
        await UntilAsync(() => model.ResultCount == "1 document" && model.SelectedDocument is { } found && found.Id == selected.Id &&
            found.Match.Contains(LetterKeyword, StringComparison.OrdinalIgnoreCase), () => model.Status);
        await CaptureAsync(window, directory, "folder-search.png");
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
            var flatbed = Scanners.FromAddress("flatbed.test:8080", "Office flatbed");
            var feeder = Scanners.FromAddress("scanner.test:8080", "Office feeder");
            settings.AddOrUpdate(flatbed);
            settings.AddOrUpdate(feeder);
            var options = new ScanOptionsPreference(settingsDirectory.FullName);
            options.Save(new CaptureSettings(ScanFeed.Flatbed, ScanPaper.A4, ScanResolution.Quick), flatbed);
            options.Save(new CaptureSettings(ScanFeed.Feeder, ScanPaper.Letter, ScanResolution.Detailed), feeder);
            options.SaveProfile("Duplex receipts", new(ScanFeed.Duplex, ScanPaper.Letter, ScanResolution.Standard,
                new ScanCleanup(ScanColour.Greyscale, ExcludeBlankPages: true, Deskew: true)));
            options.RememberLastScanner(feeder);
            await CaptureScanDialogAsync(owner, directory, "saved-scanner.png", settings, options, feeder, flatbed);
        }
        finally
        {
            settingsDirectory.Delete(recursive: true);
        }
    }

    private static async Task CaptureScanDialogAsync(Window owner, string directory, string filename,
        SavedScannerStore? settings = null, ScanOptionsPreference? captureOptions = null,
        ScannerDevice? expectedScanner = null, ScannerDevice? alternateScanner = null)
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
                var devices = Control<ComboBox>(dialog, "DeviceBox");
                Require(Equals(devices.SelectedItem, expectedScanner), "The scanner picker did not restore the last used device.");
                devices.SelectedItem = alternateScanner;
                Require(Control<ComboBox>(dialog, "SourceBox").SelectedItem?.ToString() == "Flatbed" &&
                    Equals(Control<ComboBox>(dialog, "PaperBox").SelectedItem, ScanPaper.A4) &&
                    resolution.SelectedItem?.ToString() == "150 dpi", "Changing scanners did not restore the other device's options.");
                devices.SelectedItem = expectedScanner;
                Require(Control<ComboBox>(dialog, "SourceBox").SelectedItem?.ToString() == "Feeder · one-sided" &&
                    Equals(Control<ComboBox>(dialog, "PaperBox").SelectedItem, ScanPaper.Letter) &&
                    resolution.SelectedItem?.ToString() == expectedResolution,
                    "The native scanner dialog did not restore the saved paper options.");
            }
            await CaptureAsync(dialog, directory, filename);
            if (captureOptions is not null)
            {
                Control<Expander>(dialog, "ScanCleanupExpander").IsExpanded = true;
                Control<ComboBox>(dialog, "ScanProfileBox").SelectedIndex = 0;
                Require(Control<CheckBox>(dialog, "SkipBlankPages").IsChecked == true &&
                    Control<ComboBox>(dialog, "ScanColourBox").SelectedItem?.ToString() == "Greyscale" &&
                    Control<ComboBox>(dialog, "SourceBox").SelectedItem?.ToString() == "Feeder · two-sided",
                    "Choosing the saved scan profile did not restore its cleanup settings.");
                await CaptureAsync(dialog, directory, "scan-profile.png");
            }
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

        var inboxCount = Control<TextBlock>(window, "InboxReviewCount");
        long before = model.InboxCount;
        long? openId = model.SelectedDocument?.Id;
        string letter = Path.Combine(Path.GetDirectoryName(fixture)!, "Lease Renewal.pdf");
        string incoming = Path.Combine(model.WatchedFolderPath, "Scanner output.pdf");
        string temporaryScan = Path.ChangeExtension(incoming, ".part");
        File.Copy(letter, temporaryScan);
        File.Move(temporaryScan, incoming);
        await UntilAsync(() => !model.IsBusy && model.Status == "1 document added.", () => model.Status);
        Require(model.Query == ReceiptKeyword && model.SelectedDocument?.Reviewed == true && File.Exists(incoming),
            "A watched import changed the current view or consumed the source file.");
        Require(model.InboxCount == before + 1 && model.SelectedDocument?.Id == openId && inboxCount.IsEffectivelyVisible,
            "The watched document did not increment the inbox count while the earlier PDF was open.");
        await CaptureAsync(window, directory, "watched-inbox-count.png");

        Control<TextBox>(window, "SearchBox").SetCurrentValue(TextBox.TextProperty, LetterKeyword);
        await UntilAsync(() => model.SelectedDocument?.Title == "Scanner output" && model.Preview is not null, () => model.Status);
        await CaptureAsync(window, directory, "watched-folder.png");
        string originalFolder = model.WatchedFolderPath;
        await ((IAsyncRelayCommand)stop.Command!).ExecuteAsync(null);
        Require(!model.IsWatchingFolder && existing.IsVisible && Equals(existing.Content, "Resume and catch up"),
            "The stopped watch did not offer catch-up for its remembered folder.");
        var resume = Control<Button>(window, "ResumeFolderWatchButton");
        Require(resume.IsVisible && resume.Command is IAsyncRelayCommand, "The native window did not offer the remembered folder.");
        await CaptureAsync(window, directory, "resume-watch.png");
        await ((IAsyncRelayCommand)resume.Command!).ExecuteAsync(null);
        Require(model.IsWatchingFolder && model.WatchedFolderPath == originalFolder, "The watch did not resume its previous folder.");
        Require(existing.IsVisible && existing.IsEnabled && Equals(existing.Content, "Import existing files") &&
                existing.Command is IAsyncRelayCommand,
            "The resumed watch did not offer to import existing files.");
        await ((IAsyncRelayCommand)existing.Command!).ExecuteAsync(null);
        await UntilAsync(() => !model.IsBusy && model.Status == "1 document already in the library.", () => model.Status);
        Require(model.Query == LetterKeyword && model.Documents.Count == 1 && model.InboxCount == before + 1 && File.Exists(incoming),
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
            File.AppendAllText(Path.Combine(directory, CaptureProgressFilename), filename + Environment.NewLine);
        }, DispatcherPriority.Background);
    }
}

/// <summary>A labelled fixture picker for verification with the same real library used by normal launches.</summary>
internal sealed class FixtureDialogs(params string[] documents) : IWorkspaceDialogs
{
    internal string? FolderPath { get; set; }
    internal string? LastFolderStart { get; private set; }
    internal int FolderPickerCalls { get; private set; }
    internal string? EditedTitle { get; init; }
    internal Func<DateOnly?, Task<DocumentDateEdit?>>? DocumentDateChoice { get; set; }
    internal Func<DateOnly?, int, Task<DocumentDateEdit?>>? BatchDocumentDateChoice { get; set; }
    internal Func<SavedSearchDraft, string?, bool, Task<SavedSearchDecision?>>? SavedSearchChoice { get; set; }
    internal TagRenameRequest? TagRenameChoice { get; set; }
    internal Func<IReadOnlyList<BatchTagOption>, Task<string?>>? TagRemovalChoice { get; set; }
    internal Func<IReadOnlyList<DocumentSummary>, Task<CombinePdfsRequest?>>? CombineChoice { get; set; }
    internal Func<DocumentSummary, int, PdfPageCopyAction, Task<PdfPageCopyRequest?>>? PageCopyChoice { get; set; }
    internal ScanRequest? ScannerChoice { get; set; }
    internal Func<ScanInterruption, CancellationToken, Task<bool>>? PartialScanChoice { get; set; }
    internal string? LastOpenedPath { get; private set; }
    internal string? LastCopiedText { get; private set; }
    internal ImportErrorsAction ImportErrorsChoice { get; set; }
    internal IReadOnlyList<ImportFailure> InspectedImportFailures { get; private set; } = [];
    internal IReadOnlyList<OcrCopyBatchItem> InspectedOcrCopyResults { get; private set; } = [];
    internal IndexReport? InspectedIndexReport { get; private set; }
    internal IndexReportDecision? IndexReportChoice { get; set; }
    internal string? RestoreCandidate { get; set; }
    internal Func<IReadOnlyList<TrashedDocument>, Task<TrashDecision?>>? TrashChoice { get; set; }
    internal Func<OcrLanguage, Task<PdfExportOptions?>>? PdfExportChoice { get; set; }
    internal Func<IReadOnlyList<FilingSuggestion>, Task<IReadOnlyList<FilingSelection>?>>? FilingChoice { get; set; }
    internal Func<IReadOnlyList<FilingRule>, Func<FilingRule, FilingRule>, Action<long>, Task>? RuleChoice { get; set; }
    internal Func<IntegrityReport, Task>? IntegrityChoice { get; set; }
    internal IReadOnlyList<string> SelectedFiles { get; set; } = documents;
    public Task<IReadOnlyList<string>> PickDocumentsAsync() => Task.FromResult(SelectedFiles);
    public Task<string?> PickFolderAsync(string title, string? startFolder = null)
    {
        FolderPickerCalls++;
        LastFolderStart = startFolder;
        return Task.FromResult(FolderPath);
    }
    public Task<ScanRequest?> PickScannerAsync() => Task.FromResult(ScannerChoice);
    public Task<bool> ConfirmPartialScanAsync(ScanInterruption interruption, CancellationToken cancellationToken) =>
        PartialScanChoice?.Invoke(interruption, cancellationToken) ?? Task.FromResult(false);
    public Task<ManualDuplexDecision> ChooseDuplexActionAsync(ManualDuplexPrompt prompt, CancellationToken cancellationToken) =>
        Task.FromResult(ManualDuplexDecision.SaveForLater);
    public Task<string?> EditTitleAsync(string currentTitle) => Task.FromResult(EditedTitle);
    public Task<DocumentDateEdit?> EditDocumentDateAsync(DateOnly? currentDate) =>
        DocumentDateChoice?.Invoke(currentDate) ?? Task.FromResult<DocumentDateEdit?>(null);
    public Task<DocumentDateEdit?> EditBatchDocumentDatesAsync(DateOnly? commonDate, int documentCount) =>
        BatchDocumentDateChoice?.Invoke(commonDate, documentCount) ?? Task.FromResult<DocumentDateEdit?>(null);
    public Task<SavedSearchDecision?> EditSavedSearchAsync(SavedSearchDraft draft, string? scopeTag, bool editing) =>
        SavedSearchChoice?.Invoke(draft, scopeTag, editing) ?? Task.FromResult<SavedSearchDecision?>(null);
    public Task<TagRenameRequest?> RenameTagAsync(string currentName, IReadOnlyList<string> catalogNames) => Task.FromResult(TagRenameChoice);
    public Task<string?> ChooseTagToRemoveAsync(IReadOnlyList<BatchTagOption> options) =>
        TagRemovalChoice?.Invoke(options) ?? Task.FromResult<string?>(null);
    public Task<CombinePdfsRequest?> OrderPdfCombinationAsync(IReadOnlyList<DocumentSummary> selectedDocuments) =>
        CombineChoice?.Invoke(selectedDocuments) ?? Task.FromResult<CombinePdfsRequest?>(null);
    public Task<PdfPageCopyRequest?> ChoosePdfPageCopyAsync(DocumentSummary document, int currentPage, PdfPageCopyAction action) =>
        PageCopyChoice?.Invoke(document, currentPage, action) ?? Task.FromResult<PdfPageCopyRequest?>(null);
    public Task<ImportErrorsAction> ShowImportErrorsAsync(IReadOnlyList<ImportFailure> failures)
    {
        InspectedImportFailures = failures;
        return Task.FromResult(ImportErrorsChoice);
    }
    public Task ShowOcrCopyBatchResultsAsync(IReadOnlyList<OcrCopyBatchItem> results)
    {
        InspectedOcrCopyResults = results;
        return Task.CompletedTask;
    }
    public Task<IndexReportDecision?> ShowIndexRefreshReportAsync(IndexReport report)
    {
        InspectedIndexReport = report;
        return Task.FromResult(IndexReportChoice);
    }
    public Task<string?> PickRestorePdfAsync() => Task.FromResult(RestoreCandidate);
    public Task<TrashDecision?> ShowTrashAsync(IReadOnlyList<TrashedDocument> documents) =>
        TrashChoice?.Invoke(documents) ?? Task.FromResult<TrashDecision?>(null);
    public Task<PdfExportOptions?> ChoosePdfExportAsync(OcrLanguage language) =>
        PdfExportChoice?.Invoke(language) ?? Task.FromResult<PdfExportOptions?>(new(PdfExportFormat.Original));
    public Task<IReadOnlyList<FilingSelection>?> ShowFilingSuggestionsAsync(IReadOnlyList<FilingSuggestion> suggestions) =>
        FilingChoice?.Invoke(suggestions) ?? Task.FromResult<IReadOnlyList<FilingSelection>?>(null);
    public Task ShowFilingRulesAsync(IReadOnlyList<FilingRule> rules, Func<FilingRule, FilingRule> save, Action<long> remove) =>
        RuleChoice?.Invoke(rules, save, remove) ?? Task.CompletedTask;
    public Task ShowIntegrityReportAsync(IntegrityReport report) => IntegrityChoice?.Invoke(report) ?? Task.CompletedTask;
    public void OpenPath(string path) => LastOpenedPath = path;

    public Task CopyTextAsync(string text)
    {
        LastCopiedText = text;
        return Task.CompletedTask;
    }
}
