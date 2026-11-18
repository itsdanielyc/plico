using Avalonia.Controls;
using CommunityToolkit.Mvvm.Input;
using Plico.Core;

namespace Plico.Desktop;

internal static partial class NativeVerification
{
    private static async Task VerifyReviewPositionAsync(Window window, InboxViewModel model, string directory)
    {
        const int pageSize = InboxLibrary.DefaultResultPageSize;
        string query = $"folder:\"{PagingFixtureFolder}\" status:inbox";
        Control<TextBox>(window, "SearchBox").SetCurrentValue(TextBox.TextProperty, query);
        await UntilAsync(() => model.Documents.Count == pageSize && model.HasMore, () => model.Status);
        model.LoadMoreCommand.Execute(null);
        int loaded = model.Documents.Count;
        Require(loaded > pageSize + 1, "The review fixture did not load the second page.");
        var list = Control<ListBox>(window, "DocumentList");
        var next = model.Documents[pageSize + 1];
        var first = model.Documents[pageSize];
        list.SetCurrentValue(ListBox.SelectedItemProperty, first);
        Control<Button>(window, "ReviewButton").Command!.Execute(null);
        Require(model.Query == query && model.Documents.Count == loaded - 1 && model.SelectedDocument?.Id == next.Id &&
                model.Documents.All(document => document.Id != first.Id),
            "Single review did not retain the loaded queue and advance to its next PDF.");
        await UntilAsync(() => model.Preview is not null, () => model.PreviewMessage);
        await CaptureAsync(window, directory, "review-position.png");

        var preceding = model.Documents[^3];
        var adjacent = model.Documents[^2];
        list.SetCurrentValue(ListBox.SelectedItemProperty, preceding);
        list.SelectedItems = new List<DocumentSummary> { preceding, adjacent };
        var review = Control<Button>(window, "BatchReviewButton");
        long? focus = model.SelectedDocument?.Id;
        Require((focus == preceding.Id || focus == adjacent.Id) && model.HasBatchSelection &&
                review.IsEnabled && review.Command is IAsyncRelayCommand,
            "The review queue did not offer its batch action.");
        await ((IAsyncRelayCommand)review.Command!).ExecuteAsync(review.CommandParameter);
        Require(model.Query == query && model.Documents.Count == loaded - 3 && model.SelectedDocument?.Id == next.Id &&
                model.Documents.All(document => document.Id != preceding.Id && document.Id != adjacent.Id),
            "Batch review did not skip the removed PDFs and select the next result.");
    }
}
