using Avalonia.Controls;
using Plico.Core;

namespace Plico.Desktop;

internal static partial class NativeVerification
{
    private static async Task VerifyExternalSwapAsync(Window window, InboxViewModel model, FixtureDialogs dialogs, string fixture, string directory)
    {
        const string folderName = "Exchange check";
        const string firstName = "Alpha exchange.pdf";
        const string secondName = "Beta exchange.pdf";
        const string tag = "Exchange check";
        const string query = "folder:\"Exchange check\"";
        const string fixturePadding = "\n \n \n";
        string folder = Directory.CreateDirectory(Path.Combine(model.LibraryPath, "PDFs", folderName)).FullName;
        string firstPath = Path.Combine(folder, firstName);
        string secondPath = Path.Combine(folder, secondName);
        string fixtures = Path.GetDirectoryName(fixture)!;
        File.Copy(Path.Combine(fixtures, "Lease Renewal.pdf"), firstPath);
        File.Copy(Path.Combine(fixtures, "Scanned Receipt.pdf"), secondPath);
        // Distinguish these files from earlier imports of the same two test fixtures.
        File.AppendAllText(firstPath, fixturePadding);
        File.AppendAllText(secondPath, fixturePadding);
        await model.RefreshIndexCommand.ExecuteAsync(null);

        var search = Control<TextBox>(window, "SearchBox");
        search.SetCurrentValue(TextBox.TextProperty, query);
        await UntilAsync(() => model.Documents.Count == 2 &&
            model.Documents.All(document => document.RelativePath.StartsWith($"{folderName}/", StringComparison.Ordinal)), () => model.Status);
        var first = model.Documents.Single(document => document.RelativePath == $"{folderName}/{firstName}");
        var second = model.Documents.Single(document => document.RelativePath == $"{folderName}/{secondName}");
        Control<ListBox>(window, "DocumentList").SetCurrentValue(ListBox.SelectedItemProperty, first);
        await UntilAsync(() => model.SelectedDocument?.Id == first.Id && model.Preview is not null, () => model.PreviewMessage);
        Control<AutoCompleteBox>(window, "TagBox").SetCurrentValue(AutoCompleteBox.TextProperty, tag);
        Control<Button>(window, "AddTagButton").Command!.Execute(null);
        Require(model.SelectedDocument?.Tags.Contains(tag) == true, "The source PDF did not receive its tag.");

        string holdingPath = Path.Combine(folder, "held-for-exchange.tmp");
        File.Move(firstPath, holdingPath);
        File.Move(secondPath, firstPath);
        File.Move(holdingPath, secondPath);
        await model.RefreshIndexCommand.ExecuteAsync(null);
        await UntilAsync(() => model.SelectedDocument is { } current && current.Id == first.Id &&
            current.RelativePath == $"{folderName}/{secondName}" && model.Preview is not null, () => model.Status);
        Require(model.Status.Contains("2 documents moved.", StringComparison.Ordinal) && model.Query == query &&
                model.SelectedDocument?.Tags.Contains(tag) == true &&
                model.Documents.Any(document => document.Id == second.Id && document.RelativePath == $"{folderName}/{firstName}" &&
                    !document.HasText && !document.Tags.Contains(tag)),
            $"The catalog did not follow both PDFs through their exchanged names. {model.Status}");
        await CaptureAsync(window, directory, "swapped-pdf-paths.png");
        await VerifyIndexReportAsync(window, model, dialogs, directory, secondPath, model.SelectedDocument!);
    }
}
