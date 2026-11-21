using Avalonia;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Plico.Core;
using Plico.Desktop;
using Xunit;

namespace Plico.Tests;

public sealed partial class InboxWindowTests
{
    [AvaloniaFact]
    public async Task AKeyTypedIntoAFieldOrThePaletteNeverFiresItsShortcutOnRelease()
    {
        using var temporary = new TemporaryLibrary();
        using (var library = InboxLibrary.Open(temporary.Path)) BatchFixtures.AddTwoDocuments(library, TestContext.Current.CancellationToken);
        using var model = new InboxViewModel(new FixtureDialogs());
        model.InitializeLibrary(() => temporary.Path);
        var window = new MainWindow { DataContext = model };
        window.Show();
        try
        {
            await WaitForPreviewAsync(model);
            var list = window.FindControl<ListBox>("DocumentList")!;

            // "x" typed into the palette, then Escape before the key comes back up.
            window.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.K, KeyModifiers = KeyModifiers.Control });
            var query = window.FindControl<TextBox>("PaletteQuery")!;
            Assert.True(query.Focus());
            query.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.X, KeySymbol = "x" });
            query.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Escape });
            Assert.False(window.FindControl<Border>("PaletteOverlay")!.IsVisible);
            // Closing the palette hands the keyboard to the queue, which is where the release lands.
            list.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyUpEvent, Key = Key.X, KeySymbol = "x" });
            await WaitForAsync(() => !model.IsBusy);
            Assert.Equal(2, model.Documents.Count);

            // "r" typed into search, then focus leaves the field before the key comes back up.
            var search = window.FindControl<TextBox>("SearchBox")!;
            Assert.True(search.Focus());
            search.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.R, KeySymbol = "r" });
            Assert.True(window.FindControl<Button>("ReviewButton")!.Focus());
            ReleaseKey(window, Key.R, "r");
            Assert.Equal(2, model.InboxCount);

            // Ctrl held for the press and let go first: the bare release is not the single-key shortcut.
            window.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.X, KeySymbol = "x", KeyModifiers = KeyModifiers.Control });
            ReleaseKey(window, Key.X, "x");
            await WaitForAsync(() => !model.IsBusy);
            Assert.Equal(2, model.Documents.Count);

            // Pressed and released outside a field, the key still works.
            PressWindowKey(window, Key.R, "r");
            Assert.Equal(1, model.InboxCount);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task ARowKeepsItsThumbnailWhileTheSameDocumentRendersAgain()
    {
        using var temporary = new TemporaryLibrary();
        using (var library = InboxLibrary.Open(temporary.Path)) BatchFixtures.AddTwoDocuments(library, TestContext.Current.CancellationToken);
        using var model = new InboxViewModel(new FixtureDialogs());
        model.InitializeLibrary(() => temporary.Path);
        var window = new MainWindow { DataContext = model };
        window.Show();
        try
        {
            await WaitForPreviewAsync(model);
            long open = model.SelectedDocument!.Id;
            Image RowImage() => Assert.IsType<Image>(window.GetVisualDescendants().OfType<DocumentThumbnail>()
                .Single(thumbnail => thumbnail.Document?.Id == open).Child);
            await WaitForAsync(() => RowImage().Source is not null);

            // An operation on the open document drops its cached thumbnail, and the refreshed row renders it again.
            await model.RefreshIndexCommand.ExecuteAsync(null);
            window.UpdateLayout();
            Assert.NotNull(RowImage().Source);
            await WaitForPreviewAsync(model);
            Assert.NotNull(RowImage().Source);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task SelectAllShownWorksFromAnywhereOutsideAField()
    {
        using var temporary = new TemporaryLibrary();
        using (var library = InboxLibrary.Open(temporary.Path)) BatchFixtures.AddTwoDocuments(library, TestContext.Current.CancellationToken);
        using var model = new InboxViewModel(new FixtureDialogs());
        model.InitializeLibrary(() => temporary.Path);
        var window = new MainWindow { DataContext = model };
        window.Show();
        try
        {
            await WaitForPreviewAsync(model);
            var list = window.FindControl<ListBox>("DocumentList")!;
            Assert.True(window.FindControl<Button>("ReviewButton")!.Focus());
            window.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.A, KeyModifiers = KeyModifiers.Control });
            Assert.Equal(2, list.Selection.Count);
            Assert.True(model.HasBatchSelection);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task EveryButtonHasANameAScreenReaderCanSpeak()
    {
        using var temporary = new TemporaryLibrary();
        using var model = new InboxViewModel(new FixtureDialogs());
        model.InitializeLibrary(() => temporary.Path);
        var window = new MainWindow { DataContext = model };
        window.Show();
        try
        {
            Assert.True(model.ShowWelcome);
            Assert.Empty(UnnamedButtons(window));

            Directory.CreateDirectory(Path.Combine(temporary.Path, "Second"));
            using (var library = InboxLibrary.Open(Path.Combine(temporary.Path, "Second")))
                BatchFixtures.AddTwoDocuments(library, TestContext.Current.CancellationToken);
            model.InitializeLibrary(() => Path.Combine(temporary.Path, "Second"));
            await WaitForPreviewAsync(model);
            model.ToggleReviewCommand.Execute(null);
            await WaitForPreviewAsync(model);
            Assert.True(model.HasUndo);
            Assert.Empty(UnnamedButtons(window));
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task NarrowWindowsKeepLoadMoreAndSelectShownWithinReach()
    {
        var token = TestContext.Current.CancellationToken;
        using var temporary = new TemporaryLibrary();
        using (var library = InboxLibrary.Open(temporary.Path))
        {
            string fixture = Path.Combine(AppContext.BaseDirectory, "fixtures", "Lease Renewal.pdf");
            for (int index = 1; index <= LibraryCatalog.PageSize + 2; index++)
                File.Copy(fixture, Path.Combine(library.PdfDirectory, $"Lease {index:D3}.pdf"));
            Assert.Equal(LibraryCatalog.PageSize + 2, library.Refresh(token).Indexed);
        }
        using var model = new InboxViewModel(new FixtureDialogs());
        model.InitializeLibrary(() => temporary.Path);
        var window = new MainWindow { DataContext = model, Width = 1028, Height = 749 };
        window.Show();
        try
        {
            await WaitForPreviewAsync(model);
            window.UpdateLayout();
            Assert.Contains("compact", window.FindControl<Grid>("WorkspaceGrid")!.Classes);
            Assert.True(model.HasMore);

            var loadMore = Assert.Single(VisibleButtons(window), button => ReferenceEquals(button.Command, model.LoadMoreCommand));
            Assert.True(IsInside(loadMore, window), "Load more is outside the window.");
            loadMore.Command!.Execute(null);
            Assert.Equal(LibraryCatalog.PageSize + 2, model.Documents.Count);
            Assert.DoesNotContain(VisibleButtons(window), button => ReferenceEquals(button.Command, model.LoadMoreCommand));

            var selectShown = Assert.Single(VisibleButtons(window), button => button.Content as string == model.SelectShownAction);
            Assert.True(IsInside(selectShown, window), "Select shown is outside the window.");
            selectShown.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal(LibraryCatalog.PageSize + 2, window.FindControl<ListBox>("DocumentList")!.Selection.Count);

            // A wide window goes back to one set of these controls, under the list.
            window.Width = 1280;
            window.UpdateLayout();
            Assert.Single(VisibleButtons(window), button => button.Content as string == model.SelectShownAction);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void TheShortcutsSheetIsTranslatedRowByRow()
    {
        using var temporary = new TemporaryLibrary();
        using var model = new InboxViewModel(new FixtureDialogs());
        model.InitializeLibrary(() => temporary.Path);
        try
        {
            string[] english = ShortcutLabels(model, InterfaceLanguage.English);
            string[] french = ShortcutLabels(model, InterfaceLanguage.French);
            Assert.Equal(english.Length, french.Length);
            Assert.True(english.Length >= 19, $"Only {english.Length} shortcut rows.");
            Assert.Empty(english.Intersect(french));
        }
        finally
        {
            DesktopText.SetLanguage(InterfaceLanguage.English);
        }
    }

    private static string[] ShortcutLabels(InboxViewModel model, InterfaceLanguage language)
    {
        DesktopText.SetLanguage(language);
        var window = new MainWindow { DataContext = model };
        window.Show();
        try
        {
            window.ShowShortcutSheet();
            // Each row holds its label and a panel of keycaps; group titles carry the "meta" class.
            return window.FindControl<Grid>("ShortcutsGrid")!.GetVisualDescendants().OfType<TextBlock>()
                .Where(text => text.Parent is Grid && !text.Classes.Contains("meta")).Select(text => text.Text ?? "").ToArray();
        }
        finally
        {
            window.Close();
        }
    }

    private static IEnumerable<Button> VisibleButtons(Window window) =>
        window.GetVisualDescendants().OfType<Button>().Where(button => button.IsEffectivelyVisible);

    /// <summary>Names the visible buttons whose automation name is missing or is a control type's name.</summary>
    private static string[] UnnamedButtons(Window window) =>
        [.. VisibleButtons(window).Select(button => (button, name: ControlAutomationPeer.CreatePeerForElement(button).GetName()))
            .Where(item => string.IsNullOrWhiteSpace(item.name) || item.name.StartsWith("Avalonia.", StringComparison.Ordinal))
            .Select(item => $"{item.button.Name ?? item.button.Command?.ToString() ?? "(unnamed control)"} → \"{item.name}\"")];

    private static bool IsInside(Control control, Window window) =>
        control.TranslatePoint(default, window) is { } origin && origin.X >= 0 && origin.Y >= 0 &&
        origin.X + control.Bounds.Width <= window.Bounds.Width + 0.5 && origin.Y + control.Bounds.Height <= window.Bounds.Height + 0.5;

    private static void ReleaseKey(Window window, Key key, string symbol)
    {
        var target = window.FocusManager?.GetFocusedElement() as Interactive ?? window;
        target.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyUpEvent, Key = key, KeySymbol = symbol });
    }
}
