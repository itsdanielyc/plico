using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Interactivity;
using CommunityToolkit.Mvvm.Input;
using Plico.Core;

namespace Plico.Desktop;

internal static partial class NativeVerification
{
    private static async Task VerifyBooleanSearchAsync(Window window, InboxViewModel model, string evidence)
    {
        string query = $"({ReceiptKeyword} OR notaword) -tag:unassigned";
        var search = Control<TextBox>(window, "SearchBox");
        search.SetCurrentValue(TextBox.TextProperty, query);
        await UntilAsync(() => model.ResultCount == "1 document" && model.PreviewHighlights.Count > 0, () => model.Status);
        Require(model.SelectedDocument is { MatchHighlights.Count: > 0 } &&
            model.PreviewHighlights.All(hit => hit.Width > 0 && hit.Height > 0 && hit.X >= 0 && hit.Y >= 0),
            "Boolean search did not highlight the excerpt and visible PDF words.");
        await CaptureAsync(window, evidence, "boolean-search-highlights.png");
        search.SetCurrentValue(TextBox.TextProperty, ReceiptKeyword);
    }

    private static async Task VerifyArchivalExportAsync(Window window, InboxViewModel model, FixtureDialogs dialogs, string evidence)
    {
        string? previousFolder = dialogs.FolderPath;
        var destination = Directory.CreateTempSubdirectory("plico-verify-archive-export-");
        dialogs.FolderPath = destination.FullName;
        dialogs.PdfExportChoice = async language =>
        {
            var dialog = new PdfExportDialog(language);
            var completion = dialog.ShowDialog<PdfExportOptions?>(window);
            try
            {
                Control<ComboBox>(dialog, "PdfExportFormatBox").SelectedIndex = 1;
                await CaptureAsync(dialog, evidence, "pdf-export-formats.png");
                Control<Button>(dialog, "ConfirmPdfExportButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
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
            var export = (IAsyncRelayCommand)Control<Button>(window, "ExportPdfButton").Command!;
            await export.ExecuteAsync(null);
            string? path = Directory.EnumerateFiles(destination.FullName, "*.pdf").SingleOrDefault();
            Require(path is not null, $"The PDF/A export did not save a PDF. {model.Status}");
            File.Copy(path!, Path.Combine(evidence, "archival-receipt.pdf"), overwrite: true);
        }
        finally
        {
            dialogs.PdfExportChoice = null;
            dialogs.FolderPath = previousFolder;
            model.LatestExportLocation = null;
            destination.Delete(recursive: true);
        }
    }

    private static async Task VerifyFilingSuggestionsAsync(Window window, InboxViewModel model, FixtureDialogs dialogs, string evidence)
    {
        const string suggestionTag = "Verified filing rule";
        dialogs.RuleChoice = (_, save, _) =>
        {
            save(new FilingRule(null, suggestionTag, LetterKeyword, FilingRuleMatch.Phrase));
            return Task.CompletedTask;
        };
        try
        {
            await model.ManageFilingRulesCommand.ExecuteAsync(null);
        }
        finally
        {
            dialogs.RuleChoice = null;
        }
        Control<TextBox>(window, "SearchBox").SetCurrentValue(TextBox.TextProperty, LetterKeyword);
        await UntilAsync(() => model.SelectedDocument is { MatchHighlights.Count: > 0 } document &&
            document.Match.Contains(LetterKeyword, StringComparison.OrdinalIgnoreCase), () => model.Status);

        dialogs.FilingChoice = async suggestions =>
        {
            var suggestion = suggestions.Single();
            Require(suggestion.Tags.Contains(new FilingTag(suggestionTag, FilingTagSource.Rule)),
                "The rule did not match the PDF text.");
            var dialog = new FilingSuggestionsDialog(suggestions);
            var completion = dialog.ShowDialog<IReadOnlyList<FilingSelection>?>(window);
            try
            {
                var tag = Control<ItemsControl>(dialog, "SuggestedTagList").Items.OfType<FilingTagChoice>().Single(choice => choice.Tag == suggestionTag);
                tag.Selected = true;
                await CaptureAsync(dialog, evidence, "filing-suggestions.png");
                Control<Button>(dialog, "ApplyFilingSuggestionsButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
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
            var suggest = (IAsyncRelayCommand)Control<Button>(window, "SuggestFilingButton").Command!;
            await suggest.ExecuteAsync(null);
            Require(model.SelectedDocument?.Tags.Contains(suggestionTag) == true,
                $"Confirming the suggestion did not apply its tag. {model.Status}");
        }
        finally
        {
            dialogs.FilingChoice = null;
        }
    }

    private static async Task VerifyPortabilityAsync(Window window, InboxViewModel model, FixtureDialogs dialogs, string evidence)
    {
        const string unindexedName = "Unindexed for verification.pdf";
        string pdfFolder = Path.Combine(model.LibraryPath, "PDFs");
        string unindexed = Path.Combine(pdfFolder, unindexedName);
        var destination = Directory.CreateTempSubdirectory("plico-verify-metadata-");
        string? previousFolder = dialogs.FolderPath;
        string source = Path.Combine(pdfFolder, model.SelectedDocument!.RelativePath);
        File.Copy(source, unindexed);
        dialogs.IntegrityChoice = async report =>
        {
            Require(report.Issues.Contains(new IntegrityIssue(unindexedName, IntegrityIssueKind.Unindexed)),
                "The integrity check did not report the unindexed PDF.");
            var dialog = new IntegrityReportDialog(report);
            var completion = dialog.ShowDialog(window);
            try { await CaptureAsync(dialog, evidence, "library-integrity.png"); }
            finally
            {
                dialog.Close();
                await completion;
            }
        };
        try
        {
            var flyout = (MenuFlyout)Control<Button>(window, "LibraryToolsButton").Flyout!;
            flyout.ShowAt(Control<Button>(window, "LibraryToolsButton"));
            var items = flyout.Items.OfType<MenuItem>().ToArray();
            var commands = items.Skip(1).Take(3).Select(item => item.Command as IAsyncRelayCommand).ToArray();
            flyout.Hide();
            Require(commands.Length == 3 && commands.All(command => command is not null), "Library tools did not bind their commands.");
            await commands[0]!.ExecuteAsync(null);
            Require(model.Status == "Integrity check complete.", model.Status);
            dialogs.FolderPath = destination.FullName;
            await commands[1]!.ExecuteAsync(null);
            await commands[2]!.ExecuteAsync(null);
            using var json = JsonDocument.Parse(File.ReadAllText(Directory.EnumerateFiles(destination.FullName, "*.json").Single()));
            string csv = File.ReadAllText(Directory.EnumerateFiles(destination.FullName, "*.csv").Single());
            Require(json.RootElement.GetProperty("documents").GetArrayLength() >= model.Documents.Count &&
                csv.Contains(model.SelectedDocument.Title, StringComparison.Ordinal),
                "Metadata sidecar and search results did not include the selected PDF.");
        }
        finally
        {
            dialogs.IntegrityChoice = null;
            dialogs.FolderPath = previousFolder;
            File.Delete(unindexed);
            destination.Delete(recursive: true);
        }
    }
}
