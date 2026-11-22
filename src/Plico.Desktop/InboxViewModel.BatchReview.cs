using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Plico.Core;

namespace Plico.Desktop;

internal sealed partial class InboxViewModel
{
    private IReadOnlyList<DocumentSummary> _batchSelection = [];
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasResultActions))]
    private bool _hasDeferredWatchResults;
    [ObservableProperty] private string _batchTagInput = "";

    public bool HasBatchCandidates => Documents.Count > 1;
    public bool HasResultActions => HasBatchCandidates || HasDeferredWatchResults;
    public bool HasBatchSelection => _batchSelection.Count > 1;
    public bool HasTaggedBatchSelection => HasBatchSelection && _batchSelection.Any(document => document.Tags.Count > 0);
    public string BatchCountLabel => DesktopText.Format($"{_batchSelection.Count} selected");
    public string SelectShownAction => HasBatchSelection ? DesktopText.Translate("Clear selection") : DesktopText.Translate("Select shown");
    public string BatchReviewAction => AllSelectedAreReviewed ? DesktopText.Translate("Move selected back to Inbox") : DesktopText.Translate("Mark selected reviewed");
    private bool AllSelectedAreReviewed => _batchSelection.All(document => document.Reviewed);

    internal void SetBatchSelection(IReadOnlyList<DocumentSummary> selectedDocuments)
    {
        _batchSelection = selectedDocuments;
        OnPropertyChanged(nameof(HasBatchSelection));
        OnPropertyChanged(nameof(HasTaggedBatchSelection));
        OnPropertyChanged(nameof(BatchCountLabel));
        OnPropertyChanged(nameof(SelectShownAction));
        OnPropertyChanged(nameof(BatchReviewAction));
        // While results are replaced the list is empty for a moment. Hiding the card then would drop the keyboard focus
        // of whatever field or button in it has it; the selection that follows raises the card's state anyway.
        if (!_replacingResults) OnPropertyChanged(nameof(ShowFilingCard));
        if (HasDeferredWatchResults && !HasBatchSelection && !HasExpandedResults)
        {
            Dispatcher.UIThread.Post(() =>
            {
                if (HasDeferredWatchResults) RefreshAfterWatch();
            }, DispatcherPriority.Background);
        }
    }

    private void RefreshAfterWatch()
    {
        // A queued query will read the new catalog. An active query needs a fresh read.
        if (_searchTimer.IsEnabled) return;
        bool preserveView = _searchCancellation is null || _appliedQuery == Query;
        if (preserveView && (HasBatchSelection || HasExpandedResults))
        {
            HasDeferredWatchResults = true;
            return;
        }
        CancelTypedSearch();
        _ = LoadTypedSearchAsync(preserveView);
    }

    [RelayCommand]
    private void RefreshWatchedResults()
    {
        if (HasDeferredWatchResults && CanWork) LoadResults();
    }

    [RelayCommand]
    private void ToggleReview()
    {
        if (!CanWork || SelectedDocument is not { } document) return;
        var position = CaptureReviewPosition(document.Id);
        bool reviewed = !document.Reviewed;
        try
        {
            var library = RequireLibrary();
            library.SetReviewed(document.Id, reviewed);
            RefreshInboxCount();
            LoadResults(document.Id, retainLoadedResults: true, reviewPosition: position);
            string title = reviewed ? DesktopText.Translate("Marked reviewed") : DesktopText.Translate("Back in the Inbox");
            OfferUndo(ReviewUndo(library, [document.Id], reviewed, title, document.Title),
                reviewed ? DesktopText.Translate("Marked reviewed.") : DesktopText.Translate("Back in the Inbox."));
        }
        catch (Exception error)
        {
            Status = error.Message;
        }
    }

    [RelayCommand]
    private Task ReviewSelectedAsync()
    {
        if (!HasBatchSelection) return Task.CompletedTask;
        var selectedIds = _batchSelection.Select(document => document.Id).ToArray();
        bool reviewed = !AllSelectedAreReviewed;
        var changedIds = _batchSelection.Where(document => document.Reviewed != reviewed).Select(document => document.Id).ToArray();
        var position = CaptureReviewPosition(SelectedDocument?.Id ?? selectedIds[0]);
        return RunOperationAsync("Updating the selected documents…", async token =>
        {
            var library = RequireLibrary();
            await Task.Run(() => library.SetReviewedDocuments(selectedIds, reviewed), token);
            RefreshInboxCount();
            LoadResults(retainLoadedResults: true, reviewPosition: position);
            var count = DocumentCount(selectedIds.Length);
            string status = reviewed ? DesktopText.Format($"{count} marked reviewed.") : DesktopText.Format($"{count} back in the Inbox.");
            // The offer puts back only the documents that changed, so already reviewed members stay reviewed.
            OfferUndo(ReviewUndo(library, changedIds, reviewed, WithoutFullStop(status), null), status);
        });
    }

    private sealed record ReviewPosition(DocumentFilter Filter, IReadOnlyList<DocumentSummary> PreviousResults, int Index);

    private ReviewPosition? CaptureReviewPosition(long documentId)
    {
        if (_appliedQuery != Query) return null;
        for (int index = 0; index < Documents.Count; index++)
        {
            if (Documents[index].Id == documentId) return new ReviewPosition(CurrentFilter(), Documents, index);
        }
        return null;
    }

    // O(n + m) time for n previous rows and m current rows. O(m) space when the focused row leaves the view.
    private static long? ChooseReviewResult(IReadOnlyList<DocumentSummary> documents, ReviewPosition position)
    {
        if (documents.Count == 0) return null;
        var previous = position.PreviousResults;
        long focused = previous[position.Index].Id;
        if (documents.Any(document => document.Id == focused)) return focused;

        var currentIndices = new Dictionary<long, int>(documents.Count);
        for (int index = 0; index < documents.Count; index++) currentIndices.Add(documents[index].Id, index);
        for (int index = position.Index + 1; index < previous.Count; index++)
        {
            long candidate = previous[index].Id;
            if (currentIndices.ContainsKey(candidate)) return candidate;
        }
        for (int index = position.Index - 1; index >= 0; index--)
        {
            if (!currentIndices.TryGetValue(previous[index].Id, out int preceding)) continue;
            // A newly loaded row can now follow the last surviving row from the previous page.
            return documents[Math.Min(preceding + 1, documents.Count - 1)].Id;
        }
        return null;
    }

    internal Task ApplySelectedTagAsync()
    {
        if (!HasBatchSelection) return Task.CompletedTask;
        if (string.IsNullOrWhiteSpace(BatchTagInput))
        {
            Status = DesktopText.Translate("Enter a tag for the selection.");
            return Task.CompletedTask;
        }
        var selection = _batchSelection;
        var selectedIds = selection.Select(document => document.Id).ToArray();
        var tag = BatchTagInput;
        return RunOperationAsync("Tagging the selected documents…", async token =>
        {
            var library = RequireLibrary();
            // O(t) over the selection's tags: Undo takes the tag back off only the documents that did not have it.
            string key = InboxLibrary.GetTagKey(tag);
            long[] untagged = selection.Where(document => !document.Tags.Any(label => InboxLibrary.GetTagKey(label) == key))
                .Select(document => document.Id).ToArray();
            await Task.Run(() => library.AddTagToDocuments(selectedIds, tag), token);
            Tags = library.GetTags();
            LoadResults(retainLoadedResults: true);
            BatchTagInput = "";
            string status = DesktopText.Format($"Tag applied to {DocumentCount(selectedIds.Length)}.");
            if (untagged.Length > 0)
            {
                OfferUndo(BatchTagUndo(library, untagged, tag.Trim(), selectedIds.Length), status);
                return;
            }
            RetireUndo();
            Status = status;
        });
    }

    internal Task RemoveSelectedTagAsync()
    {
        if (!HasTaggedBatchSelection) return Task.CompletedTask;
        var selectedDocuments = _batchSelection;
        return RunOperationAsync("Choose a tag to remove…", async token =>
        {
            var library = RequireLibrary();
            string? tag = await _dialogs.ChooseTagToRemoveAsync(GetBatchTagOptions(selectedDocuments));
            if (tag is null)
            {
                Status = DesktopText.Translate("Tag removal cancelled.");
                return;
            }

            var selectedIds = selectedDocuments.Select(document => document.Id).ToArray();
            int changed = await Task.Run(() => library.RemoveTagFromDocuments(selectedIds, tag, token), token);
            RetireUndo();
            Tags = library.GetTags();
            LoadResults(retainLoadedResults: true);
            Status = changed == 0 ? DesktopText.Translate("The selected documents no longer have that tag.") : DesktopText.Format($"Tag removed from {DocumentCount(changed)}.");
        });
    }

    // O(t + u log u) time and O(u) space for t selected tag assignments and u unique labels.
    private static BatchTagOption[] GetBatchTagOptions(IReadOnlyList<DocumentSummary> documents)
    {
        var assignmentCounts = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var document in documents)
        {
            foreach (string label in document.Tags)
            {
                assignmentCounts[label] = assignmentCounts.GetValueOrDefault(label) + 1;
            }
        }

        return assignmentCounts.OrderBy(entry => entry.Key, StringComparer.OrdinalIgnoreCase)
            .Select(entry => new BatchTagOption(entry.Key, entry.Value))
            .ToArray();
    }
}
