using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using CommunityToolkit.Mvvm.Input;
using Plico.Core;
using Plico.Desktop;
using Xunit;

namespace Plico.Tests;

public sealed partial class InboxWindowTests
{
    [AvaloniaFact]
    public async Task FilingCardOffersRuleSuggestionsAndAcceptsThemInOneStep()
    {
        using var temporary = new TemporaryLibrary();
        using (var library = InboxLibrary.Open(temporary.Path))
        {
            await library.ImportAsync(RedesignFixture("Lease Renewal.pdf"), TestContext.Current.CancellationToken);
            library.SaveFilingRule(new FilingRule(null, "Housing", "deposit", FilingRuleMatch.AnyWord));
        }
        using var model = new InboxViewModel(new FixtureDialogs());
        model.InitializeLibrary(() => temporary.Path);
        var window = new MainWindow { DataContext = model };
        window.Show();
        try
        {
            await WaitForAsync(() => model.HasSuggestions);
            var suggested = Assert.Single(model.SuggestedTags);
            Assert.Equal("Housing", suggested.Label);
            Assert.Equal(FilingTagSource.Rule, suggested.Source);
            Assert.Contains("tag rules", model.SuggestionReason);
            var accept = Assert.IsType<Button>(window.FindControl<Button>("AcceptSuggestionsButton"));
            Assert.True(accept.IsEffectivelyVisible);

            accept.Command!.Execute(null);

            Assert.Contains("Housing", model.SelectedDocument!.Tags);
            Assert.DoesNotContain(model.SuggestedTags, tag => tag.Label == "Housing");
            Assert.False(model.SelectedDocument.Reviewed);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task ReviewAndTrashCanBeUndoneFromTheMessageBar()
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
            var undo = Assert.IsType<Button>(window.FindControl<Button>("UndoButton"));
            Assert.False(undo.IsVisible);
            long reviewed = model.SelectedDocument!.Id;

            model.ToggleReviewCommand.Execute(null);
            Assert.Equal(1, model.InboxCount);
            Assert.True(undo.IsVisible);
            await Assert.IsAssignableFrom<IAsyncRelayCommand>(undo.Command).ExecuteAsync(null);
            Assert.Equal(2, model.InboxCount);
            Assert.Equal(reviewed, model.SelectedDocument?.Id);
            Assert.False(model.SelectedDocument!.Reviewed);
            Assert.False(model.HasUndo);

            await model.TrashDocumentCommand.ExecuteAsync(null);
            Assert.Equal(1, model.InboxCount);
            Assert.StartsWith("Moved ", model.UndoLabel);
            await model.UndoCommand.ExecuteAsync(null);
            Assert.Equal(2, model.InboxCount);
            Assert.Equal(2, model.Documents.Count);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task SingleKeysReviewAndMoveWhileTypingIsLeftAlone()
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
            long first = model.SelectedDocument!.Id;
            PressWindowKey(window, Key.J, "j");
            Assert.NotEqual(first, model.SelectedDocument!.Id);
            PressWindowKey(window, Key.K, "k");
            Assert.Equal(first, model.SelectedDocument!.Id);

            var search = Assert.IsType<TextBox>(window.FindControl<TextBox>("SearchBox"));
            Assert.True(search.Focus());
            PressWindowKey(window, Key.R, "r");
            Assert.Equal(2, model.InboxCount);

            Assert.True(window.FindControl<Button>("ReviewButton")!.Focus());
            PressWindowKey(window, Key.R, "r");
            Assert.Equal(1, model.InboxCount);
            Assert.True(model.HasUndo);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task CommandPaletteFindsToolsByNameAndAliasAndCloses()
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
            var overlay = Assert.IsType<Border>(window.FindControl<Border>("PaletteOverlay"));
            var query = Assert.IsType<TextBox>(window.FindControl<TextBox>("PaletteQuery"));
            var list = Assert.IsType<ListBox>(window.FindControl<ListBox>("PaletteList"));
            window.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.K, KeyModifiers = KeyModifiers.Control });
            Assert.True(overlay.IsVisible);

            query.Text = "trash";
            Assert.Equal("Trash…", Assert.IsType<PaletteCommand>(list.SelectedItem).Title);
            var trash = Assert.Single(list.ItemsSource!.Cast<PaletteCommand>(), command => command.Title == "Move to Trash");
            Assert.Equal(["X"], trash.Keys);

            query.Text = "ocr";
            Assert.Contains(list.ItemsSource!.Cast<PaletteCommand>(), command => command.Title == "Without text");

            query.Text = "zzzz";
            Assert.True(window.FindControl<TextBlock>("PaletteEmpty")!.IsVisible);

            window.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Escape });
            Assert.False(overlay.IsVisible);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task EmptyLibraryWelcomesThenInboxClearFollowsTheLastReview()
    {
        using var temporary = new TemporaryLibrary();
        using var model = new InboxViewModel(new FixtureDialogs(RedesignFixture("Lease Renewal.pdf")));
        model.InitializeLibrary(() => temporary.Path);
        var window = new MainWindow { DataContext = model };
        window.Show();
        try
        {
            Assert.True(model.ShowWelcome);
            Assert.False(model.ShowInboxClear);
            await model.AddFilesCommand.ExecuteAsync(null);
            Assert.False(model.ShowInboxZero);
            model.ToggleReviewCommand.Execute(null);
            Assert.True(model.ShowInboxClear);
            Assert.False(model.ShowWelcome);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void NarrowWindowsTurnTheQueueIntoAThumbnailStrip()
    {
        using var temporary = new TemporaryLibrary();
        using var model = new InboxViewModel(new FixtureDialogs());
        model.InitializeLibrary(() => temporary.Path);
        var window = new MainWindow { DataContext = model };
        window.Show();
        try
        {
            var workspace = Assert.IsType<Grid>(window.FindControl<Grid>("WorkspaceGrid"));
            Assert.DoesNotContain("compact", workspace.Classes);
            Assert.True(workspace.ColumnDefinitions[1].ActualWidth > 440);
            window.Width = window.MinWidth;
            window.UpdateLayout();
            Assert.Contains("compact", workspace.Classes);
            Assert.Equal(84, workspace.ColumnDefinitions[0].Width.Value);
            Assert.True(workspace.ColumnDefinitions[1].ActualWidth >= 300);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void EveryRedesignedSurfaceLoadsItsTextInEnglishAndFrench()
    {
        using var temporary = new TemporaryLibrary();
        using var model = new InboxViewModel(new FixtureDialogs());
        model.InitializeLibrary(() => temporary.Path);
        try
        {
            foreach (var language in new[] { InterfaceLanguage.English, InterfaceLanguage.French })
            {
                DesktopText.SetLanguage(language);
                var window = new MainWindow { DataContext = model };
                window.Show();
                var settings = new SettingsWindow { DataContext = model };
                settings.Show(window);
                Assert.Equal(language == InterfaceLanguage.French ? "Réglages de Plico" : "Plico settings", settings.Title);
                window.ShowShortcutSheet();
                Assert.Equal(2, window.FindControl<Grid>("ShortcutsGrid")!.Children.Count);
                settings.Close();
                window.Close();
            }
        }
        finally
        {
            DesktopText.SetLanguage(InterfaceLanguage.English);
        }
    }

    [AvaloniaFact]
    public async Task NarrowWindowsKeepTheHeadingSortAndSaveSearch()
    {
        using var temporary = new TemporaryLibrary();
        using (var library = InboxLibrary.Open(temporary.Path)) BatchFixtures.AddTwoDocuments(library, TestContext.Current.CancellationToken);
        using var model = new InboxViewModel(new FixtureDialogs());
        model.InitializeLibrary(() => temporary.Path);
        // A 1024 × 768 screen leaves about this much room, as on the hosted Windows and macOS runners.
        var window = new MainWindow { DataContext = model, Width = 1028, Height = 749 };
        window.Show();
        try
        {
            await WaitForPreviewAsync(model);
            model.Query = "lease";
            await WaitForAsync(() => model.Heading == "Search results" && model.Documents.Count == 2);
            window.UpdateLayout();
            Assert.Contains("compact", window.FindControl<Grid>("WorkspaceGrid")!.Classes);
            var heading = window.FindControl<TextBlock>("InboxHeading")!;
            Assert.True(heading.IsEffectivelyVisible);
            Assert.True(heading.Bounds.Width > 120, $"Heading width {heading.Bounds.Width}");
            Assert.True(window.FindControl<ComboBox>("DocumentSortBox")!.IsEffectivelyVisible);
            Assert.True(window.FindControl<Button>("SaveSearchButton")!.IsEffectivelyVisible);
        }
        finally
        {
            window.Close();
        }
    }

    [Fact]
    public void PaletteRanksTitlePrefixesBeforeAliasesAndSubsequences()
    {
        PaletteCommand Command(string title, string aliases) => new(title, "Test", null, [], aliases, () => { });
        var commands = new[] { Command("Export PDF…", "copy save"), Command("Extract pages…", "split"), Command("Move to Trash", "delete") };

        Assert.Equal(["Export PDF…", "Extract pages…"], PaletteCommands.Filter(commands, "ex").Select(command => command.Title));
        Assert.Equal("Move to Trash", Assert.Single(PaletteCommands.Filter(commands, "delete")).Title);
        Assert.Equal("Extract pages…", Assert.Single(PaletteCommands.Filter(commands, "xtpg")).Title);
        Assert.Equal(3, PaletteCommands.Filter(commands, "  ").Count);
    }

    private static string RedesignFixture(string name) => Path.Combine(AppContext.BaseDirectory, "fixtures", name);

    private static void PressWindowKey(Window window, Key key, string symbol) =>
        window.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyUpEvent, Key = key, KeySymbol = symbol });
}
