using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Plico.Core;

namespace Plico.Desktop;

internal sealed partial class InboxViewModel
{
    private IReadOnlyList<DocumentSummary> _batchSelection = [];
    private bool _watchResultsPending;
    [ObservableProperty] private string _batchTagInput = "";

    public bool HasBatchCandidates => Documents.Count > 1;
    public bool HasBatchSelection => _batchSelection.Count > 1;
    public string BatchCountLabel => $"{_batchSelection.Count} selected";
    public string SelectShownAction => HasBatchSelection ? "Clear selection" : "Select shown";
    public string BatchReviewAction => AllSelectedAreReviewed ? "Return selected to inbox" : "Mark selected reviewed";
    private bool AllSelectedAreReviewed => _batchSelection.All(document => document.Reviewed);

    internal void SetBatchSelection(IReadOnlyList<DocumentSummary> selectedDocuments)
    {
        _batchSelection = selectedDocuments;
        OnPropertyChanged(nameof(HasBatchSelection));
        OnPropertyChanged(nameof(BatchCountLabel));
        OnPropertyChanged(nameof(SelectShownAction));
        OnPropertyChanged(nameof(BatchReviewAction));
        if (_watchResultsPending && !HasBatchSelection)
        {
            Dispatcher.UIThread.Post(() =>
            {
                if (_watchResultsPending) RefreshAfterWatch();
            }, DispatcherPriority.Background);
        }
    }

    private void RefreshAfterWatch()
    {
        if (HasBatchSelection)
        {
            _watchResultsPending = true;
            return;
        }
        LoadResults();
    }

    [RelayCommand]
    private Task ReviewSelectedAsync()
    {
        if (!HasBatchSelection) return Task.CompletedTask;
        var selectedIds = _batchSelection.Select(document => document.Id).ToArray();
        bool reviewed = !AllSelectedAreReviewed;
        return RunOperationAsync("Updating the selected documents…", async token =>
        {
            var library = RequireLibrary();
            await Task.Run(() => library.SetReviewedDocuments(selectedIds, reviewed), token);
            LoadResults();
            var count = DocumentCount(selectedIds.Length);
            Status = reviewed ? $"{count} marked reviewed." : $"{count} returned to inbox.";
        });
    }

    internal Task ApplySelectedTagAsync()
    {
        if (!HasBatchSelection) return Task.CompletedTask;
        if (string.IsNullOrWhiteSpace(BatchTagInput))
        {
            Status = "Enter a tag for the selection.";
            return Task.CompletedTask;
        }
        var selectedIds = _batchSelection.Select(document => document.Id).ToArray();
        var tag = BatchTagInput;
        return RunOperationAsync("Tagging the selected documents…", async token =>
        {
            var library = RequireLibrary();
            await Task.Run(() => library.AddTagToDocuments(selectedIds, tag), token);
            Tags = library.GetTags();
            LoadResults();
            BatchTagInput = "";
            Status = $"Tag applied to {DocumentCount(selectedIds.Length)}.";
        });
    }
}
