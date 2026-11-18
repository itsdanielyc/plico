using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using CommunityToolkit.Mvvm.Input;
using Plico.Core;

namespace Plico.Desktop;

internal static partial class NativeVerification
{
    private static async Task VerifySavedSearchAsync(MainWindow window, InboxViewModel model, FixtureDialogs dialogs, string directory)
    {
        const string savedName = "Filed receipts";
        const string sidebarTag = "Packet";
        const string filedTitle = "Paperwork packet";
        string query = $"{ReceiptKeyword} folder:Filed/Packets";
        var sortBox = Control<ComboBox>(window, "DocumentSortBox");
        var tagButton = window.GetVisualDescendants().OfType<Button>()
            .First(button => button.DataContext is string tag && tag == sidebarTag);
        tagButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        sortBox.SetCurrentValue(ComboBox.SelectedItemProperty, model.SortChoices.Single(choice => choice.Order == DocumentSort.Title));
        Control<TextBox>(window, "SearchBox").SetCurrentValue(TextBox.TextProperty, query);
        await UntilAsync(() => model.ResultCount.Contains($"in {sidebarTag}", StringComparison.Ordinal) &&
            model.Documents is [{ Title: filedTitle }], () => model.ResultCount);

        dialogs.SavedSearchChoice = (draft, scopeTag, editing) =>
        {
            Require(!editing && draft.Query == query && scopeTag == sidebarTag && draft.Sort == DocumentSort.Title,
                "The saved search editor missed part of the active filter.");
            return ConfirmSavedSearchAsync(window, draft, scopeTag, savedName, directory);
        };
        try
        {
            var save = Control<Button>(window, "SaveSearchButton");
            Require(save.IsEffectivelyVisible && save.IsEnabled && save.Command is IAsyncRelayCommand,
                "The active query did not offer a saved search.");
            await ((IAsyncRelayCommand)save.Command!).ExecuteAsync(save.CommandParameter);
        }
        finally
        {
            dialogs.SavedSearchChoice = null;
        }
        Require(model.ActiveSavedSearch is { Name: savedName }, model.Status);

        Control<Button>(window, "AllDocumentsButton").Command!.Execute(null);
        sortBox.SetCurrentValue(ComboBox.SelectedItemProperty, model.SortChoices.Single(choice => choice.Order == DocumentSort.Newest));
        Require(model.Query.Length == 0 && model.Heading == "All documents", "The saved search could not be left.");
        var list = Control<ItemsControl>(window, "SavedSearchList");
        await UntilAsync(() => list.GetVisualDescendants().OfType<Button>().Any(button => button.DataContext is SavedSearch),
            () => "The saved search shortcut did not appear in the sidebar.");
        var shortcut = list.GetVisualDescendants().OfType<Button>()
            .Single(button => button.DataContext is SavedSearch { Name: savedName });
        shortcut.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        await UntilAsync(() => model.Heading == savedName && model.Preview is not null, () => model.Status);
        Require(model.Query == query && model.SortOption?.Order == DocumentSort.Title &&
                model.ResultCount.Contains($"in {sidebarTag}", StringComparison.Ordinal) && model.Documents is [{ Title: filedTitle }],
            "Opening the saved search did not restore its query, scope, and order.");
        await CaptureAsync(window, directory, "saved-search.png");
    }

    private static async Task<SavedSearchDecision?> ConfirmSavedSearchAsync(Window owner, SavedSearchDraft draft, string? scopeTag,
        string name, string directory)
    {
        var dialog = new SavedSearchDialog(draft, scopeTag, editing: false);
        var result = dialog.ShowDialog<SavedSearchDecision?>(owner);
        try
        {
            Control<TextBox>(dialog, "SavedNameBox").Text = name;
            await CaptureAsync(dialog, directory, "saved-search-dialog.png");
            Control<Button>(dialog, "ConfirmSavedSearchButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            return await result;
        }
        finally
        {
            if (dialog.IsVisible) dialog.Close();
            await result;
        }
    }
}
