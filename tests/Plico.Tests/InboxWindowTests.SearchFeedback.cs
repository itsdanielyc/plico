using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Headless.XUnit;
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
            var toolbar = Assert.IsType<Border>(window.FindControl<Border>("Toolbar"));

            searchBox.SetCurrentValue(TextBox.TextProperty, invalidQuery);
            await WaitForAsync(() => model.ResultCount != "Searching…");
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
            AssertHintSitsUnderTheField(window, hint, searchBox, toolbar);

            // A narrow window keeps the line under the field, never in the 84 px strip, and keeps the page and the card.
            window.Width = window.MinWidth;
            await WaitForAsync(() => Math.Abs(window.Bounds.Width - window.MinWidth) < 1);
            window.UpdateLayout();
            Assert.Contains("compact", Assert.IsType<Grid>(window.FindControl<Grid>("WorkspaceGrid")).Classes);
            AssertHintSitsUnderTheField(window, hint, searchBox, toolbar);
            Assert.True(hint.Bounds.Width > 240, $"The hint is {hint.Bounds.Width} px wide.");
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

    private static void AssertHintSitsUnderTheField(Window window, TextBlock hint, TextBox field, Border toolbar)
    {
        var line = BoundsIn(hint, window);
        var box = BoundsIn(field, window);
        var bar = BoundsIn(toolbar, window);
        Assert.True(hint.IsEffectivelyVisible);
        Assert.True(line.Top >= box.Bottom - 1, $"The hint {line} is not under the field {box}.");
        Assert.InRange(line.Left, box.Left - 1, box.Left + 40);
        Assert.True(bar.Contains(line), $"The hint {line} is outside the toolbar {bar}.");
    }
}
