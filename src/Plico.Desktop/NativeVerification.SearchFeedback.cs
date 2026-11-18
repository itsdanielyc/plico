using Avalonia.Controls;

namespace Plico.Desktop;

internal static partial class NativeVerification
{
    private static async Task VerifySearchQueryFeedbackAsync(Window window, InboxViewModel model, string directory)
    {
        string invalidQuery = $"{ReceiptKeyword} status:pending";
        var searchBox = Control<TextBox>(window, "SearchBox");
        var message = Control<TextBlock>(window, "EmptyResultsMessage");
        searchBox.SetCurrentValue(TextBox.TextProperty, invalidQuery);
        await UntilAsync(() => model.ResultCount != "Searching…", () => model.Status);
        Require(model.ResultCount == "Check search" && model.Documents.Count == 0 && searchBox.Text == invalidQuery &&
                message.IsVisible && message.Text?.Contains("status:reviewed", StringComparison.Ordinal) == true,
            "The malformed review status did not show a correction in the results pane.");
        await CaptureAsync(window, directory, "search-query-feedback.png");

        searchBox.SetCurrentValue(TextBox.TextProperty, ReceiptKeyword);
        await UntilAsync(() => model.Documents is [{ Reviewed: true }] && model.Preview is not null && !message.IsVisible,
            () => model.Status);
    }
}
