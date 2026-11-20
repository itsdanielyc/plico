using System.Windows.Input;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace Plico.Desktop;

/// <summary>One entry in the Ctrl+K / ⌘K command palette.</summary>
internal sealed record PaletteCommand(string Title, string Group, Geometry? Icon, IReadOnlyList<string> Keys, string Aliases, Action Run);

/// <summary>Builds and filters the command palette. Every tool in Plico is reachable here, with its shortcut.</summary>
internal static class PaletteCommands
{
    internal static string Mod => OperatingSystem.IsMacOS() ? "⌘" : "Ctrl";

    internal static IReadOnlyList<PaletteCommand> Build(MainWindow window, InboxViewModel model)
    {
        var commands = new List<PaletteCommand>(80);
        string review = T("Review"), go = T("Go to"), add = T("Add"), document = T("Document"), selection = T("Selection"), library = T("Library");

        void Add(string title, string group, string icon, string keys, string aliases, ICommand command, object? parameter = null)
        {
            if (command.CanExecute(parameter)) commands.Add(new(T(title), group, Icon(icon), Split(keys), aliases, () => command.Execute(parameter)));
        }
        void Act(string title, string group, string icon, string keys, string aliases, Action action, bool available = true)
        {
            if (available) commands.Add(new(T(title), group, Icon(icon), Split(keys), aliases, action));
        }

        if (model is { CanWork: true, HasSelection: true })
        {
            Act(model.SelectedIsReviewed ? "Return to Inbox" : "Mark reviewed", review, "Icon.Check", "R", "done file reviewed", window.ReviewWithMotion);
            Add("Accept all suggestions", review, "Icon.Check", "A", "suggestion accept", model.AcceptSuggestionsCommand);
            Act("Add a tag", review, "Icon.Tag", "T", "label", window.FocusTagEntry, model.ShowFilingCard);
            Add("Set the document date…", review, "Icon.Calendar", "D", "date dated", model.EditDocumentDateCommand);
            Add("Rename…", review, "Icon.Note", "E", "title edit", model.EditDocumentTitleCommand);
            Add("Move to Trash", review, "Icon.Trash", "X", "delete remove bin", model.TrashDocumentCommand);
            Add("Open in your PDF viewer", review, "Icon.Open", "O", "open pdf external", model.OpenPdfCommand);
            Add("Reminders and deadlines…", review, "Icon.Bell", "", "deadline due renewal expiry", model.EditDeadlinesCommand);
            Add("Custom fields…", review, "Icon.Sliders", "", "metadata structured", model.EditMetadataCommand);
        }
        Add("Undo", review, "Icon.History", Mod + " Z", "revert", model.UndoCommand);

        Add("Inbox", go, "Icon.AllDocuments", "", "queue review", model.ShowInboxCommand);
        Add("All documents", go, "Icon.AllDocuments", "", "library everything", model.ShowAllCommand);
        Add("Upcoming reminders…", go, "Icon.Upcoming", "", "due deadline", model.RemindersCommand);
        Add("Without text", go, "Icon.Recognize", "", "ocr missing scan", model.ShowWithoutTextCommand);
        Add("Trash…", go, "Icon.Trash", "", "restore deleted bin", model.OpenTrashCommand);
        Act("Search", go, "Icon.Search", "/", "find query", window.FocusSearchField);
        Add("Search a folder…", go, "Icon.Folder", "", "folder path", model.ChooseSearchFolderCommand);
        Add("Save search…", go, "Icon.Bookmark", "", "saved shortcut", model.SaveSearchCommand);

        Add("Add files…", add, "Icon.Upload", Mod + " O", "import open", model.AddFilesCommand);
        Add("Scan…", add, "Icon.Scan", Mod + " Shift S", "scanner paper feeder", model.ScanCommand);
        Add("Import a password-protected PDF…", add, "Icon.Lock", "", "password unlock", model.ImportProtectedPdfCommand);
        Add("Move from Paperless-ngx (folder)…", add, "Icon.Share", "", "migrate paperless", model.MigratePaperlessFolderCommand);
        Add("Move from Paperless-ngx (export)…", add, "Icon.Share", "", "migrate paperless zip", model.MigratePaperlessZipCommand);
        Add("Move from Evernote…", add, "Icon.Share", "", "migrate enex", model.MigrateEvernoteCommand);
        Add("Move from Mariner Paperless…", add, "Icon.Share", "", "migrate mariner", model.MigrateMarinerCommand);
        Add("Watch a folder…", add, "Icon.Watch", "", "folder watch scanner output", model.StartFolderWatchCommand);
        Add("Stop watching", add, "Icon.Watch", "", "folder watch", model.StopFolderWatchCommand);
        Add("Intake folder…", add, "Icon.Folder", "", "destination", model.ChooseIntakeFolderCommand);
        Add("Phone inbox folder…", add, "Icon.Phone", "", "phone mobile", model.ChoosePhoneInboxFolderCommand);

        if (model is { CanWork: true, HasSelection: true })
        {
            Add("Edit pages…", document, "Icon.Pages", "", "reorder delete pages", model.EditPdfPagesCommand);
            Add("Rotate pages…", document, "Icon.RotateRight", "", "rotate turn", model.RotatePdfPagesCommand);
            Add("Extract pages…", document, "Icon.Split", "", "split", model.ExtractPdfPagesCommand);
            Add("Version history…", document, "Icon.History", "", "versions restore", model.ShowPdfVersionsCommand);
            Add("Move to folder…", document, "Icon.Move", "", "file folder", model.MovePdfCommand);
            Add("Export PDF…", document, "Icon.Share", "", "copy save", model.ExportPdfCommand);
            Add("Export a redacted PDF…", document, "Icon.Redact", "", "redact black out", model.ExportRedactedPdfCommand);
            Add("Export a stamped PDF…", document, "Icon.Stamp", "", "stamp", model.ExportStampedPdfCommand);
            Add("Export a compressed PDF…", document, "Icon.Compress", "", "smaller compress", model.ExportCompressedPdfCommand);
            Act(model.SearchableCopyAction, document, "Icon.Recognize", "", "ocr searchable copy", () => model.CreateSearchableCopyCommand.Execute(null));
            Add("Filing suggestions…", document, "Icon.Check", "", "suggest tag date", model.SuggestFilingCommand);
            Add("Related documents…", document, "Icon.Link", "", "similar", model.FindRelatedCommand);
            Add("Ask the local model for filing…", document, "Icon.Chat", "", "ai assistant", model.SuggestLocalFilingCommand);
            Add("Copy text", document, "Icon.Combine", "", "clipboard", model.CopyDocumentTextCommand);
            Add(model.PageTextAction, document, "Icon.Text", "", "read text page", model.TogglePageTextCommand);
            Add(model.PreviewFitAction, document, "Icon.Fit", "", "zoom fit width", model.TogglePreviewFitCommand);
            Add("Zoom in", document, "Icon.Plus", "", "bigger", model.ZoomInCommand);
            Add("Zoom out", document, "Icon.Minus", "", "smaller", model.ZoomOutCommand);
            Add("Show in folder", document, "Icon.Folder", "", "reveal explorer finder", model.OpenDocumentFolderCommand);
            Add("Set the archive number…", document, "Icon.Tag", "", "asn serial", model.EditArchiveSerialCommand);
        }

        Act("Select all shown", selection, "Icon.Check", Mod + " A", "batch select", window.SelectAllShown, model.HasBatchCandidates);
        if (model.HasBatchSelection)
        {
            Add(model.BatchReviewAction, selection, "Icon.Check", "", "batch review", model.ReviewSelectedCommand);
            Add("Combine PDFs…", selection, "Icon.Combine", "", "merge", model.CombinePdfsCommand);
            Add("Move PDFs…", selection, "Icon.Move", "", "batch move", model.MoveSelectedPdfsCommand);
            Add("Export PDFs…", selection, "Icon.Share", "", "batch export", model.ExportSelectedPdfsCommand);
            Add("Export binder…", selection, "Icon.Binder", "", "household binder", model.ExportHouseholdBinderCommand);
            Add("Create OCR copies", selection, "Icon.Recognize", "", "batch ocr", model.CreateSelectedOcrCopiesCommand);
        }

        Add("Choose library folder…", library, "Icon.Folder", "", "switch open library", model.ChooseLibraryCommand);
        Add("Show PDF folder", library, "Icon.Folder", "", "reveal", model.OpenPdfFolderCommand);
        Add("Back up library…", library, "Icon.Binder", "", "backup copy", model.BackUpLibraryCommand);
        Add("Check library integrity", library, "Icon.Check", "", "verify", model.CheckIntegrityCommand);
        Add("Refresh index", library, "Icon.Refresh", "", "rescan reindex", model.RefreshIndexCommand);
        Add("Index report…", library, "Icon.Info", "", "unavailable missing", model.ShowIndexReportCommand);
        Add("Import errors…", library, "Icon.Warning", "", "failed retry", model.InspectImportErrorsCommand);
        Add("Export library metadata (JSON)…", library, "Icon.Share", "", "json", model.ExportMetadataCommand);
        Add("Export search results (CSV)…", library, "Icon.Share", "", "csv spreadsheet", model.ExportSearchCsvCommand);
        Add("Tag rules…", library, "Icon.Tag", "", "filing rules", model.ManageFilingRulesCommand);
        Add("Metadata definitions…", library, "Icon.Sliders", "", "custom fields", model.ManageMetadataCommand);
        Add("Find an archive number…", library, "Icon.Search", "", "asn", model.FindArchiveSerialCommand);
        Add("Print archive label sheets…", library, "Icon.Tag", "", "asn labels", model.PrintArchiveLabelsCommand);
        Add("Print a separator sheet…", library, "Icon.Split", "", "patch separator", model.PrintSeparatorSheetCommand);
        Add("Local models…", library, "Icon.Chat", "", "ollama ai", model.ConfigureLocalModelsCommand);
        Add("Semantic search…", library, "Icon.Search", "", "meaning ai", model.SearchSemanticallyCommand);
        Add("Ask the local model…", library, "Icon.Chat", "", "chat question", model.AskLocalModelCommand);
        Add("Undo local filing", library, "Icon.History", "", "revert ai", model.UndoLocalFilingCommand);
        Add("Appearance…", library, "Icon.Sliders", "", "theme dark light language", model.ShowAppearanceCommand);
        Act("Settings…", library, "Icon.Sliders", Mod + " ,", "preferences options", window.OpenSettings);
        Act("Keyboard shortcuts", library, "Icon.Keyboard", "?", "keys help", window.ShowShortcutSheet);
        return commands;
    }

    /// <summary>
    /// Ranks commands by how well the query matches: whole-title prefix, then word prefix, then alias, then an
    /// in-order subsequence of the title. O(c · q) for c commands and query length q.
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
            var row = new Grid { ColumnDefinitions = new("*,Auto"), MinHeight = 34 };
            var rule = new Border { BorderThickness = new(0, 1, 0, 0) };
            rule.Bind(Border.BorderBrushProperty, rule.GetResourceObservable("LineBrush"));
            Grid.SetColumnSpan(rule, 2);
            row.Children.Add(rule);
            row.Children.Add(new TextBlock { Text = DesktopText.Translate(label), VerticalAlignment = VerticalAlignment.Center, FontSize = 14 });
            var caps = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 3, VerticalAlignment = VerticalAlignment.Center, [Grid.ColumnProperty] = 1 };
            foreach (string key in keys.Split(' ', StringSplitOptions.RemoveEmptyEntries))
                caps.Children.Add(new Border { Classes = { "key" }, Child = new TextBlock { Text = key } });
            row.Children.Add(caps);
            panel.Children.Add(row);
        }
        Grid.SetColumn(panel, column);
        grid.Children.Add(panel);
    }
}
