using Avalonia;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.VisualTree;
using Plico.Core;
using Plico.Desktop;
using Xunit;

namespace Plico.Tests;

public sealed partial class InboxWindowTests
{
    [AvaloniaFact]
    public void TheShortcutsSheetOwnsKeyboardFocusAndBlocksTheWorkspace()
    {
        using var temporary = new TemporaryLibrary();
        using var model = new InboxViewModel(new FixtureDialogs());
        model.InitializeLibrary(() => temporary.Path);
        var window = new MainWindow { DataContext = model };
        window.Show();
        try
        {
            var search = window.FindControl<TextBox>("SearchBox")!;
            Assert.True(search.Focus());
            window.ShowShortcutSheet();
            var sheet = window.FindControl<Border>("ShortcutsOverlay")!;
            Assert.Contains(sheet.GetVisualDescendants(), control => ReferenceEquals(control, window.FocusManager!.GetFocusedElement()));
            window.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.F, KeyModifiers = KeyModifiers.Control });
            Assert.False(search.IsFocused);
            Assert.False(search.Focus(), "The search field behind the sheet accepted keyboard focus.");
            window.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Escape });
            Assert.True(search.IsFocused);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task ADocumentShortcutCannotTrashOneHiddenMemberOfABatch()
    {
        using var temporary = new TemporaryLibrary();
        using (var library = InboxLibrary.Open(temporary.Path))
            BatchFixtures.AddTwoDocuments(library, TestContext.Current.CancellationToken);
        using var model = new InboxViewModel(new FixtureDialogs());
        model.InitializeLibrary(() => temporary.Path);
        var window = new MainWindow { DataContext = model };
        window.Show();
        try
        {
            window.SelectAllShown();
            Assert.True(model.HasBatchSelection);
            var list = window.FindControl<ListBox>("DocumentList")!;
            list.Focus();
            list.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.X, KeySymbol = "x" });
            list.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyUpEvent, Key = Key.X, KeySymbol = "x" });
            await WaitForAsync(() => !model.IsBusy);
            Assert.Equal(2, model.InboxCount);
            Assert.DoesNotContain(PaletteCommands.Build(window, model), entry => entry.Title == "Move to Trash");
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task AnOpenPaletteCannotTrashADocumentAfterSettingsSwitchesLibraries()
    {
        using var first = new TemporaryLibrary();
        using var second = new TemporaryLibrary();
        foreach (var location in new[] { first.Path, second.Path })
        {
            using var library = InboxLibrary.Open(location);
            BatchFixtures.AddTwoDocuments(library, TestContext.Current.CancellationToken);
        }
        using var model = new InboxViewModel(new FixtureDialogs { FolderPath = second.Path });
        model.InitializeLibrary(() => first.Path);
        var window = new MainWindow { DataContext = model };
        window.Show();
        try
        {
            window.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.K, KeyModifiers = KeyModifiers.Control });
            var query = window.FindControl<TextBox>("PaletteQuery")!;
            query.Text = "Move to Trash";
            Assert.Equal("Move to Trash", Assert.IsType<PaletteCommand>(window.FindControl<ListBox>("PaletteList")!.SelectedItem).Title);
            await model.ChooseLibraryCommand.ExecuteAsync(null);
            Assert.Equal(second.Path, model.LibraryPath);
            query.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Enter });
            await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() => { }, Avalonia.Threading.DispatcherPriority.Background);
            await WaitForAsync(() => !model.IsBusy);
            Assert.Equal(2, model.InboxCount);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void ThePaletteOnlyOffersActionsAvailableInTheCurrentWorkspace()
    {
        using var temporary = new TemporaryLibrary();
        using var model = new InboxViewModel(new FixtureDialogs());
        var window = new MainWindow { DataContext = model };
        window.Show();
        try
        {
            var unopened = PaletteCommands.Build(window, model);
            Assert.DoesNotContain(unopened, entry => entry.Title == "Add files…");
            Assert.Contains(unopened, entry => entry.Title == "Choose library folder…");
            model.InitializeLibrary(() => temporary.Path);
            var empty = PaletteCommands.Build(window, model);
            Assert.Contains(empty, entry => entry.Title == "Add files…");
            Assert.DoesNotContain(empty, entry => entry.Title is "Undo" or "Stop watching" or "Save search…");
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task ThumbnailStripNamesEachQueueItemForKeyboardAndScreenReaderNavigation()
    {
        using var temporary = new TemporaryLibrary();
        using (var library = InboxLibrary.Open(temporary.Path))
            BatchFixtures.AddTwoDocuments(library, TestContext.Current.CancellationToken);
        using var model = new InboxViewModel(new FixtureDialogs());
        model.InitializeLibrary(() => temporary.Path);
        var window = new MainWindow { DataContext = model, Width = 900 };
        window.Show();
        try
        {
            await WaitForPreviewAsync(model);
            var queue = window.FindControl<ListBox>("DocumentList")!;
            foreach (var document in model.Documents)
            {
                var row = Assert.IsType<ListBoxItem>(queue.ContainerFromItem(document));
                Assert.Equal(document.Title, ControlAutomationPeer.CreatePeerForElement(row).GetName());
            }
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task EveryMatchAndZoomButtonFitsInTheNarrowPageColumn()
    {
        using var temporary = new TemporaryLibrary();
        using var model = new InboxViewModel(new FixtureDialogs(RedesignFixture("Match Ledger.pdf")));
        model.InitializeLibrary(() => temporary.Path);
        var window = new MainWindow { DataContext = model, Width = 1100, Height = 749 };
        window.Show();
        try
        {
            await model.AddFilesCommand.ExecuteAsync(null);
            model.Query = "ledger renewal";
            await WaitForAsync(() => model.HasPageMatchAction);
            await model.GoToMatchCommand.ExecuteAsync(null);
            window.UpdateLayout();
            var page = window.FindControl<Border>("PageColumn")!;
            var bounds = new List<Rect>();
            foreach (var name in new[] { "PreviousMatchButton", "GoToMatchButton", "PreviewFitButton", "PageThumbnailsButton", "ZoomOutButton", "ZoomInButton", "OpenPdfButton" })
            {
                var button = window.FindControl<Button>(name)!;
                Assert.True(button.IsEffectivelyVisible);
                Point start = button.TranslatePoint(default, page)!.Value;
                Assert.InRange(start.X, -1, page.Bounds.Width - button.Bounds.Width + 1);
                var current = new Rect(start, button.Bounds.Size);
                Assert.DoesNotContain(bounds, previous => previous.Intersects(current));
                bounds.Add(current);
            }
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void SettingsActionsIdentifyTheirTargetInBothLanguages()
    {
        using var temporary = new TemporaryLibrary();
        using var model = new InboxViewModel(new FixtureDialogs());
        model.InitializeLibrary(() => temporary.Path);
        try
        {
            foreach (var language in new[] { InterfaceLanguage.English, InterfaceLanguage.French })
            {
                DesktopText.SetLanguage(language);
                var settings = new SettingsWindow { DataContext = model };
                settings.Show();
                try
                {
                    var actions = VisibleButtons(settings).Where(button => button.Classes.Contains("secondary"));
                    var names = actions.Select(button => ControlAutomationPeer.CreatePeerForElement(button).GetName()).ToArray();
                    Assert.Empty(names.GroupBy(name => name).Where(group => group.Count() > 1).Select(group => group.Key));
                    Assert.DoesNotContain(names, name => string.IsNullOrWhiteSpace(name));
                }
                finally { settings.Close(); }
            }
        }
        finally { DesktopText.SetLanguage(InterfaceLanguage.English); }
    }

    [AvaloniaFact]
    public async Task TheCompactFrenchSidebarFitsItsInboxLabelAndTwoTagsWithSavedIntake()
    {
        using var temporary = new TemporaryLibrary();
        using var settings = new TemporaryLibrary();
        using var incoming = new TemporaryLibrary();
        using (var library = InboxLibrary.Open(temporary.Path))
        {
            BatchFixtures.AddTwoDocuments(library, TestContext.Current.CancellationToken);
            var id = library.Find(new DocumentFilter()).Items[0].Id;
            foreach (string tag in new[] { "Bills", "Receipts", "Taxes" }) library.AddTag(id, tag);
            Directory.CreateDirectory(Path.Combine(library.PdfDirectory, "Filed"));
        }
        var intake = new IntakeFolderPreference(settings.Path);
        intake.Save(temporary.Path, "Filed");
        var watch = new WatchFolderPreference(settings.Path);
        watch.Save(temporary.Path, incoming.Path);
        DesktopText.SetLanguage(InterfaceLanguage.French);
        using var model = new InboxViewModel(new FixtureDialogs(), watchPreference: watch, intakeFolderPreference: intake);
        var window = new MainWindow { DataContext = model, Width = 900, Height = 590 };
        try
        {
            model.InitializeLibrary(() => temporary.Path);
            window.Show();
            await WaitForPreviewAsync(model);
            // Places keep one line in their 36 px rows: Inbox and All documents use their sidebar labels in French, whole.
            foreach (var (name, text) in new[] { ("InboxButton", "Réception"), ("AllDocumentsButton", "Documents") })
            {
                var place = window.FindControl<Button>(name)!;
                var label = place.GetVisualDescendants().OfType<TextBlock>().Single(block => block.Text == text);
                var fullLabel = new TextBlock { Text = label.Text, FontFamily = label.FontFamily, FontSize = label.FontSize, FontWeight = label.FontWeight };
                fullLabel.Measure(Size.Infinity);
                Assert.True(label.Bounds.Width >= fullLabel.DesiredSize.Width - 1,
                    $"{name} label has {label.Bounds.Width} pixels for {fullLabel.DesiredSize.Width} pixels of text.");
                Assert.True(place.Bounds.Height <= 36.5, $"{name} grew to {place.Bounds.Height} pixels.");
            }
            var inbox = window.FindControl<Button>("InboxButton")!;
            Assert.Equal("Boîte de réception", ToolTip.GetTip(inbox));
            var rail = inbox.FindAncestorOfType<ScrollViewer>()!;
            var tags = rail.GetVisualDescendants().OfType<Button>().Where(button => button.Classes.Contains("rail")).Take(2).ToArray();
            Assert.Equal(2, tags.Length);
            foreach (var tag in tags)
            {
                var bottom = tag.TranslatePoint(new Point(0, tag.Bounds.Height), rail)!.Value.Y;
                Assert.InRange(bottom, 0, rail.Viewport.Height + 1);
            }
            // The routine detail (the recognition language and where it runs) waits for a wider sidebar. Its words follow
            // the platform, "ce PC" or "cet ordinateur", and on macOS and Linux the longer one took a second line that
            // pushed the second tag out of view (CI run 45027446333). Without it the card is the same height everywhere.
            var detail = window.FindControl<TextBlock>("StatusCardDetailText")!;
            Assert.False(detail.IsEffectivelyVisible, $"The compact status card still shows “{detail.Text}”.");
        }
        finally
        {
            window.Close();
            DesktopText.SetLanguage(InterfaceLanguage.English);
        }
    }

    [AvaloniaFact]
    public async Task TheStatusCardNamesTheRecognitionLanguageWhenTheSidebarHasRoom()
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
            var detail = window.FindControl<TextBlock>("StatusCardDetailText")!;
            Assert.True(detail.IsEffectivelyVisible, "The status card hides its detail in a wide window.");
            Assert.Equal(PlatformText.RecognitionPlace(model.RecognitionLanguage), detail.Text);
        }
        finally { window.Close(); }
    }
}
