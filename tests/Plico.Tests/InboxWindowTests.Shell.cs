using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using Plico.Core;
using Plico.Desktop;
using Xunit;

namespace Plico.Tests;

/// <summary>The miille shell: the toast that replaced the status bar, and the welcome page at the minimum size.</summary>
public sealed partial class InboxWindowTests
{
    [AvaloniaFact]
    public async Task TheToastRisesOverThePageAndCarriesOnlyItsOwnUndo()
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
            var toast = Assert.IsType<Border>(window.FindControl<Border>("Toast"));
            var undo = Assert.IsType<Button>(window.FindControl<Button>("UndoButton"));

            model.ToggleReviewCommand.Execute(null);
            await WaitForPreviewAsync(model);
            window.UpdateLayout();
            Assert.True(toast.IsEffectivelyVisible);
            Assert.True(undo.IsEffectivelyVisible);
            Assert.Equal("Undo", undo.Content);

            // Over the page canvas, above the page bar, and clear of the filing card where Mark reviewed lives.
            var page = Assert.IsType<Border>(window.FindControl<Border>("PageColumn"));
            var review = Assert.IsType<Button>(window.FindControl<Button>("ReviewButton"));
            var toastBounds = Bounds(toast, window);
            var pageBounds = Bounds(page, window);
            Assert.True(toastBounds.Left >= pageBounds.Left - 1 && toastBounds.Right <= pageBounds.Right + 1,
                $"The toast {toastBounds} left the page canvas {pageBounds}.");
            Assert.False(toastBounds.Intersects(Bounds(review, window)));
            var pageBar = page.GetVisualDescendants().OfType<Border>().Single(border => border.Classes.Contains("pagebar"));
            Assert.True(toastBounds.Bottom <= Bounds(pageBar, window).Top - 15, "The toast does not sit above the page bar.");

            // A later, unrelated message keeps the undo on Ctrl+Z and in the palette, but not in its toast.
            model.TagInput = "Toast check";
            model.AddTagCommand.Execute(null);
            await WaitForAsync(() => model.SelectedDocument?.Tags.Contains("Toast check") == true);
            Assert.True(model.HasUndo);
            Assert.True(toast.IsEffectivelyVisible);
            Assert.False(undo.IsEffectivelyVisible);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void TheWelcomePageFitsTheMinimumWindowAndDropsItsArtWhenNarrow()
    {
        using var temporary = new TemporaryLibrary();
        using var model = new InboxViewModel(new FixtureDialogs());
        model.InitializeLibrary(() => temporary.Path);
        var window = new MainWindow { DataContext = model, Width = 900, Height = 590 };
        window.Show();
        try
        {
            Assert.True(model.ShowWelcome);
            window.UpdateLayout();
            var page = Assert.IsType<StackPanel>(window.FindControl<StackPanel>("WelcomePage"));
            var scroll = Assert.IsType<ScrollViewer>(page.Parent);
            Assert.True(scroll.Extent.Height <= scroll.Viewport.Height + 1, $"The welcome page needs {scroll.Extent} in {scroll.Viewport}.");
            Assert.True(scroll.Extent.Width <= scroll.Viewport.Width + 1);
            var art = page.GetVisualDescendants().OfType<Panel>().Single(panel => panel.Classes.Contains("hero-art"));
            Assert.False(art.IsEffectivelyVisible);

            window.Width = 1280;
            window.Height = 800;
            window.UpdateLayout();
            Assert.True(art.IsEffectivelyVisible);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task TheFrenchMarkReviewedKeepsItsKeycapAtTheMinimumWidth()
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
            var review = Assert.IsType<Button>(window.FindControl<Button>("ReviewButton"));
            var key = review.GetVisualDescendants().OfType<Border>().Single(border => border.Classes.Contains("key"));
            var label = review.GetVisualDescendants().OfType<TextBlock>().First(text => text.Text == model.ReviewAction);
            var button = Bounds(review, window);
            Assert.True(Bounds(key, window).Right <= button.Right - 4, $"The R keycap {Bounds(key, window)} leaves {button}.");
            Assert.True(Bounds(label, window).Left >= button.Left + 4, $"The label {Bounds(label, window)} leaves {button}.");
        }
        finally
        {
            window.Close();
            DesktopText.SetLanguage(InterfaceLanguage.English);
        }
    }

    private static Rect Bounds(Visual visual, Visual relativeTo)
    {
        var origin = visual.TranslatePoint(default, relativeTo) ?? throw new InvalidOperationException("The control is not in the window.");
        return new Rect(origin, visual.Bounds.Size);
    }
}
