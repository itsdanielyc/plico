using Avalonia.Controls;
using Avalonia.Interactivity;
using Plico.Core;

namespace Plico.Desktop;

internal static partial class NativeVerification
{
    private const string PagingFixtureFolder = "Paging check";

    private static async Task VerifyBatchTagPagingAsync(Window window, InboxViewModel model, string fixture, string evidenceDirectory)
    {
        const string appliedTag = "Page selection";
        const int extraDocuments = 2;
        const int documentCount = InboxLibrary.DefaultResultPageSize + extraDocuments;
        string folder = Directory.CreateDirectory(Path.Combine(model.LibraryPath, "PDFs", PagingFixtureFolder)).FullName;
        string source = Path.Combine(Path.GetDirectoryName(fixture)!, "Lease Renewal.pdf");
        for (int index = 1; index <= documentCount; index++)
        {
            File.Copy(source, Path.Combine(folder, $"Lease {index:D3}.pdf"));
        }

        await model.RefreshIndexCommand.ExecuteAsync(null);
        Control<TextBox>(window, "SearchBox").SetCurrentValue(TextBox.TextProperty, $"folder:\"{PagingFixtureFolder}\"");
        await UntilAsync(() => model.HasMore && model.Documents.Count == InboxLibrary.DefaultResultPageSize &&
            model.Documents.All(document => document.RelativePath.StartsWith($"{PagingFixtureFolder}/", StringComparison.Ordinal)), () => model.Status);
        model.LoadMoreCommand.Execute(null);
        Require(model.Documents.Count == documentCount && !model.HasMore, "The native result list did not load its second page.");

        Control<Button>(window, "SelectShownButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        var list = Control<ListBox>(window, "DocumentList");
        Require(list.Selection.Count == documentCount, "Select shown did not select the PDFs on both pages.");
        long[] shownIds = model.Documents.Select(document => document.Id).ToArray();

        Control<AutoCompleteBox>(window, "BatchTagBox").SetCurrentValue(AutoCompleteBox.TextProperty, appliedTag);
        Control<Button>(window, "ApplyBatchTagButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        await UntilAsync(() => !model.IsBusy && model.Status == $"Tag applied to {documentCount} documents.", () => model.Status);
        Require(list.Selection.Count == documentCount && shownIds.SequenceEqual(model.Documents.Select(document => document.Id)) &&
                model.Documents.All(document => document.Tags.Contains(appliedTag)),
            "Batch tagging dropped the loaded page or its selection.");
        await UntilAsync(() => model.Preview is not null, () => model.PreviewMessage);
        await CaptureAsync(window, evidenceDirectory, "batch-tag-paging.png");
    }
}
