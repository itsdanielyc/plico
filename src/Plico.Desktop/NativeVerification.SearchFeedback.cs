using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;

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

        // A search that runs and finds nothing is the family empty state in the queue, with Clear search.
        searchBox.SetCurrentValue(TextBox.TextProperty, "zzqx-nothing-matches");
        await UntilAsync(() => model.ResultCount != "Searching…" && model.Documents.Count == 0 && message.IsEffectivelyVisible &&
            model.EmptyTitle == "No documents match" && !hint.IsEffectivelyVisible, () => model.Status);
        await CaptureAsync(window, directory, "search-no-match.png");

        searchBox.SetCurrentValue(TextBox.TextProperty, ReceiptKeyword);
        await UntilAsync(() => model.Documents is [{ Reviewed: true }] && model.Preview is not null && !message.IsVisible &&
            !hint.IsEffectivelyVisible, () => model.Status);

        // Search tips is a flyout in its own popup, so its capture is the popup's own surface.
        var tips = Control<Button>(window, "SearchTipsButton");
        var flyout = (Flyout)tips.Flyout!;
        flyout.ShowAt(tips);
        var content = (Control)flyout.Content!;
        await UntilAsync(() => flyout.IsOpen && content.IsEffectivelyVisible && content.Bounds.Width > 0, () => "Search tips did not open.");
        await CaptureVisualAsync(TopLevel.GetTopLevel(content) ?? throw new InvalidOperationException("Search tips has no surface."),
            directory, "search-tips.png");
        flyout.Hide();
    }

    /// <summary>
    /// A search that cannot run in the narrow French window: the longest correction floats under the field, on one or
    /// two lines, over the sheet, and the queue, the page and the card stay where they were.
    /// </summary>
    private static async Task VerifyCompactSearchCorrectionAsync(MainWindow window, InboxViewModel model, string directory)
    {
        var searchBox = Control<TextBox>(window, "SearchBox");
        var hint = Control<TextBlock>(window, "SearchQueryHint");
        var review = Control<Button>(window, "ReviewButton");
        var before = review.TranslatePoint(default, window);
        searchBox.SetCurrentValue(TextBox.TextProperty, "bail type:");
        await UntilAsync(() => hint.IsEffectivelyVisible && model.Preview is not null, () => model.Status);
        Require(review.TranslatePoint(default, window) == before && model.SelectedDocument is not null,
            "The correction moved the card or closed the document.");
        await CaptureAsync(window, directory, "search-query-feedback-compact.png");
        searchBox.SetCurrentValue(TextBox.TextProperty, "");
        await UntilAsync(() => !hint.IsEffectivelyVisible && model.Preview is not null, () => model.Status);
    }

    /// <summary>
    /// The card's in-place editors as they look in the window's current theme and language: the title's field, the date
    /// field with the keyboard in it, and the date's calendar.
    /// </summary>
    private static async Task CaptureInPlaceEditorsAsync(MainWindow window, InboxViewModel model, string directory, string variant)
    {
        window.BeginTitleEdit();
        var title = Control<TextBox>(window, "TitleEditBox");
        await UntilAsync(() => title.IsEffectivelyVisible && title.IsFocused, () => "The title did not open for editing.");
        await CaptureAsync(window, directory, $"edit-title-{variant}.png");
        model.CancelTitleEdit();

        window.FocusDocumentDate();
        var field = Control<TextBox>(window, "DocumentDateField");
        await UntilAsync(() => field.IsFocused, () => "D did not put the keyboard in the date field.");
        await CaptureAsync(window, directory, $"document-date-{variant}.png");

        Control<Button>(window, "DocumentDateCalendarButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        var picker = Control<Popup>(window, "DocumentDatePicker");
        await UntilAsync(() => picker.IsOpen && Control<Calendar>(window, "DocumentDateCalendar").IsEffectivelyVisible,
            () => "The date field's calendar did not open.");
        await CaptureAsync(window, directory, $"document-date-calendar-{variant}.png");
        picker.IsOpen = false;
        Control<ListBox>(window, "DocumentList").Focus();
    }
}
