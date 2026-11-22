using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Plico.Core;
using Plico.Desktop;
using Xunit;

namespace Plico.Tests;

/// <summary>
/// The filing card is edited where it is: the title in its heading, the date in its field, a tag with one Enter. Each
/// edit saves as it happens and is undone from its toast.
/// </summary>
public sealed partial class InboxWindowTests
{
    [AvaloniaFact]
    public async Task TheTitleIsEditedInPlaceAndSavesOnEnterOrLeavingAndRestoresOnEscape()
    {
        using var temporary = new TemporaryLibrary();
        using (var library = InboxLibrary.Open(temporary.Path)) BatchFixtures.AddTwoDocuments(library, TestContext.Current.CancellationToken);
        using var model = new InboxViewModel(new FixtureDialogs());
        model.InitializeLibrary(() => temporary.Path);
        var window = new MainWindow { DataContext = model, Width = 1280, Height = 800 };
        window.Show();
        try
        {
            await WaitForPreviewAsync(model);
            var start = Assert.IsType<Button>(window.FindControl<Button>("EditTitleButton"));
            var editor = Assert.IsType<TextBox>(window.FindControl<TextBox>("TitleEditBox"));
            long id = model.SelectedDocument!.Id;
            string original = model.SelectedDocument.Title;
            Assert.False(editor.IsVisible);
            Assert.True(start.IsEffectivelyVisible);
            Assert.Contains(start.GetVisualDescendants().OfType<Border>(),
                border => border.Classes.Contains("key") && border.Child is TextBlock { Text: "E" });

            // E turns the heading into a field with the title selected; Escape puts it back unchanged.
            PressWindowKey(window, Key.E, "e");
            Assert.True(editor.IsEffectivelyVisible);
            Assert.True(editor.IsFocused);
            Assert.Equal(original, editor.Text);
            Assert.Equal(original, editor.SelectedText);
            editor.SetCurrentValue(TextBox.TextProperty, "Abandoned title");
            PressFieldKey(editor, Key.Escape);
            Assert.False(editor.IsVisible);
            Assert.True(start.IsEffectivelyVisible);
            Assert.Equal(original, model.SelectedDocument!.Title);

            // A click on the heading opens it too, and Enter saves with a toast that offers Undo.
            start.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.True(editor.IsFocused);
            editor.SetCurrentValue(TextBox.TextProperty, "Lease renewal letter");
            PressFieldKey(editor, Key.Enter);
            Assert.False(editor.IsVisible);
            Assert.Equal("Lease renewal letter", model.SelectedDocument!.Title);
            Assert.Equal(("Renamed", "“Lease renewal letter”"), (model.ToastTitle, model.ToastBody));
            Assert.Equal("Renamed", model.UndoLabel);

            // Leaving the field saves as well, and focus stays where it went.
            start.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            editor.SetCurrentValue(TextBox.TextProperty, "Lease renewal");
            var review = Assert.IsType<Button>(window.FindControl<Button>("ReviewButton"));
            Assert.True(review.Focus());
            Assert.Equal("Lease renewal", model.SelectedDocument!.Title);
            Dispatcher.UIThread.RunJobs();
            Assert.False(editor.IsVisible);
            Assert.True(review.IsFocused);

            // An empty title is rolled back, and the message says why.
            start.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            editor.SetCurrentValue(TextBox.TextProperty, "   ");
            PressFieldKey(editor, Key.Enter);
            Assert.False(editor.IsVisible);
            Assert.Equal("Lease renewal", model.SelectedDocument!.Title);
            Assert.Equal("Couldn't rename", model.ToastTitle);
            Assert.Contains("Enter a title", model.ToastBody, StringComparison.Ordinal);

            // Nothing changed, so Undo still reverses the latest rename.
            await model.UndoCommand.ExecuteAsync(null);
            Assert.Equal("Lease renewal letter", model.Documents.Single(document => document.Id == id).Title);
            await WaitForPreviewAsync(model);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task TheDocumentDateIsTypedInPlaceWithItsSuggestionInsideAndACalendar()
    {
        using var temporary = new TemporaryLibrary();
        using (var library = InboxLibrary.Open(temporary.Path)) BatchFixtures.AddTwoDocuments(library, TestContext.Current.CancellationToken);
        // The April lease says when it was issued, so Plico suggests that date for it.
        var catalog = new LibraryCatalog(Path.Combine(temporary.Path, ".plico", "catalog.sqlite3"));
        catalog.Upsert("April lease.pdf", "April lease", new PdfText("Lease renewal\nInvoice date: 2026-04-03", 1, true),
            new FileVersion(1, 100, true), new byte[32]);
        using var model = new InboxViewModel(new FixtureDialogs());
        model.InitializeLibrary(() => temporary.Path);
        var window = new MainWindow { DataContext = model, Width = 1280, Height = 800 };
        window.Show();
        try
        {
            Assert.IsType<ListBox>(window.FindControl<ListBox>("DocumentList"))
                .SetCurrentValue(ListBox.SelectedItemProperty, model.Documents.Single(document => document.Title == "April lease"));
            await WaitForPreviewAsync(model);
            await WaitForAsync(() => model.HasSuggestedDate);
            var field = Assert.IsType<TextBox>(window.FindControl<TextBox>("DocumentDateField"));
            var suggestion = Assert.IsType<Button>(window.FindControl<Button>("SuggestedDateButton"));
            var error = Assert.IsType<TextBlock>(window.FindControl<TextBlock>("DocumentDateError"));
            Assert.True(field.IsEffectivelyVisible);
            Assert.True(string.IsNullOrEmpty(field.Text));
            // The dashed suggestion sits inside the empty field.
            Assert.True(suggestion.IsEffectivelyVisible);
            Assert.Contains(suggestion, field.GetVisualDescendants());
            Assert.Equal("3 Apr 2026", model.SuggestedDateLabel);

            suggestion.Command!.Execute(suggestion.CommandParameter);
            Assert.Equal(new DateOnly(2026, 4, 3), model.SelectedDocument!.DocumentDate);
            Assert.Equal("3 Apr 2026", field.Text);
            Assert.False(suggestion.IsEffectivelyVisible);

            // D puts the cursor in the field with its date selected; a date typed as it is shown saves on Enter.
            PressWindowKey(window, Key.D, "d");
            Assert.True(field.IsFocused);
            Assert.Equal("3 Apr 2026", field.SelectedText);
            field.SetCurrentValue(TextBox.TextProperty, "29 Feb 2024");
            PressFieldKey(field, Key.Enter);
            Assert.Equal(new DateOnly(2024, 2, 29), model.SelectedDocument!.DocumentDate);
            Assert.Equal("29 Feb 2024", field.Text);
            Assert.Equal(("Date set", "29 Feb 2024"), (model.ToastTitle, model.ToastBody));
            Assert.Equal("Date set", model.UndoLabel);

            // A date that does not exist stays in the field with one line saying what to type; Escape restores it.
            field.SetCurrentValue(TextBox.TextProperty, "31 Feb 2024");
            PressFieldKey(field, Key.Enter);
            Assert.Equal(new DateOnly(2024, 2, 29), model.SelectedDocument!.DocumentDate);
            Assert.True(error.IsEffectivelyVisible);
            Assert.StartsWith("Use a date like ", error.Text, StringComparison.Ordinal);
            PressFieldKey(field, Key.Escape);
            Assert.Equal("29 Feb 2024", field.Text);
            Assert.False(error.IsVisible);

            // Leaving an emptied field clears the date, and Undo puts it back.
            Assert.True(field.Focus());
            field.SetCurrentValue(TextBox.TextProperty, "");
            Assert.True(Assert.IsType<Button>(window.FindControl<Button>("ReviewButton")).Focus());
            Assert.Null(model.SelectedDocument!.DocumentDate);
            Assert.Equal(("Date cleared", "April lease"), (model.ToastTitle, model.ToastBody));
            await model.UndoCommand.ExecuteAsync(null);
            Assert.Equal(new DateOnly(2024, 2, 29), model.SelectedDocument!.DocumentDate);
            Assert.Equal("29 Feb 2024", field.Text);

            // The calendar picks a date, and the clear button inside the field removes it.
            Assert.IsType<Button>(window.FindControl<Button>("DocumentDateCalendarButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var picker = Assert.IsType<Popup>(window.FindControl<Popup>("DocumentDatePicker"));
            Assert.True(picker.IsOpen);
            var calendar = Assert.IsType<Calendar>(window.FindControl<Calendar>("DocumentDateCalendar"));
            Assert.Equal(new DateTime(2024, 2, 29), calendar.SelectedDate);
            calendar.SelectedDate = new DateTime(2025, 1, 15);
            Assert.False(picker.IsOpen);
            Assert.Equal(new DateOnly(2025, 1, 15), model.SelectedDocument!.DocumentDate);
            var clear = Assert.IsType<Button>(window.FindControl<Button>("ClearDocumentDateButton"));
            Assert.Contains(clear, field.GetVisualDescendants());
            clear.Command!.Execute(clear.CommandParameter);
            Assert.Null(model.SelectedDocument!.DocumentDate);
            Assert.True(string.IsNullOrEmpty(field.Text));
            await WaitForPreviewAsync(model);
        }
        finally
        {
            window.Close();
        }
    }

    [Theory]
    [InlineData(false, "21 Nov 2026", 2026, 11, 21)]
    [InlineData(false, "21 november 2026", 2026, 11, 21)]
    [InlineData(false, " 3 apr 2026 ", 2026, 4, 3)]
    [InlineData(false, "2024-02-29", 2024, 2, 29)]
    [InlineData(true, "21 nov. 2026", 2026, 11, 21)]
    [InlineData(true, "21 nov 2026", 2026, 11, 21)]
    [InlineData(true, "1 février 2024", 2024, 2, 1)]
    public void ADateIsReadAsTheInterfaceWritesIt(bool french, string text, int year, int month, int day)
    {
        DesktopText.SetLanguage(french ? InterfaceLanguage.French : InterfaceLanguage.English);
        try
        {
            Assert.True(DisplayDates.TryParseDay(text, out var parsed));
            Assert.Equal(new DateOnly(year, month, day), parsed);
            Assert.False(DisplayDates.TryParseDay("31 Feb 2024", out _));
            Assert.False(DisplayDates.TryParseDay("soon", out _));
        }
        finally
        {
            DesktopText.SetLanguage(InterfaceLanguage.English);
        }
    }

    /// <summary>A key pressed in a focused field, as a person typing there would.</summary>
    private static void PressFieldKey(Control field, Key key) => field.RaiseEvent(new KeyEventArgs
    {
        RoutedEvent = InputElement.KeyDownEvent,
        Key = key
    });
}
