using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Plico.Core;

namespace Plico.Desktop;

internal sealed partial class InboxViewModel
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowIndexReport))]
    private RefreshResult? _latestIndexRefresh;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowIndexReport))]
    private long _unavailableCount;

    public bool ShowIndexReport => !IsBusy &&
        (UnavailableCount > 0 || LatestIndexRefresh is { Warnings.Count: > 0 });

    private void RefreshUnavailableCount() => UnavailableCount = _library?.CountUnavailableDocuments() ?? 0;

    [RelayCommand]
    private Task RefreshIndexAsync() => RunOperationAsync("Checking the PDF folder…", async token =>
    {
        var library = RequireLibrary();
        var result = await Task.Run(() => library.Refresh(token), token);
        Tags = library.GetTags();
        RefreshInboxCount();
        RefreshUnavailableCount();
        LoadResults(reloadPreview: result.Indexed > 0 || result.Relocated > 0);
        LatestIndexRefresh = result;
        Status = $"{DocumentCount(result.Indexed)} indexed.";
        if (result.Relocated > 0) Status += $" {DocumentCount(result.Relocated)} moved.";
        if (UnavailableCount > 0) Status += $" {DocumentCount(UnavailableCount)} unavailable.";
        int notices = result.Warnings.Count;
        if (notices > 0) Status += notices == 1 ? " 1 index notice." : $" {notices} index notices.";
    });

    [RelayCommand]
    private async Task ShowIndexReportAsync()
    {
        if (!ShowIndexReport || _library is not { } library) return;
        var notices = LatestIndexRefresh?.Warnings ?? [];
        string previous = Status;
        IndexReport? report = null;
        await RunOperationAsync("Reading index report…", async token =>
        {
            var unavailable = await Task.Run(() => library.ReadUnavailablePaths(token), token);
            token.ThrowIfCancellationRequested();
            UnavailableCount = unavailable.Count;
            report = new(unavailable, notices);
            Status = previous;
        });

        if (report is not null && ShowIndexReport && ReferenceEquals(_library, library))
        {
            string? path = await _dialogs.ShowIndexRefreshReportAsync(report);
            if (path is not null && CanWork && ReferenceEquals(_library, library))
            {
                await RestoreFromIndexReportAsync(library, path);
            }
        }
    }

    private Task RestoreFromIndexReportAsync(InboxLibrary library, string relativePath) =>
        RunOperationAsync("Choose a matching PDF…", async token =>
        {
            string? copyPath = await _dialogs.PickRestorePdfAsync();
            if (copyPath is null)
            {
                Status = "PDF restore cancelled.";
                return;
            }

            Status = "Restoring PDF…";
            var restored = await Task.Run(() => library.RestoreUnavailablePdfAsync(relativePath, copyPath, token), token);
            Tags = library.GetTags();
            RefreshInboxCount();
            RefreshUnavailableCount();
            Navigate(inboxOnly: !restored.Reviewed, preferredId: restored.Id);
            Status = "PDF restored.";
        });
}
