using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Plico.Core;

namespace Plico.Desktop;

internal sealed partial class InboxViewModel
{
    private bool _restoringSavedSearch;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSavedSearches))]
    private IReadOnlyList<SavedSearch> _savedSearches = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowSaveSearch))]
    [NotifyPropertyChangedFor(nameof(HasActiveSavedSearch))]
    private SavedSearch? _activeSavedSearch;

    public bool HasSavedSearches => SavedSearches.Count > 0;
    public bool HasActiveSavedSearch => ActiveSavedSearch is not null;
    public bool ShowSaveSearch => ActiveSavedSearch is null && !string.IsNullOrWhiteSpace(Query);

    internal Task OpenSavedSearchAsync(SavedSearch search) => CanWork && SavedSearches.Contains(search)
        ? ApplySavedSearchAsync(search) : Task.CompletedTask;

    [RelayCommand]
    private Task SaveSearchAsync() => ShowSaveSearch ? ChangeSavedSearchAsync(null) : Task.CompletedTask;

    [RelayCommand]
    private Task EditActiveSavedSearchAsync() => ActiveSavedSearch is { } search
        ? ChangeSavedSearchAsync(search) : Task.CompletedTask;

    private Task ChangeSavedSearchAsync(SavedSearch? existing)
    {
        var filter = CurrentFilter();
        var draft = existing is null ? new SavedSearchDraft("", filter.Query, filter.Sort)
            : new SavedSearchDraft(existing.Name, existing.Query, existing.Sort);
        string? scopeTag = existing is null ? filter.Tag : existing.ScopeTag;
        return RunOperationAsync(existing is null ? "Name this search…" : "Edit saved search…", async token =>
        {
            var library = RequireLibrary();
            var choice = await _dialogs.EditSavedSearchAsync(draft, scopeTag, editing: existing is not null);
            token.ThrowIfCancellationRequested();
            if (choice is SavedSearchDecision.Store stored)
            {
                var edited = stored.Draft;
                var saved = existing is null
                    ? library.SaveSearch(edited.Name, edited.Query, edited.Sort, scopeTag)
                    : library.UpdateSavedSearch(existing.Id, edited.Name, edited.Query, edited.Sort);
                SavedSearches = library.GetSavedSearches();
                Status = DesktopText.Format($"Saved “{saved.Name}”.");
                await ApplySavedSearchAsync(saved);
            }
            else if (choice is SavedSearchDecision.Remove && existing is not null)
            {
                library.RemoveSavedSearch(existing.Id);
                SavedSearches = library.GetSavedSearches();
                ActiveSavedSearch = null;
                UpdateResultHeading(CurrentFilter());
                Status = DesktopText.Format($"Removed “{existing.Name}”.");
            }
            else Status = DesktopText.Translate("Saved search editor closed.");
        });
    }

    private Task ApplySavedSearchAsync(SavedSearch search)
    {
        CancelTypedSearch();
        ResetPageMatchNavigation();
        _appliedQuery = null;
        _restoringSavedSearch = true;
        try
        {
            _inboxOnly = false;
            _activeTag = search.ScopeTag;
            OnPropertyChanged(nameof(HasActiveTag));
            Query = search.Query;
            SortOption = AvailableSortChoices.Single(choice => choice.Order == search.Sort);
            ActiveSavedSearch = search;
        }
        finally
        {
            _restoringSavedSearch = false;
        }
        SetSearchPending();
        OnPropertyChanged(nameof(HasPageMatchAction));
        return LoadTypedSearchAsync();
    }
}
