using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using CommunityToolkit.Mvvm.Input;
using Plico.Core;
using Plico.Desktop;
using Xunit;

namespace Plico.Tests;

public sealed partial class InboxWindowTests
{
    [AvaloniaFact]
    public void SaveSearchAppearsWhenThereIsASearchToSave()
    {
        using var temporary = new TemporaryLibrary();
        using var model = new InboxViewModel(new FixtureDialogs());
        model.InitializeLibrary(() => temporary.Path);
        var window = new MainWindow { DataContext = model };
        window.Show();
        try
        {
            var save = Assert.IsType<Button>(window.FindControl<Button>("SaveSearchButton"));
            Assert.False(save.IsEffectivelyVisible);

            model.Query = "tag:property renewal";
            Assert.True(save.IsEffectivelyVisible);
            Assert.True(save.IsEnabled);

            model.Query = "  ";
            Assert.False(save.IsEffectivelyVisible);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task SavedSearchShortcutRestoresQueryTagAndSort()
    {
        var token = TestContext.Current.CancellationToken;
        using var temporary = new TemporaryLibrary();
        long scopedId;
        using (var library = InboxLibrary.Open(temporary.Path))
        {
            string fixtures = Path.Combine(AppContext.BaseDirectory, "fixtures");
            var wanted = await library.ImportAsync(Path.Combine(fixtures, "Lease Renewal.pdf"), token);
            await library.ImportAsync(Path.Combine(fixtures, "Match Ledger.pdf"), token);
            library.AddTag(wanted.Id, "Property");
            scopedId = wanted.Id;
        }

        var dialogs = new FixtureDialogs();
        using var model = new InboxViewModel(dialogs);
        model.InitializeLibrary(() => temporary.Path);
        var window = new MainWindow { DataContext = model };
        window.Show();
        try
        {
            model.FilterByTag("Property");
            model.Query = "renewal";
            model.SortOption = model.SortChoices.Single(choice => choice.Order == DocumentSort.Title);
            var save = Assert.IsType<Button>(window.FindControl<Button>("SaveSearchButton"));
            dialogs.SavedSearchChoice = (draft, scope, editing) =>
            {
                Assert.False(editing);
                Assert.Equal("renewal", draft.Query);
                Assert.Equal("Property", scope);
                Assert.Equal(DocumentSort.Title, draft.Sort);
                return Task.FromResult<SavedSearchDecision?>(new SavedSearchDecision.Store(draft with { Name = "Property renewals" }));
            };
            await Assert.IsAssignableFrom<IAsyncRelayCommand>(save.Command).ExecuteAsync(null);
            var saved = Assert.Single(model.SavedSearches);
            Assert.Equal("Property renewals", model.Heading);

            model.ShowAllCommand.Execute(null);
            model.SortOption = model.SortChoices.Single(choice => choice.Order == DocumentSort.Newest);
            Assert.Equal(2, model.Documents.Count);
            var shortcuts = Assert.IsType<ItemsControl>(window.FindControl<ItemsControl>("SavedSearchList"));
            await WaitForAsync(() => shortcuts.GetVisualDescendants().OfType<Button>().Any());
            var open = Assert.Single(shortcuts.GetVisualDescendants().OfType<Button>());
            open.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await WaitForAsync(() => model.Heading == "Property renewals");
            Assert.Equal(saved, model.ActiveSavedSearch);
            Assert.Equal("renewal", model.Query);
            Assert.Equal(DocumentSort.Title, model.SortOption?.Order);
            Assert.Equal(scopedId, Assert.Single(model.Documents).Id);

            var edit = Assert.IsType<Button>(window.FindControl<Button>("EditSavedSearchButton"));
            dialogs.SavedSearchChoice = (draft, scope, editing) =>
            {
                Assert.True(editing);
                Assert.Equal("Property renewals", draft.Name);
                Assert.Equal("Property", scope);
                return Task.FromResult<SavedSearchDecision?>(new SavedSearchDecision.Store(new("Current lease", "north", DocumentSort.Newest)));
            };
            var editCommand = Assert.IsAssignableFrom<IAsyncRelayCommand>(edit.Command);
            await editCommand.ExecuteAsync(null);
            Assert.Equal(saved.Id, Assert.Single(model.SavedSearches).Id);
            Assert.Equal("Current lease", model.Heading);
            Assert.Equal("north", model.Query);
            Assert.Equal(DocumentSort.Newest, model.SortOption?.Order);

            dialogs.SavedSearchChoice = (draft, scope, editing) =>
            {
                Assert.True(editing);
                Assert.Equal("Current lease", draft.Name);
                Assert.Equal("Property", scope);
                return Task.FromResult<SavedSearchDecision?>(new SavedSearchDecision.Remove());
            };
            await editCommand.ExecuteAsync(null);
            Assert.Empty(model.SavedSearches);
            Assert.Equal("Search results", model.Heading);
            Assert.Equal(scopedId, Assert.Single(model.Documents).Id);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task SavedSearchDialogShowsValidationBeforeClosing()
    {
        var owner = new Window();
        owner.Show();
        var dialog = new SavedSearchDialog(new("Property renewal", "folder:", DocumentSort.Title), null, editing: false);
        var decision = dialog.ShowDialog<SavedSearchDecision?>(owner);
        try
        {
            var confirm = Assert.IsType<Button>(dialog.FindControl<Button>("ConfirmSavedSearchButton"));
            confirm.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.False(decision.IsCompleted);
            Assert.True(dialog.FindControl<TextBlock>("SavedSearchValidation")?.IsVisible);
            Assert.False(dialog.FindControl<Button>("RemoveSavedSearchButton")?.IsVisible);

            var query = Assert.IsType<TextBox>(dialog.FindControl<TextBox>("SavedQueryBox"));
            query.Text = "folder:Filed renewal";
            confirm.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var stores = Assert.IsType<SavedSearchDecision.Store>(await decision);
            Assert.Equal("folder:Filed renewal", stores.Draft.Query);
            Assert.Equal(DocumentSort.Title, stores.Draft.Sort);
        }
        finally
        {
            if (dialog.IsVisible) dialog.Close();
            await decision;
            owner.Close();
        }
    }
}
