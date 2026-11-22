using System.Windows.Input;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;

namespace Plico.Desktop;

/// <summary>One entry in the Ctrl+K / ⌘K command palette.</summary>
internal sealed record PaletteCommand(string Title, string Group, Geometry? Icon, IReadOnlyList<string> Keys, string Aliases,
    Action Run, ICommand? Source = null)
{
    internal bool MatchesAction(PaletteCommand other) => Source is null
        ? other.Source is null && Group == other.Group && Title == other.Title
        : ReferenceEquals(Source, other.Source);
}

/// <summary>Builds and filters the command palette. Every tool in Plico is reachable here, with its shortcut.</summary>
internal static class PaletteCommands
{
    internal static string Mod => OperatingSystem.IsMacOS() ? "⌘" : "Ctrl";
    private static KeyModifiers CommandModifier => OperatingSystem.IsMacOS() ? KeyModifiers.Meta : KeyModifiers.Control;
    public static KeyGesture SettingsGesture { get; } = new(Key.OemComma, CommandModifier);
    public static KeyGesture AddFilesGesture { get; } = new(Key.O, CommandModifier);
    public static KeyGesture PaletteGesture { get; } = new(Key.K, CommandModifier);

    internal static IReadOnlyList<PaletteCommand> Build(MainWindow window, InboxViewModel model)
    {
        var commands = new List<PaletteCommand>(80);
        string review = T("Review"), go = T("Go to"), add = T("Add"), document = T("Document"), selection = T("Selection"), library = T("Library");

        void Add(string title, string group, string icon, string keys, string aliases, ICommand command, bool available = true)
        {
            Act(title, group, icon, keys, aliases, () => command.Execute(null), available && command.CanExecute(null), command);
        }
        void Act(string title, string group, string icon, string keys, string aliases, Action action, bool available = true, ICommand? source = null)
        {
            if (available) commands.Add(new(T(title), group, Icon(icon), Split(keys), aliases, action, source));
        }

        Add("Choose library folder…", library, "Icon.Folder", "", "switch open library", model.ChooseLibraryCommand, model.IsIdle);
        Act("Settings", library, "Icon.Sliders", Mod + " ,", "preferences options", window.OpenSettings);
        Act("Keyboard shortcuts", library, "Icon.Keyboard", "?", "keys help", window.ShowShortcutSheet);
        if (!model.CanWork)
        {
            Add("Cancel", library, "Icon.Close", "", "stop operation", model.CancelCommand, model.IsBusy);
            return commands;
        }
        if (model.ShowFilingCard)
        {
            Act(model.SelectedIsReviewed ? "Back to Inbox" : "Mark reviewed", review, "Icon.Check", "R", "done file reviewed", window.ReviewWithMotion);
            Add("Accept all suggestions", review, "Icon.Check", "A", "suggestion accept", model.AcceptSuggestionsCommand, model.HasSuggestions);
            Act("Add a tag", review, "Icon.Tag", "T", "label", window.FocusTagEntry, model.ShowFilingCard);
            Act("Set the document date", review, "Icon.Calendar", "D", "date dated", window.FocusDocumentDate);
            Act("Rename", review, "Icon.Note", "E", "title edit", window.BeginTitleEdit);
            Add("Move to Trash", review, "Icon.Trash", "X", "delete remove bin", model.TrashDocumentCommand);
            Add("Open in your PDF viewer", review, "Icon.Open", "O", "open pdf external", model.OpenPdfCommand);
            Add("Reminder…", review, "Icon.Bell", "", "deadline due renewal expiry", model.EditDeadlinesCommand);
            Add("Details…", review, "Icon.Sliders", "", "metadata structured", model.EditMetadataCommand);
        }
        Add("Undo", review, "Icon.History", Mod + " Z", "revert", model.UndoCommand, model.HasUndo);

        Add("Inbox", go, "Icon.AllDocuments", "", "queue review", model.ShowInboxCommand);
        Add("All documents", go, "Icon.AllDocuments", "", "library everything", model.ShowAllCommand);
        Add("Upcoming", go, "Icon.Upcoming", "", "due deadline", model.RemindersCommand);
        Add("Without text", go, "Icon.Recognize", "", "ocr missing scan", model.ShowWithoutTextCommand);
        Add("Trash", go, "Icon.Trash", "", "restore deleted bin", model.OpenTrashCommand);
        Act("Search", go, "Icon.Search", "/", "find query", window.FocusSearchField);
        Add("Search a folder…", go, "Icon.Folder", "", "folder path", model.ChooseSearchFolderCommand);
        Add("Save search…", go, "Icon.Bookmark", "", "saved shortcut", model.SaveSearchCommand, model.ShowSaveSearch);
        Add("Edit search…", go, "Icon.Bookmark", "", "saved shortcut", model.EditActiveSavedSearchCommand, model.HasActiveSavedSearch);
        Add("Rename selected tag", go, "Icon.Tag", "", "tag label", model.RenameActiveTagCommand, model.HasActiveTag);
        Add("Load more", go, "Icon.Plus", "", "next results", model.LoadMoreCommand, model.HasMore);
        Add("Refresh results", go, "Icon.Refresh", "", "watched scans", model.RefreshWatchedResultsCommand, model.HasDeferredWatchResults);
        Act("Next document", go, "Icon.ChevronRight", "J", "queue", () => window.MoveInQueue(1), model.HasDocuments && model.Documents[^1].Id != model.SelectedDocument?.Id);
        Act("Previous document", go, "Icon.ChevronLeft", "K", "queue", () => window.MoveInQueue(-1), model.HasDocuments && model.Documents[0].Id != model.SelectedDocument?.Id);

        Add("Add files…", add, "Icon.Upload", Mod + " O", "import open", model.AddFilesCommand);
        Add("Scan…", add, "Icon.Scan", Mod + " Shift S", "scanner paper feeder", model.ScanCommand);
        Add("Import a password-protected PDF…", add, "Icon.Lock", "", "password unlock", model.ImportProtectedPdfCommand);
        Add("Move from Paperless-ngx (folder)…", add, "Icon.Share", "", "migrate paperless", model.MigratePaperlessFolderCommand);
        Add("Move from Paperless-ngx (export)…", add, "Icon.Share", "", "migrate paperless zip", model.MigratePaperlessZipCommand);
        Add("Move from Evernote…", add, "Icon.Share", "", "migrate enex", model.MigrateEvernoteCommand);
        Add("Move from Mariner Paperless…", add, "Icon.Share", "", "migrate mariner", model.MigrateMarinerCommand);
        Add("Watch a folder…", add, "Icon.Watch", "", "folder watch scanner output", model.StartFolderWatchCommand, model.CanStartFolderWatch);
        Add("Stop watching", add, "Icon.Watch", "", "folder watch", model.StopFolderWatchCommand, model.IsWatchingFolder);
        Add("Resume folder watch", add, "Icon.Watch", "", "folder watch", model.ResumeFolderWatchCommand, model.CanOfferResumeFolderWatch);
        Add(model.ImportWatchFilesLabel, add, "Icon.Watch", "", "catch up", model.ImportWatchFolderFilesCommand, model.CanOfferImportWatchFiles);
        Add("Intake folder…", add, "Icon.Folder", "", "destination", model.ChooseIntakeFolderCommand);
        Add("Phone inbox folder…", add, "Icon.Phone", "", "phone mobile", model.ChoosePhoneInboxFolderCommand, !model.IsWatchingPhoneInbox);
        Add("Resume phone inbox", add, "Icon.Phone", "", "mobile watch", model.ResumePhoneInboxCommand, model.CanResumePhoneInbox);
        Add(model.PhoneCatchUpLabel, add, "Icon.Phone", "", "phone scans", model.CatchUpPhoneInboxCommand, model.CanCatchUpPhoneInbox);
        Add("Stop phone inbox", add, "Icon.Phone", "", "mobile watch", model.StopPhoneInboxCommand, model.IsWatchingPhoneInbox);

        if (model.ShowFilingCard)
        {
            Add("Edit pages…", document, "Icon.Pages", "", "reorder delete pages", model.EditPdfPagesCommand);
            Add("Rotate pages…", document, "Icon.RotateRight", "", "rotate turn", model.RotatePdfPagesCommand);
            Add("Extract pages…", document, "Icon.Split", "", "split", model.ExtractPdfPagesCommand);
            Add("Version history", document, "Icon.History", "", "versions restore", model.ShowPdfVersionsCommand);
            Add("Move to folder…", document, "Icon.Move", "", "file folder", model.MovePdfCommand);
            Add("Export PDF…", document, "Icon.Share", "", "copy save", model.ExportPdfCommand);
            Add("Export a redacted PDF…", document, "Icon.Redact", "", "redact black out", model.ExportRedactedPdfCommand);
            Add("Export a stamped PDF…", document, "Icon.Stamp", "", "stamp", model.ExportStampedPdfCommand);
            Add("Export a compressed PDF…", document, "Icon.Compress", "", "smaller compress", model.ExportCompressedPdfCommand);
            Act(model.SearchableCopyAction, document, "Icon.Recognize", "", "ocr searchable copy", () => model.CreateSearchableCopyCommand.Execute(null));
            Add("Filing suggestions…", document, "Icon.Check", "", "suggest tag date", model.SuggestFilingCommand);
            Add("Related documents", document, "Icon.Link", "", "similar", model.FindRelatedCommand);
            Add("Ask the local model for filing…", document, "Icon.Chat", "", "ai assistant", model.SuggestLocalFilingCommand, model.HasLocalChat);
            Add("Copy text", document, "Icon.Combine", "", "clipboard", model.CopyDocumentTextCommand, !model.HasNoSearchableText);
            Add(model.PageTextAction, document, "Icon.Text", "", "read text page", model.TogglePageTextCommand, !model.HasNoSearchableText);
            Add("Previous page", document, "Icon.ChevronLeft", "[", "page back", model.PreviousPageCommand, model.CanGoBack);
            Add("Next page", document, "Icon.ChevronRight", "]", "page forward", model.NextPageCommand, model.CanGoForward);
            Add(model.PageMatchActionLabel, document, "Icon.Search", "", "search match", model.GoToMatchCommand, model.HasPageMatchAction);
            Add("Previous match", document, "Icon.Search", "", "search match", model.PreviousMatchCommand, model.PageMatchNavigationActive);
            Add("Thumbnails", document, "Icon.Pages", "", "page preview", model.TogglePageThumbnailsCommand, model.HasMultiplePages && !model.IsPageTextView);
            Add(model.PreviewFitAction, document, "Icon.Fit", "", "zoom fit width", model.TogglePreviewFitCommand);
            Add("Zoom in", document, "Icon.Plus", "", "bigger", model.ZoomInCommand, model.HasPreview && model.CanZoomIn);
            Add("Zoom out", document, "Icon.Minus", "", "smaller", model.ZoomOutCommand, model.HasPreview && model.CanZoomOut);
            Add("Show in folder", document, "Icon.Folder", "", "reveal explorer finder", model.OpenDocumentFolderCommand);
            Add("Set the archive number…", document, "Icon.Tag", "", "asn serial", model.EditArchiveSerialCommand);
        }

        Act("Select all shown", selection, "Icon.Check", Mod + " A", "batch select", window.SelectAllShown, model.HasBatchCandidates);
        if (model.HasBatchSelection)
        {
            Act("Tag selected", selection, "Icon.Tag", "", "batch label", window.FocusBatchTagEntry);
            Act("Remove tag…", selection, "Icon.Tag", "", "batch remove label",
                () => _ = window.UpdateSelectedDocumentsAsync(static batch => batch.RemoveSelectedTagAsync()), model.HasTaggedBatchSelection);
            Act("Document date…", selection, "Icon.Calendar", "", "batch date",
                () => _ = window.UpdateSelectedDocumentsAsync(static batch => batch.EditBatchDocumentDatesAsync()));
            Act("Filing suggestions…", selection, "Icon.Check", "", "batch suggestions",
                () => _ = window.UpdateSelectedDocumentsAsync(static batch => batch.SuggestSelectedFilingAsync()));
            Add(model.BatchReviewAction, selection, "Icon.Check", "", "batch review", model.ReviewSelectedCommand);
            Add("Combine PDFs…", selection, "Icon.Combine", "", "merge", model.CombinePdfsCommand);
            Add("Move PDFs…", selection, "Icon.Move", "", "batch move", model.MoveSelectedPdfsCommand);
            Add("Export PDFs…", selection, "Icon.Share", "", "batch export", model.ExportSelectedPdfsCommand);
            Add("Export binder…", selection, "Icon.Binder", "", "household binder", model.ExportHouseholdBinderCommand);
            Add("Create OCR copies", selection, "Icon.Recognize", "", "batch ocr", model.CreateSelectedOcrCopiesCommand);
        }

        Add("Show PDF folder", library, "Icon.Folder", "", "reveal", model.OpenPdfFolderCommand);
        Add("Back up library…", library, "Icon.Binder", "", "backup copy", model.BackUpLibraryCommand);
        Add("Check library integrity", library, "Icon.Check", "", "verify", model.CheckIntegrityCommand);
        Add("Refresh index", library, "Icon.Refresh", "", "rescan reindex", model.RefreshIndexCommand);
        Add("Index report", library, "Icon.Info", "", "unavailable missing", model.ShowIndexReportCommand, model.ShowIndexReport);
        Add("Import errors", library, "Icon.Warning", "", "failed retry", model.InspectImportErrorsCommand, model.HasImportFailures);
        Add("Open backup", library, "Icon.Folder", "", "recent backup", model.OpenBackupFolderCommand, model.HasLatestBackup);
        Add("Open export", library, "Icon.Folder", "", "recent export", model.OpenExportFolderCommand, model.HasLatestExport);
        Add("OCR results", library, "Icon.Recognize", "", "batch ocr", model.InspectOcrCopyBatchResultsCommand, model.HasOcrCopyBatchResults);
        Add("Export library metadata (JSON)…", library, "Icon.Share", "", "json", model.ExportMetadataCommand);
        Add("Export search results (CSV)…", library, "Icon.Share", "", "csv spreadsheet", model.ExportSearchCsvCommand);
        Add("Tag rules…", library, "Icon.Tag", "", "filing rules", model.ManageFilingRulesCommand);
        Add("Metadata definitions…", library, "Icon.Sliders", "", "custom fields", model.ManageMetadataCommand);
        Add("Find an archive number…", library, "Icon.Search", "", "asn", model.FindArchiveSerialCommand);
        Add("Print archive label sheets…", library, "Icon.Tag", "", "asn labels", model.PrintArchiveLabelsCommand);
        Add("Print a separator sheet…", library, "Icon.Split", "", "patch separator", model.PrintSeparatorSheetCommand);
        Add("Local models…", library, "Icon.Chat", "", "ollama ai", model.ConfigureLocalModelsCommand);
        Add("Semantic search…", library, "Icon.Search", "", "meaning ai", model.SearchSemanticallyCommand, model.HasSemanticSearch);
        Add("Clear semantic index", library, "Icon.Refresh", "", "embedding index", model.RebuildSemanticIndexCommand, model.HasSemanticSearch);
        Add("Ask the local model…", library, "Icon.Chat", "", "chat question", model.AskLocalModelCommand, model.HasLocalChat);
        Add("Undo local filing", library, "Icon.History", "", "revert ai", model.UndoLocalFilingCommand, model.ShowFilingCard);
        Add("Appearance…", library, "Icon.Sliders", "", "theme dark light language", model.ShowAppearanceCommand);
        return commands;
    }

    /// <summary>
    /// Ranks commands by how well the query matches: whole-title prefix, then word prefix, then alias, then an
    /// in-order subsequence of the title. Scoring scans the command labels. Sorting m matches takes O(m log m)
    /// time and O(m) space.
    /// </summary>
    internal static IReadOnlyList<PaletteCommand> Filter(IReadOnlyList<PaletteCommand> commands, string query)
    {
        string needle = query.Trim().ToLowerInvariant();
        if (needle.Length == 0) return commands;
        return commands.Select((command, order) => (command, order, score: Score(command, needle)))
            .Where(match => match.score > 0)
            .OrderByDescending(match => match.score).ThenBy(match => match.order)
            .Select(match => match.command).ToArray();
    }

    private static int Score(PaletteCommand command, string needle)
    {
        string title = command.Title.ToLowerInvariant();
        if (title.StartsWith(needle, StringComparison.Ordinal)) return 400;
        if (title.Contains(" " + needle, StringComparison.Ordinal)) return 300;
        if (title.Contains(needle, StringComparison.Ordinal)) return 220;
        if ((" " + command.Aliases + " " + command.Group.ToLowerInvariant()).Contains(" " + needle, StringComparison.Ordinal)) return 160;
        int position = 0;
        foreach (char letter in needle)
        {
            position = title.IndexOf(letter, position);
            if (position < 0) return 0;
            position++;
        }
        return 60;
    }

    private static string T(string english) => DesktopText.Translate(english);
    private static Geometry? Icon(string key) => Avalonia.Application.Current?.FindResource(key) as Geometry;
    private static IReadOnlyList<string> Split(string keys) => keys.Length == 0 ? [] : keys.Split(' ');
}

/// <summary>The "?" sheet: every keyboard shortcut in two columns.</summary>
internal static class ShortcutSheet
{
    internal static void Fill(Grid grid)
    {
        string mod = PaletteCommands.Mod;
        AddGroup(grid, 0, DesktopText.Translate("Reviewing"),
        [
            ("J  K", "Next and previous document"), ("R", "Mark reviewed and open the next"), ("A", "Accept all suggestions"),
            ("T", "Add a tag"), ("D", "Set the document date"), ("E", "Rename"), ("X", "Move to Trash and open the next"),
            ("O", "Open in your PDF viewer"), ("[  ]", "Previous and next page")
        ]);
        AddGroup(grid, 1, DesktopText.Translate("Everywhere"),
        [
            (mod + " K", "Commands"), ("/", "Search"), (mod + " F", "Search"), (mod + " Z", "Undo"), (mod + " A", "Select all shown"),
            ("Esc", "Close"), (mod + " O", "Add files"), (mod + " Shift S", "Scan"), (mod + " ,", "Settings"), ("?", "Keyboard shortcuts")
        ]);
    }

    // Every label needs its own row in DesktopText.tsv: Translate falls back to English without a notice.
    private static void AddGroup(Grid grid, int column, string title, IReadOnlyList<(string Keys, string Label)> rows)
    {
        var panel = new StackPanel { Spacing = 0 };
        panel.Children.Add(new TextBlock { Text = title, Classes = { "meta" }, Margin = new(0, 6, 0, 8) });
        foreach (var (keys, label) in rows)
        {
            var row = new Grid { ColumnDefinitions = new("*,Auto"), ColumnSpacing = 12, MinHeight = 38 };
            var rule = new Border { BorderThickness = new(0, 1, 0, 0) };
            rule.Bind(Border.BorderBrushProperty, rule.GetResourceObservable("Line"));
            Grid.SetColumnSpan(rule, 2);
            row.Children.Add(rule);
            row.Children.Add(new TextBlock { Text = DesktopText.Translate(label), VerticalAlignment = VerticalAlignment.Center, FontSize = 14 });
            var caps = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, VerticalAlignment = VerticalAlignment.Center, [Grid.ColumnProperty] = 1 };
            foreach (string key in keys.Split(' ', StringSplitOptions.RemoveEmptyEntries))
                caps.Children.Add(new Border { Classes = { "key" }, Child = new TextBlock { Text = key } });
            row.Children.Add(caps);
            panel.Children.Add(row);
        }
        Grid.SetColumn(panel, column);
        grid.Children.Add(panel);
    }
}
