using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Shapes;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.LogicalTree;
using Avalonia.VisualTree;
using Plico.Core;
using Plico.Desktop;
using Xunit;

namespace Plico.Tests;

public sealed partial class InboxWindowTests
{
    [AvaloniaFact]
    public async Task AnInvalidSearchSpeaksUnderTheFieldAndKeepsTheResultsAndTheOpenDocument()
    {
        const string invalidQuery = "northstar status:pending";
        var token = TestContext.Current.CancellationToken;
        using var temporary = new TemporaryLibrary();
        using (var library = InboxLibrary.Open(temporary.Path))
        {
            BatchFixtures.AddTwoDocuments(library, token);
        }

        using var model = new InboxViewModel(new FixtureDialogs());
        model.InitializeLibrary(() => temporary.Path);
        var window = new MainWindow { DataContext = model };
        window.Show();
        try
        {
            await WaitForPreviewAsync(model);
            var open = Assert.IsType<DocumentSummary>(model.SelectedDocument);
            var preview = model.Preview;
            var searchBox = Assert.IsType<TextBox>(window.FindControl<TextBox>("SearchBox"));
            var message = Assert.IsType<TextBlock>(window.FindControl<TextBlock>("EmptyResultsMessage"));
            var workspace = Workspace(window);

            searchBox.SetCurrentValue(TextBox.TextProperty, invalidQuery);
            await WaitForAsync(() => model.HasSearchQueryHint);
            window.UpdateLayout();

            // The queue, the page and the card stay as they were, and the queue does not turn into a message.
            Assert.Equal(invalidQuery, searchBox.Text);
            Assert.Equal(2, model.Documents.Count);
            Assert.Equal(open.Id, model.SelectedDocument?.Id);
            Assert.Same(preview, model.Preview);
            Assert.True(model.ShowFilingCard);
            Assert.False(message.IsEffectivelyVisible);
            Assert.Equal("2 documents", model.ResultCount);
            Assert.False(model.HasMore);
            // One line under the field says how to repair the search, and the field shows it is the one to fix.
            var hint = Assert.IsType<TextBlock>(window.FindControl<TextBlock>("SearchQueryHint"));
            Assert.True(hint.IsEffectivelyVisible);
            Assert.Equal("Use one review status: status:inbox or status:reviewed.", hint.Text);
            Assert.Contains("invalid", searchBox.Classes);
            // The line floats over the sheet: the toolbar keeps its height, and the queue, the page and the card stay put.
            Assert.Equal(workspace, Workspace(window));
            AssertHintSitsUnderTheField(window, hint, searchBox);

            // A narrow window keeps the line under the field, never in the 84 px strip, and keeps the page and the card.
            window.Width = window.MinWidth;
            await WaitForAsync(() => Math.Abs(window.Bounds.Width - window.MinWidth) < 1);
            window.UpdateLayout();
            Assert.Contains("compact", Assert.IsType<Grid>(window.FindControl<Grid>("WorkspaceGrid")).Classes);
            AssertHintSitsUnderTheField(window, hint, searchBox);
            Assert.True(hint.Bounds.Width > 240, $"The hint is {hint.Bounds.Width} px wide.");
            var narrow = Workspace(window);
            model.Query = "northstar";
            await WaitForAsync(() => !model.HasSearchQueryHint && model.ResultCount != "Searching…");
            Assert.Equal(narrow, Workspace(window));
            model.Query = invalidQuery;
            await WaitForAsync(() => model.HasSearchQueryHint);
            Assert.True(Assert.IsType<Border>(window.FindControl<Border>("PreviewPaper")).IsEffectivelyVisible);
            Assert.True(Assert.IsType<Button>(window.FindControl<Button>("ReviewButton")).IsEffectivelyVisible);
            window.Width = 1280;

            searchBox.SetCurrentValue(TextBox.TextProperty, "northstar status:reviewed");
            Assert.Equal("", model.EmptyMessage);
            await WaitForAsync(() => model.ResultCount != "Searching…");
            window.UpdateLayout();
            Assert.False(hint.IsEffectivelyVisible);
            Assert.DoesNotContain("invalid", searchBox.Classes);
            Assert.Equal("0 documents", model.ResultCount);
            // The empty queue says what is true in its title and offers a way forward in its one sentence.
            Assert.Equal("No documents match", model.EmptyTitle);
            Assert.Equal("Try other words or a different filter.", message.Text);
            Assert.True(message.IsEffectivelyVisible);
            Assert.True(model.ClearSearchCommand.CanExecute(null));

            searchBox.SetCurrentValue(TextBox.TextProperty, "northstar status:inbox");
            await WaitForAsync(() => model.Documents.Count == 2);
            Assert.Equal("2 documents", model.ResultCount);
            Assert.False(message.IsVisible);
            Assert.All(model.Documents, document => Assert.False(document.Reviewed));
        }
        finally
        {
            if (model.SelectedDocument is not null) await WaitForPreviewAsync(model);
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task AHalfTypedFilterShowsNoCorrectionUntilTypingPausesOrEnterIsPressed()
    {
        using var temporary = new TemporaryLibrary();
        using (var library = InboxLibrary.Open(temporary.Path)) BatchFixtures.AddTwoDocuments(library, TestContext.Current.CancellationToken);
        using var model = new InboxViewModel(new FixtureDialogs());
        model.InitializeLibrary(() => temporary.Path);
        var window = new MainWindow { DataContext = model, Width = 1280, Height = 800 };
        window.Show();
        bool flashed = false;
        model.PropertyChanged += (_, change) =>
        {
            if (change.PropertyName == nameof(InboxViewModel.HasSearchQueryHint) && model.HasSearchQueryHint) flashed = true;
        };
        try
        {
            await WaitForPreviewAsync(model);
            var search = Assert.IsType<TextBox>(window.FindControl<TextBox>("SearchBox"));
            Assert.True(search.Focus());

            // "status:" cannot run on its own. The search tries it during a short pause, and the next keys complete it.
            TypeText(window, "status:");
            await WaitForAsync(() => model.ResultCount != "Searching…");
            TypeText(window, "inbox");
            await WaitForAsync(() => model.ResultCount == "2 documents" && model.Heading == "Search results");
            Assert.False(flashed, "The correction flashed while the filter was being typed.");
            Assert.DoesNotContain("invalid", search.Classes);

            // Enter asks for the search now, so a query that cannot run says why at once.
            model.QueryHintDelay = TimeSpan.FromHours(1);
            search.SelectAll();
            TypeText(window, "status:pending");
            PressKey(window, Key.Enter, PhysicalKey.Enter);
            await WaitForAsync(() => model.HasSearchQueryHint);
            Assert.Contains("invalid", search.Classes);
            Assert.Equal(2, model.Documents.Count);
        }
        finally
        {
            if (model.SelectedDocument is not null) await WaitForPreviewAsync(model);
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task WhileASearchCannotRunEveryChangeToTheOpenDocumentStillShows()
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
            var search = Assert.IsType<TextBox>(window.FindControl<TextBox>("SearchBox"));
            Assert.True(search.Focus());
            TypeText(window, "status:pending");
            await WaitForAsync(() => model.HasSearchQueryHint);
            var first = model.SelectedDocument!;

            // A tag typed with one Enter shows as a chip on the card at once.
            var tagField = Assert.IsType<AutoCompleteBox>(window.FindControl<AutoCompleteBox>("TagBox"));
            ClickAt(window, tagField);
            TypeText(window, "Housing");
            PressKey(window, Key.Enter, PhysicalKey.Enter);
            Assert.Contains(model.SelectedTagChips, chip => chip.Label == "Housing");

            // A date typed and entered stays in the field and on the document.
            var dateField = Assert.IsType<TextBox>(window.FindControl<TextBox>("DocumentDateField"));
            ClickAt(window, dateField);
            TypeText(window, "29 Feb 2024");
            PressKey(window, Key.Enter, PhysicalKey.Enter);
            Assert.Equal(new DateOnly(2024, 2, 29), model.SelectedDocument!.DocumentDate);
            Assert.Equal("29 Feb 2024", dateField.Text);

            // Mark reviewed takes the document out of the Inbox the queue shows and opens the next one.
            ClickAt(window, Assert.IsType<Button>(window.FindControl<Button>("ReviewButton")));
            await WaitForPreviewAsync(model);
            Assert.DoesNotContain(model.Documents, document => document.Id == first.Id);
            var second = Assert.IsType<DocumentSummary>(model.SelectedDocument);
            Assert.NotEqual(first.Id, second.Id);
            Assert.Equal(1, model.InboxCount);

            // Trash takes it out of the queue and off the card.
            ClickAt(window, Assert.IsType<Button>(window.FindControl<Button>("TrashDocumentButton")));
            await WaitForAsync(() => !model.IsBusy);
            Assert.DoesNotContain(model.Documents, document => document.Id == second.Id);
            Assert.NotEqual(second.Id, model.SelectedDocument?.Id);

            // The search still says how to repair it, with what was typed.
            Assert.Equal("status:pending", search.Text);
            Assert.True(model.HasSearchQueryHint);
        }
        finally
        {
            if (model.SelectedDocument is not null) await WaitForPreviewAsync(model);
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task AnEmptySearchIsTheFamilyEmptyStateAtEveryWidth()
    {
        using var temporary = new TemporaryLibrary();
        using (var library = InboxLibrary.Open(temporary.Path))
        {
            BatchFixtures.AddTwoDocuments(library, TestContext.Current.CancellationToken);
        }
        using var model = new InboxViewModel(new FixtureDialogs());
        model.InitializeLibrary(() => temporary.Path);
        var window = new MainWindow { DataContext = model };
        window.Show();
        try
        {
            await WaitForPreviewAsync(model);
            model.Query = "nothing-matches-this";
            await WaitForAsync(() => model.ResultCount == "0 documents");
            var message = Assert.IsType<TextBlock>(window.FindControl<TextBlock>("EmptyResultsMessage"));
            foreach (double width in new[] { window.MinWidth, 1280 })
            {
                window.Width = width;
                await WaitForAsync(() => Math.Abs(window.Bounds.Width - width) < 1);
                window.UpdateLayout();
                Assert.True(message.IsEffectivelyVisible);
                Assert.True(message.Bounds.Width > 160, $"At {width}px the sentence is {message.Bounds.Width} px wide.");
                var empty = Assert.IsType<Panel>(window.FindControl<Panel>("QueueEmptyState"));
                // A dashed outline, the graphite tile, a title, one sentence and Clear search, never squeezed into the strip.
                var parts = empty.GetVisualDescendants().OfType<Control>().Where(part => part.IsEffectivelyVisible).ToArray();
                Assert.Contains(parts, part => part is Rectangle frame && frame.Classes.Contains("dashed-frame") && frame.Bounds.Width > 200);
                Assert.Contains(parts, part => part is Border tile && tile.Classes.Contains("empty-tile"));
                Assert.Contains(parts, part => part is TextBlock { Text: "No documents match" });
                Assert.Contains(parts, part => part is Button { Content: "Clear search" });
            }
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void SearchTipsOpenFromAHelpButtonInTheInterfaceLanguage(bool useFrench)
    {
        using var temporary = new TemporaryLibrary();
        DesktopText.SetLanguage(useFrench ? InterfaceLanguage.French : InterfaceLanguage.English);
        using var model = new InboxViewModel(new FixtureDialogs());
        var window = new MainWindow { DataContext = model };
        try
        {
            model.InitializeLibrary(() => temporary.Path);
            window.Show();
            var search = Assert.IsType<TextBox>(window.FindControl<TextBox>("SearchBox"));
            Assert.Equal(useFrench ? "Chercher dans vos papiers" : "Search your paperwork", search.PlaceholderText);
            // The syntax no longer hides in an English tooltip on the field.
            Assert.Null(ToolTip.GetTip(search));

            var tips = Assert.IsType<Button>(window.FindControl<Button>("SearchTipsButton"));
            string title = useFrench ? "Astuces de recherche" : "Search tips";
            Assert.Equal(title, Avalonia.Automation.AutomationProperties.GetName(tips));
            Assert.Equal(title, ToolTip.GetTip(tips));
            var flyout = Assert.IsType<Flyout>(tips.Flyout);
            var content = Assert.IsAssignableFrom<Control>(flyout.Content);
            var texts = content.GetLogicalDescendants().OfType<TextBlock>().Select(text => text.Text ?? "").ToArray();
            Assert.Contains(title, texts);
            foreach (string example in new[] { "status:inbox", "text:missing", "dated:2024-02", "added:2026-11", "folder:\"Filed/Property\"", "tag:tax" })
                Assert.Contains(texts, text => text.Contains(example, StringComparison.Ordinal));
            Assert.Contains(useFrench ? "La date sur le papier" : "The date on the paper", texts);
            Assert.True(texts.Length >= 8, $"Only {texts.Length} lines of tips.");
        }
        finally
        {
            window.Close();
            DesktopText.SetLanguage(InterfaceLanguage.English);
        }
    }

    /// <summary>
    /// The correction sits just under the field: its words start where the typed query starts, and its glyph sits under
    /// the field's search glyph.
    /// </summary>
    private static void AssertHintSitsUnderTheField(Window window, TextBlock hint, TextBox field)
    {
        var line = BoundsIn(hint, window);
        var box = BoundsIn(field, window);
        var typed = BoundsIn(field.GetVisualDescendants().OfType<TextPresenter>().Single(), window);
        double glyphCentre = BoundsIn(Assert.IsAssignableFrom<Control>(field.InnerLeftContent), window).Center.X;
        var alert = BoundsIn(Assert.IsType<Grid>(hint.Parent).Children.OfType<Control>().First(), window);
        Assert.True(hint.IsEffectivelyVisible);
        Assert.InRange(line.Top - box.Bottom, 0, 16);
        Assert.True(Math.Abs(line.Left - typed.Left) <= 1, $"The correction starts at {line.Left:0.#} and the query at {typed.Left:0.#}.");
        Assert.True(Math.Abs(alert.Center.X - glyphCentre) <= 1.5, $"The alert glyph is centred at {alert.Center.X:0.#} and the search glyph at {glyphCentre:0.#}.");
    }

    /// <summary>Where the toolbar, the queue, the page and the card are: none of them moves for a correction.</summary>
    private static (Rect Toolbar, Rect Queue, Rect Page, Rect Card) Workspace(Window window)
    {
        window.UpdateLayout();
        return (BoundsIn(Assert.IsType<Border>(window.FindControl<Border>("Toolbar")), window),
            BoundsIn(Assert.IsType<Border>(window.FindControl<Border>("QueueHeader")), window),
            BoundsIn(Assert.IsType<Border>(window.FindControl<Border>("PreviewPaper")), window),
            BoundsIn(Assert.IsType<Button>(window.FindControl<Button>("ReviewButton")), window));
    }
}
