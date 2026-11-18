using Avalonia.Controls;
using Avalonia.Interactivity;
using Plico.Core;

namespace Plico.Desktop;

internal sealed record SavedSearchDraft(string Name, string Query, DocumentSort Sort);

internal abstract record SavedSearchDecision
{
    internal sealed record Store(SavedSearchDraft Draft) : SavedSearchDecision;
    internal sealed record Remove : SavedSearchDecision;
}

/// <summary>Edits a named search and validates it with the same parser used for document retrieval.</summary>
internal sealed partial class SavedSearchDialog : Window
{
    internal SavedSearchDialog(SavedSearchDraft draft, string? scopeTag, bool editing)
    {
        InitializeComponent();
        Title = editing ? "Edit saved search" : DesktopText.Translate("Save search");
        SavedNameBox.MaxLength = InboxLibrary.MaximumTitleLength;
        SavedNameBox.Text = draft.Name;
        SavedQueryBox.MaxLength = InboxLibrary.MaximumSearchQueryLength;
        SavedQueryBox.Text = draft.Query;
        SavedScope.IsVisible = scopeTag is not null;
        if (scopeTag is not null) SavedScope.Text = $"Scoped to the “{scopeTag}” tag.";
        SavedSortBox.ItemsSource = InboxViewModel.AvailableSortChoices;
        SavedSortBox.SelectedItem = InboxViewModel.AvailableSortChoices.Single(choice => choice.Order == draft.Sort);
        RemoveSavedSearchButton.IsVisible = editing;
    }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        SavedNameBox.Focus();
        SavedNameBox.SelectAll();
    }

    private void Cancel_Click(object? sender, RoutedEventArgs e) => Close();

    private void Remove_Click(object? sender, RoutedEventArgs e) => Close(new SavedSearchDecision.Remove());

    private void Save_Click(object? sender, RoutedEventArgs e)
    {
        try
        {
            string name = InboxLibrary.NormalizeTitle(SavedNameBox.Text ?? "");
            string query = InboxLibrary.NormalizeSavedSearchQuery(SavedQueryBox.Text ?? "");
            var sort = ((InboxViewModel.SortChoice?)SavedSortBox.SelectedItem)?.Order ?? DocumentSort.Automatic;
            Close(new SavedSearchDecision.Store(new(name, query, sort)));
        }
        catch (ArgumentException error)
        {
            SavedSearchValidation.Text = error.Message;
            SavedSearchValidation.IsVisible = true;
            (error.ParamName == "query" ? SavedQueryBox : SavedNameBox).Focus();
        }
    }
}
