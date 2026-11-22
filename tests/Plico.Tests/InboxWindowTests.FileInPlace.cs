using Avalonia;
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

            // Leaving the field saves as well, and focus stays where it went. The results reload once the input that
            // moved the keyboard has been handled.
            start.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            editor.SetCurrentValue(TextBox.TextProperty, "Lease renewal");
            var review = Assert.IsType<Button>(window.FindControl<Button>("ReviewButton"));
            Assert.True(review.Focus());
            Dispatcher.UIThread.RunJobs();
            Assert.Equal("Lease renewal", model.SelectedDocument!.Title);
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
            Dispatcher.UIThread.RunJobs();
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

    [Theory]
    [InlineData(false, "1 Jan 202")]
    [InlineData(false, "1 Jan 0202")]
    [InlineData(false, "29 Feb 20")]
    [InlineData(false, "5 6")]
    [InlineData(false, "Nov 2026")]
    [InlineData(false, "21 Nov")]
    [InlineData(false, "1.2")]
    [InlineData(false, "2024")]
    [InlineData(false, "31/12/99")]
    [InlineData(true, "5 6")]
    [InlineData(true, "1 janv. 202")]
    public void AnUnfinishedOrAmbiguousDateIsNotRead(bool french, string text)
    {
        DesktopText.SetLanguage(french ? InterfaceLanguage.French : InterfaceLanguage.English);
        try
        {
            Assert.False(DisplayDates.TryParseDay(text, out var read), $"“{text}” was read as {read:yyyy-MM-dd}.");
        }
        finally
        {
            DesktopText.SetLanguage(InterfaceLanguage.English);
        }
    }

    [AvaloniaFact]
    public async Task TheFrenchDateFieldKeepsItsPlaceholderWholeBesideItsKeycapAtTheMinimumWidth()
    {
        using var temporary = new TemporaryLibrary();
        using (var library = InboxLibrary.Open(temporary.Path)) BatchFixtures.AddTwoDocuments(library, TestContext.Current.CancellationToken);
        DesktopText.SetLanguage(InterfaceLanguage.French);
        using var model = new InboxViewModel(new FixtureDialogs());
        var window = new MainWindow { DataContext = model, Width = 900, Height = 590 };
        try
        {
            model.InitializeLibrary(() => temporary.Path);
            window.Show();
            await WaitForPreviewAsync(model);
            window.UpdateLayout();
            var field = Assert.IsType<TextBox>(window.FindControl<TextBox>("DocumentDateField"));
            var calendar = Assert.IsType<Button>(window.FindControl<Button>("DocumentDateCalendarButton"));
            var key = field.GetVisualDescendants().OfType<Border>().Single(border => border.Classes.Contains("key") && border.IsEffectivelyVisible);
            var words = new TextBlock { Text = field.PlaceholderText, FontSize = field.FontSize, FontWeight = field.FontWeight, FontFamily = field.FontFamily };
            words.Measure(Size.Infinity);
            double room = Bounds(key, window).Left - Bounds(calendar, window).Right - field.Padding.Left - field.Padding.Right;
            Assert.True(words.DesiredSize.Width <= room, $"“{field.PlaceholderText}” needs {words.DesiredSize.Width:0.#} px and has {room:0.#}.");
        }
        finally
        {
            window.Close();
            DesktopText.SetLanguage(InterfaceLanguage.English);
        }
    }

    [AvaloniaTheory]
    [InlineData(1280, false)]
    [InlineData(900, true)]
    public async Task TheCardsFieldsLineUpWithItsValuesAndShareOneKeycapColumn(double width, bool useFrench)
    {
        using var temporary = new TemporaryLibrary();
        using (var library = InboxLibrary.Open(temporary.Path)) BatchFixtures.AddTwoDocuments(library, TestContext.Current.CancellationToken);
        DesktopText.SetLanguage(useFrench ? InterfaceLanguage.French : InterfaceLanguage.English);
        using var model = new InboxViewModel(new FixtureDialogs());
        var window = new MainWindow { DataContext = model, Width = width, Height = 800 };
        try
        {
            model.InitializeLibrary(() => temporary.Path);
            window.Show();
            await WaitForPreviewAsync(model);
            window.UpdateLayout();
            var title = Assert.IsType<Button>(window.FindControl<Button>("EditTitleButton"));
            var dateField = Assert.IsType<TextBox>(window.FindControl<TextBox>("DocumentDateField"));
            var tagField = Assert.IsType<AutoCompleteBox>(window.FindControl<AutoCompleteBox>("TagBox"));
            var reminder = Assert.IsType<Button>(window.FindControl<Button>("EditDeadlinesButton"));

            // The empty date says so in Fg3 at full strength, like the contract's placeholders, not in a disabled grey.
            var placeholder = dateField.GetVisualDescendants().OfType<TextBlock>().Single(text => text.Name == "PART_Placeholder");
            Assert.True(placeholder.IsEffectivelyVisible);
            Assert.Equal(1, placeholder.Opacity);
            Assert.Equal(Avalonia.Media.Color.Parse("#66666D"), Assert.IsAssignableFrom<Avalonia.Media.ISolidColorBrush>(placeholder.Foreground).Color);

            // Its words and its glyph start where the other values' words and glyphs start.
            var reminderWords = BoundsIn(reminder.GetVisualDescendants().OfType<TextBlock>().First(text => text.IsEffectivelyVisible), window);
            var reminderGlyph = BoundsIn(reminder.GetVisualDescendants().OfType<Icon>().First(), window);
            var dateGlyph = BoundsIn(Assert.IsType<Button>(window.FindControl<Button>("DocumentDateCalendarButton")).GetVisualDescendants().OfType<Icon>().First(), window);
            Assert.True(Math.Abs(BoundsIn(placeholder, window).Left - reminderWords.Left) <= 1,
                $"The date's words start at {BoundsIn(placeholder, window).Left:0.#} and the reminder's at {reminderWords.Left:0.#}.");
            Assert.True(Math.Abs(dateGlyph.Left - reminderGlyph.Left) <= 1, $"The calendar glyph is at {dateGlyph.Left:0.#} and the bell at {reminderGlyph.Left:0.#}.");

            // E, D and T stand in one column at the card's right.
            double KeycapRight(Control owner) =>
                BoundsIn(owner.GetVisualDescendants().OfType<Border>().Single(border => border.Classes.Contains("key") && border.IsEffectivelyVisible), window).Right;
            double e = KeycapRight(title), d = KeycapRight(dateField), t = KeycapRight(tagField);
            Assert.True(Math.Abs(e - d) <= 1 && Math.Abs(d - t) <= 1, $"The keycaps end at E {e:0.#}, D {d:0.#} and T {t:0.#}.");

            // The date field is a 32 px control, and the title's field ends where the date's does.
            Assert.Equal(32, dateField.Bounds.Height);
            PressWindowKey(window, Key.E, "e");
            window.UpdateLayout();
            var editor = Assert.IsType<TextBox>(window.FindControl<TextBox>("TitleEditBox"));
            Assert.Equal(32, editor.Bounds.Height);
            Assert.True(Math.Abs(BoundsIn(editor, window).Right - BoundsIn(dateField, window).Right) <= 1,
                $"The title's field ends at {BoundsIn(editor, window).Right:0.#} and the date's at {BoundsIn(dateField, window).Right:0.#}.");
            PressFieldKey(editor, Key.Escape);
        }
        finally
        {
            window.Close();
            DesktopText.SetLanguage(InterfaceLanguage.English);
        }
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task TheCalendarSpeaksTheInterfacesLanguageStartsOnMondayAndRingsToday(bool useFrench)
    {
        using var temporary = new TemporaryLibrary();
        using (var library = InboxLibrary.Open(temporary.Path)) BatchFixtures.AddTwoDocuments(library, TestContext.Current.CancellationToken);
        DesktopText.SetLanguage(useFrench ? InterfaceLanguage.French : InterfaceLanguage.English);
        var systemCulture = System.Globalization.CultureInfo.CurrentCulture;
        using var model = new InboxViewModel(new FixtureDialogs());
        var window = new MainWindow { DataContext = model, Width = 1280, Height = 800 };
        try
        {
            model.InitializeLibrary(() => temporary.Path);
            window.Show();
            await WaitForPreviewAsync(model);
            var field = Assert.IsType<TextBox>(window.FindControl<TextBox>("DocumentDateField"));
            ClickAt(window, Assert.IsType<Button>(window.FindControl<Button>("DocumentDateCalendarButton")));
            var picker = Assert.IsType<Popup>(window.FindControl<Popup>("DocumentDatePicker"));
            Assert.True(picker.IsOpen);
            window.UpdateLayout();
            var calendar = Assert.IsType<Calendar>(window.FindControl<Calendar>("DocumentDateCalendar"));
            var culture = DisplayDates.Culture;
            var parts = calendar.GetVisualDescendants().OfType<Control>().ToArray();

            // The month and the days are named in the interface's language, and the week starts on Monday.
            var header = parts.OfType<Button>().Single(button => button.Name == "PART_HeaderButton");
            Assert.Contains(DateTime.Today.ToString("MMMM", culture), Assert.IsType<string>(header.Content), StringComparison.OrdinalIgnoreCase);
            var monthView = parts.OfType<Grid>().Single(grid => grid.Name == "PART_MonthView");
            var firstTitle = monthView.Children.OfType<TextBlock>().OrderBy(title => title.Bounds.X).First();
            Assert.Equal(culture.DateTimeFormat.ShortestDayNames[(int)DayOfWeek.Monday], firstTitle.Text);

            // Today wears a ring; the ink fill is kept for a chosen date, and none is chosen yet.
            var today = parts.OfType<CalendarDayButton>().Single(day => day.Classes.Contains(":today"));
            var todayFace = today.GetVisualDescendants().OfType<Border>().First(border => border.Name == "Root");
            Assert.True(todayFace.Background is null || todayFace.Background is Avalonia.Media.ISolidColorBrush { Color.A: 0 },
                $"Today is filled with {todayFace.Background}.");
            Assert.Contains(today.GetVisualDescendants().OfType<Border>(), border => border.BorderThickness.Left >= 1 &&
                border.BorderBrush is Avalonia.Media.ISolidColorBrush { Color: var ring } && ring == Avalonia.Media.Color.Parse("#0F0F10"));

            // The month chevrons are quiet 16 px glyphs, and the popover has room for its whole shadow.
            foreach (var name in new[] { "PART_PreviousButton", "PART_NextButton" })
            {
                var chevron = parts.OfType<Button>().Single(button => button.Name == name).GetVisualDescendants().OfType<Avalonia.Controls.Shapes.Path>().Single();
                Assert.True(chevron.Bounds.Width <= 12, $"The {name} glyph is {chevron.Bounds.Width} px wide.");
            }
            var popover = Assert.IsType<Border>(picker.Child);
            Assert.True(popover.Margin.Bottom >= 56 && popover.Margin.Left >= 32 && popover.Margin.Right >= 12,
                $"The popover leaves {popover.Margin} for a shadow that reaches 56 px below it, 32 px to its left and needs 12 at its right.");
            // It hangs just under the field, flush with its right edge, whole inside the window with its shadow.
            var box = BoundsIn(popover, window);
            Assert.InRange(box.Top - BoundsIn(field, window).Bottom, 2, 8);
            Assert.True(Math.Abs(box.Right - BoundsIn(field, window).Right) <= 1, $"The calendar ends at {box.Right:0.#} and the field at {BoundsIn(field, window).Right:0.#}.");
            Assert.True(box.Left - popover.Margin.Left >= 0 && box.Right <= window.Bounds.Width && box.Bottom + popover.Margin.Bottom <= window.Bounds.Height,
                $"The calendar {box} does not fit the window with its shadow.");

            picker.IsOpen = false;
            Assert.Equal(systemCulture, System.Globalization.CultureInfo.CurrentCulture);
        }
        finally
        {
            window.Close();
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
