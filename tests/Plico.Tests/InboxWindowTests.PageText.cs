using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Plico.Core;
using Plico.Desktop;
using Xunit;

namespace Plico.Tests;

public sealed partial class InboxWindowTests
{
    [AvaloniaFact]
    public async Task ReadPageTextFollowsThePreviewAndHandlesAnImageOnlyPage()
    {
        const string fixtureName = "Mixed Pages.pdf";
        const string scanName = "Scanned Receipt.pdf";
        const string letterKeyword = "NORTHSTAR";
        const string emptyPage = "This page has no searchable text.";
        using var temporary = new TemporaryLibrary();
        using (var library = InboxLibrary.Open(temporary.Path))
        {
            File.Copy(Path.Combine(AppContext.BaseDirectory, "fixtures", fixtureName),
                Path.Combine(library.PdfDirectory, fixtureName));
            File.Copy(Path.Combine(AppContext.BaseDirectory, "fixtures", scanName),
                Path.Combine(library.PdfDirectory, scanName));
            Assert.Equal(2, library.Refresh(TestContext.Current.CancellationToken).Indexed);
        }

        using var model = new InboxViewModel(new FixtureDialogs());
        model.InitializeLibrary(() => temporary.Path);
        var window = new MainWindow { DataContext = model, Width = 900, Height = 590 };
        window.Show();
        try
        {
            var documents = Assert.IsType<ListBox>(window.FindControl<ListBox>("DocumentList"));
            var mixed = Assert.Single(model.Documents, document => document.PageCount == 2);
            documents.SetCurrentValue(ListBox.SelectedItemProperty, mixed);
            await WaitForPreviewAsync(model);
            Assert.NotNull(model.Preview);
            var toggle = Assert.IsType<Button>(window.FindControl<Button>("ReadPageTextButton"));
            var text = Assert.IsType<SelectableTextBlock>(window.FindControl<SelectableTextBlock>("PageTextContent"));
            var textScroll = Assert.IsType<ScrollViewer>(window.FindControl<ScrollViewer>("PageTextScroll"));
            var pdfScroll = Assert.IsType<ScrollViewer>(window.FindControl<ScrollViewer>("PreviewScroll"));
            var fit = Assert.IsType<Button>(window.FindControl<Button>("PreviewFitButton"));
            var pageNavigation = Assert.IsAssignableFrom<Panel>(window.FindControl<Panel>("PreviewPageNavigation"));
            var copy = Assert.IsType<Button>(window.FindControl<Button>("CopyDocumentTextButton"));
            Assert.Equal("Read text", toggle.Content);
            Assert.True(toggle.IsVisible && toggle.IsEnabled && pdfScroll.IsVisible && fit.IsVisible);
            Assert.False(textScroll.IsVisible);
            AssertPageNavigationFits(window, pageNavigation, copy, toggle);

            toggle.Command!.Execute(null);
            await WaitForAsync(() => text.Text?.Contains(letterKeyword, StringComparison.OrdinalIgnoreCase) == true);
            Assert.True(textScroll.IsVisible);
            Assert.False(pdfScroll.IsVisible);
            Assert.False(fit.IsVisible);
            Assert.Equal("Show page", toggle.Content);
            text.SelectionStart = 0;
            text.SelectionEnd = text.Text!.Length;
            Assert.Contains(letterKeyword, text.SelectedText, StringComparison.OrdinalIgnoreCase);

            model.NextPageCommand.Execute(null);
            await WaitForAsync(() => model.PreviewMessage == emptyPage);
            Assert.Equal("2 / 2", model.PageLabel);
            Assert.False(textScroll.IsVisible);
            Assert.True(string.IsNullOrEmpty(text.Text));
            Assert.Equal("Show page", toggle.Content);

            model.PreviousPageCommand.Execute(null);
            await WaitForAsync(() => text.Text?.Contains(letterKeyword, StringComparison.OrdinalIgnoreCase) == true);
            Assert.Equal("1 / 2", model.PageLabel);
            Assert.Equal(0, text.SelectionEnd);

            toggle.Command.Execute(null);
            await WaitForPreviewAsync(model);
            Assert.NotNull(model.Preview);
            Assert.True(pdfScroll.IsVisible && fit.IsVisible);
            Assert.False(textScroll.IsVisible);
            Assert.Equal("Read text", toggle.Content);

            toggle.Command.Execute(null);
            await WaitForAsync(() => textScroll.IsVisible);
            var scan = Assert.Single(model.Documents, document => !document.HasText);
            documents.SetCurrentValue(ListBox.SelectedItemProperty, scan);
            await WaitForPreviewAsync(model);
            Assert.NotNull(model.Preview);
            Assert.True(pdfScroll.IsVisible && fit.IsVisible);
            Assert.False(toggle.IsVisible || textScroll.IsVisible);
            Assert.True(string.IsNullOrEmpty(text.Text));
        }
        finally
        {
            window.Close();
        }
    }

    private static void AssertPageNavigationFits(Window window, Panel navigation, Button copy, Button read)
    {
        var previous = Assert.IsType<Button>(navigation.Children[0]);
        var label = Assert.IsType<TextBlock>(navigation.Children[1]);
        var next = Assert.IsType<Button>(navigation.Children[2]);
        string originalPageLabel = Assert.IsType<string>(label.Text);
        foreach (string pageLabel in new[] { originalPageLabel, "987 / 1000" })
        {
            label.SetCurrentValue(TextBlock.TextProperty, pageLabel);
            window.UpdateLayout();
            var copyRight = Assert.IsType<Point>(copy.TranslatePoint(new Point(copy.Bounds.Width, 0), window));
            var previousLeft = Assert.IsType<Point>(previous.TranslatePoint(default, window));
            var nextRight = Assert.IsType<Point>(next.TranslatePoint(new Point(next.Bounds.Width, 0), window));
            var readLeft = Assert.IsType<Point>(read.TranslatePoint(default, window));
            Assert.True(copyRight.X <= previousLeft.X, $"Previous page overlaps Copy text at page {pageLabel}.");
            Assert.True(nextRight.X <= readLeft.X, $"Next page overlaps Read text at page {pageLabel}.");
        }
        label.SetCurrentValue(TextBlock.TextProperty, originalPageLabel);
    }
}
