using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.XUnit;
using Avalonia.Layout;
using Avalonia.VisualTree;
using Plico.Core;
using Plico.Desktop;
using Xunit;

namespace Plico.Tests;

public sealed partial class InboxWindowTests
{
    [AvaloniaFact]
    public async Task EveryScrollerInTheShellHidesItsBarUntilThePointerIsOverIt()
    {
        using var temporary = new TemporaryLibrary();
        using (var library = InboxLibrary.Open(temporary.Path))
        {
            BatchFixtures.AddTwoDocuments(library, TestContext.Current.CancellationToken);
        }
        using var model = new InboxViewModel(new FixtureDialogs());
        model.InitializeLibrary(() => temporary.Path);
        var window = new MainWindow { DataContext = model, Width = 1280, Height = 800 };
        window.Show();
        try
        {
            await WaitForPreviewAsync(model);
            window.UpdateLayout();
            // The card scrolls at this height, so its bar is in play; like the sidebar's, it is drawn only under the pointer.
            var card = Assert.IsType<ScrollViewer>(window.FindControl<ScrollViewer>("DocumentActionsScroll"));
            Assert.True(card.Extent.Height > card.Viewport.Height, "The card does not scroll at 1280 × 800.");
            AssertBarsWaitForThePointer("the filing card", card);
            AssertBarsWaitForThePointer("the queue", Assert.IsType<ListBox>(window.FindControl<ListBox>("DocumentList")));

            var settings = OpenSettingsAt(window, "IntakeGroup");
            try
            {
                foreach (var scroller in settings.GetVisualDescendants().OfType<ScrollViewer>())
                    AssertBarsWaitForThePointer($"Settings ({scroller.Name ?? "navigation"})", scroller);
            }
            finally { settings.Close(); }
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task TheQueueActionsShareOneRowWhenTheyFit()
    {
        using var temporary = new TemporaryLibrary();
        using (var library = InboxLibrary.Open(temporary.Path))
        {
            BatchFixtures.AddTwoDocuments(library, TestContext.Current.CancellationToken);
        }
        using var model = new InboxViewModel(new FixtureDialogs());
        model.InitializeLibrary(() => temporary.Path);
        var window = new MainWindow { DataContext = model, Width = 1280, Height = 800 };
        window.Show();
        try
        {
            await WaitForPreviewAsync(model);
            model.Query = "northstar";
            await WaitForAsync(() => model.ResultCount == "2 documents");
            await WaitForPreviewAsync(model);
            var header = Assert.IsType<Border>(window.FindControl<Border>("QueueHeader"));
            var save = Assert.IsType<Button>(window.FindControl<Button>("SaveSearchButton"));
            var select = Assert.IsType<Button>(window.FindControl<Button>("SelectShownButton"));
            foreach (double width in new[] { 1280, 1180 })
            {
                window.Width = width;
                await WaitForAsync(() => Math.Abs(window.Bounds.Width - width) < 1);
                window.UpdateLayout();
                Assert.True(save.IsEffectivelyVisible && select.IsEffectivelyVisible);
                var saveBounds = BoundsIn(save, header);
                var selectBounds = BoundsIn(select, header);
                Assert.True(Math.Abs(saveBounds.Y - selectBounds.Y) < 1,
                    $"At {width}px Save search is at {saveBounds} and Select shown at {selectBounds}: two rows.");
                Assert.True(selectBounds.Right <= header.Bounds.Width + 0.5);
                Assert.Equal(model.SelectShownAction, select.Content);
            }
        }
        finally
        {
            if (model.SelectedDocument is not null) await WaitForPreviewAsync(model);
            window.Close();
        }
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MatchButtonsSitOnTheViewerBarAsChevronsNamedByTheirTooltips(bool useFrench)
    {
        using var temporary = new TemporaryLibrary();
        DesktopText.SetLanguage(useFrench ? InterfaceLanguage.French : InterfaceLanguage.English);
        using var model = new InboxViewModel(new FixtureDialogs(RedesignFixture("Match Ledger.pdf")));
        var window = new MainWindow { DataContext = model };
        try
        {
            model.InitializeLibrary(() => temporary.Path);
            window.Show();
            await model.AddFilesCommand.ExecuteAsync(null);
            model.Query = "ledger renewal";
            await WaitForAsync(() => model.HasPageMatchAction);
            await WaitForPreviewAsync(model);
            var previous = Assert.IsType<Button>(window.FindControl<Button>("PreviousMatchButton"));
            var next = Assert.IsType<Button>(window.FindControl<Button>("GoToMatchButton"));
            var fit = Assert.IsType<Button>(window.FindControl<Button>("PreviewFitButton"));

            // Both chevrons hold their places from the start: before the first jump Previous is there but cannot act,
            // so a second click where the first one landed goes forward again, never back.
            window.UpdateLayout();
            var firstNext = BoundsIn(next, window);
            Assert.True(previous.IsEffectivelyVisible, "Previous match is missing before the first jump.");
            Assert.False(previous.IsEffectivelyEnabled);
            ClickAt(window, next);
            await WaitForAsync(() => model.PageMatchNavigationActive && !model.IsBusy);
            await WaitForPreviewAsync(model);
            window.UpdateLayout();
            // Within half a pixel: a pressed button eases back from its pressed scale.
            var afterNext = BoundsIn(next, window);
            Assert.True(Math.Abs(afterNext.X - firstNext.X) < 0.5 && Math.Abs(afterNext.Y - firstNext.Y) < 0.5,
                $"Next match moved from {firstNext} to {afterNext} after the first jump.");
            Assert.True(previous.IsEffectivelyEnabled);
            // The chevrons are quiet like the viewer bar's other glyphs, not ink carets (with the pointer elsewhere).
            Avalonia.Headless.HeadlessWindowExtensions.MouseMove(window, new Point(2, 2));
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            foreach (var button in new[] { previous, next })
            {
                var glyph = button.GetVisualDescendants().OfType<Icon>().Single();
                Assert.Equal(Avalonia.Media.Color.Parse("#45454B"), Assert.IsAssignableFrom<Avalonia.Media.ISolidColorBrush>(glyph.Foreground).Color);
                Assert.True(glyph.Bounds.Width >= 18, $"The {button.Name} glyph is {glyph.Bounds.Width} px.");
            }

            // The words stay the buttons' content and tooltips, so tests, the palette and screen readers keep them.
            Assert.Equal(DesktopText.Translate("Next match"), next.Content);
            Assert.Equal(DesktopText.Translate("Next match"), ToolTip.GetTip(next));
            Assert.Equal(DesktopText.Translate("Previous match"), ToolTip.GetTip(previous));
            foreach (double width in new[] { window.MinWidth, 1280 })
            {
                window.Width = width;
                await WaitForAsync(() => Math.Abs(window.Bounds.Width - width) < 1);
                window.UpdateLayout();
                double row = BoundsIn(fit, window).Center.Y;
                foreach (var button in new[] { previous, next })
                {
                    var bounds = BoundsIn(button, window);
                    Assert.True(Math.Abs(bounds.Center.Y - row) < 1, $"At {width}px {button.Name} sits at {bounds}, off the viewer bar's row at {row}.");
                    Assert.True(bounds.Width <= 44, $"At {width}px {button.Name} is {bounds.Width} px wide, not an icon button.");
                }
            }
        }
        finally
        {
            window.Close();
            DesktopText.SetLanguage(InterfaceLanguage.English);
        }
    }

    [AvaloniaFact]
    public async Task FrenchCountsAgreeWithTheirNumberAndTheCardKeepsItsWordsWhole()
    {
        using var temporary = new TemporaryLibrary();
        DesktopText.SetLanguage(InterfaceLanguage.French);
        var dialogs = new FixtureDialogs(RedesignFixture("Lease Renewal.pdf"));
        using var model = new InboxViewModel(dialogs);
        var window = new MainWindow { DataContext = model, Width = 900, Height = 590 };
        try
        {
            model.InitializeLibrary(() => temporary.Path);
            window.Show();
            await model.AddFilesCommand.ExecuteAsync(null);
            Assert.Equal("1 document ajouté", model.ToastTitle);
            dialogs.SelectedFiles = [RedesignFixture("Match Ledger.pdf"), RedesignFixture("Mixed Pages.pdf")];
            await model.AddFilesCommand.ExecuteAsync(null);
            Assert.Equal("2 documents ajoutés", model.ToastTitle);
            await WaitForPreviewAsync(model);
            window.UpdateLayout();

            // At the minimum width the status card's destination and the card's facts are whole, not cut with "…".
            var status = Assert.IsType<Panel>(window.FindControl<Panel>("StatusCard"));
            var destination = Assert.Single(status.GetVisualDescendants().OfType<TextBlock>(),
                text => text.Text?.Contains("PDFs", StringComparison.Ordinal) == true);
            AssertWhole(destination);
            var facts = window.GetVisualDescendants().OfType<TextBlock>().Where(text => text.Classes.Contains("fact") && text.IsEffectivelyVisible).ToArray();
            Assert.Contains(facts, fact => fact.Text == "Dans la boîte de réception");
            Assert.All(facts, AssertWhole);
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
    public async Task TheSearchPlaceholderStaysWholeBesideItsKeycapAndTipsAtTheMinimumWidth(bool useFrench)
    {
        using var temporary = new TemporaryLibrary();
        using (var library = InboxLibrary.Open(temporary.Path)) BatchFixtures.AddTwoDocuments(library, TestContext.Current.CancellationToken);
        DesktopText.SetLanguage(useFrench ? InterfaceLanguage.French : InterfaceLanguage.English);
        using var model = new InboxViewModel(new FixtureDialogs());
        var window = new MainWindow { DataContext = model, Width = 900, Height = 590 };
        try
        {
            model.InitializeLibrary(() => temporary.Path);
            window.Show();
            await WaitForPreviewAsync(model);
            window.UpdateLayout();
            var search = Assert.IsType<TextBox>(window.FindControl<TextBox>("SearchBox"));
            Assert.True(Assert.IsType<Button>(window.FindControl<Button>("SearchTipsButton")).IsEffectivelyVisible);
            var key = Assert.Single(search.GetVisualDescendants().OfType<Border>(), border => border.Classes.Contains("key") && border.IsEffectivelyVisible);
            var presenter = Assert.Single(search.GetVisualDescendants().OfType<Avalonia.Controls.Presenters.TextPresenter>());
            var words = new TextBlock { Text = search.PlaceholderText, FontSize = search.FontSize, FontWeight = search.FontWeight, FontFamily = search.FontFamily };
            words.Measure(Size.Infinity);
            // The words run from where typing starts to the "/" keycap. Native text is a few pixels wider than headless
            // text (desktop-french.png cut "papiers" when this had 4 px to spare), so the placeholder keeps a margin.
            const double nativeSlack = 10;
            double room = BoundsIn(key, window).Left - BoundsIn(presenter, window).Left;
            Assert.True(words.DesiredSize.Width + nativeSlack <= room,
                $"“{search.PlaceholderText}” needs {words.DesiredSize.Width:0.#} px and {nativeSlack} px to spare, and has {room:0.#}.");
        }
        finally
        {
            window.Close();
            DesktopText.SetLanguage(InterfaceLanguage.English);
        }
    }

    private static void AssertBarsWaitForThePointer(string surface, Control scroller)
    {
        var bars = scroller.GetVisualDescendants().OfType<ScrollBar>().Where(bar => bar.Orientation == Orientation.Vertical).ToArray();
        Assert.NotEmpty(bars);
        Assert.All(bars, bar => Assert.True(bar.Opacity == 0, $"The scroll bar of {surface} is drawn at opacity {bar.Opacity} with no pointer over it."));
    }

    private static void AssertWhole(TextBlock text)
    {
        Assert.NotNull(text.TextLayout);
        Assert.False(text.TextLayout.TextLines.Any(line => line.HasCollapsed), $"“{text.Text}” is cut at {text.Bounds.Width} px.");
    }
}
