using Avalonia;
using Avalonia.Controls;

namespace Plico.Desktop;

internal static partial class NativeVerification
{
    /// <summary>
    /// A search that cannot run says how to repair it in one line under the field and leaves the queue, the page and
    /// the card as they were; correcting it runs the search again.
    /// </summary>
    private static async Task VerifySearchQueryFeedbackAsync(Window window, InboxViewModel model, string directory)
    {
        string invalidQuery = $"{ReceiptKeyword} status:pending";
        var searchBox = Control<TextBox>(window, "SearchBox");
        var hint = Control<TextBlock>(window, "SearchQueryHint");
        var message = Control<TextBlock>(window, "EmptyResultsMessage");
        long? open = model.SelectedDocument?.Id;
        int shown = model.Documents.Count;
        Require(open is not null && shown > 0, "The search feedback check needs an open search result.");
        searchBox.SetCurrentValue(TextBox.TextProperty, invalidQuery);
        await UntilAsync(() => model.ResultCount != "Searching…" && hint.IsEffectivelyVisible, () => model.Status);
        Require(model.Documents.Count == shown && model.SelectedDocument?.Id == open && model.Preview is not null &&
                searchBox.Text == invalidQuery && !message.IsEffectivelyVisible &&
                hint.Text?.Contains("status:reviewed", StringComparison.Ordinal) == true &&
                hint.TranslatePoint(default, window) is { } line && searchBox.TranslatePoint(default, window) is { } field &&
                line.Y >= field.Y + searchBox.Bounds.Height - 1,
            "The malformed review status did not show its correction under the search field with the results kept.");
        await CaptureAsync(window, directory, "search-query-feedback.png");

        searchBox.SetCurrentValue(TextBox.TextProperty, ReceiptKeyword);
        await UntilAsync(() => model.Documents is [{ Reviewed: true }] && model.Preview is not null && !message.IsVisible &&
            !hint.IsEffectivelyVisible, () => model.Status);
    }
}
