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

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowIndexReport))]
    private IReadOnlyList<string> _folderRestoreNotices = [];

    public bool ShowIndexReport => !IsBusy &&
        (UnavailableCount > 0 || LatestIndexRefresh is { Warnings.Count: > 0 } || FolderRestoreNotices.Count > 0);

    private void RefreshUnavailableCount() => UnavailableCount = _library?.CountUnavailableDocuments() ?? 0;

    [RelayCommand]
    private Task RefreshIndexAsync() => RunOperationAsync(DesktopText.Translate("Checking the PDF folder…"), async token =>
    {
        var library = RequireLibrary();
        var result = await Task.Run(() => library.Refresh(token), token);
        Tags = library.GetTags();
        RefreshInboxCount();
        RefreshUnavailableCount();
        LoadResults(reloadPreview: result.Indexed > 0 || result.Relocated > 0);
        LatestIndexRefresh = result;
        FolderRestoreNotices = [];
        Status = DesktopText.Format($"{DocumentCount(result.Indexed)} indexed.");
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
        if (FolderRestoreNotices.Count > 0) notices = [.. notices, .. FolderRestoreNotices];
        string previous = Status;
        IndexReport? report = null;
        await RunOperationAsync(DesktopText.Translate("Reading index report…"), async token =>
        {
            var unavailable = await Task.Run(() => library.ReadUnavailablePaths(token), token);
            token.ThrowIfCancellationRequested();
            UnavailableCount = unavailable.Count;
            report = new(unavailable, notices);
            Status = previous;
        });

        if (report is not null && ShowIndexReport && ReferenceEquals(_library, library))
        {
            var decision = await _dialogs.ShowIndexRefreshReportAsync(report);
            if (!CanWork || !ReferenceEquals(_library, library)) return;
            switch (decision)
            {
                case IndexReportDecision.RestorePdf selected:
                    await RestoreFromIndexReportAsync(library, selected.RelativePath);
                    break;
                case IndexReportDecision.RestoreFolder:
                    await RestoreFolderFromIndexReportAsync(library);
                    break;
            }
        }
    }

    private Task RestoreFromIndexReportAsync(InboxLibrary library, string relativePath) =>
        RunOperationAsync("Choose a matching PDF…", async token =>
        {
            string? copyPath = await _dialogs.PickRestorePdfAsync();
            if (copyPath is null)
            {
                Status = DesktopText.Translate("PDF restore cancelled.");
                return;
            }

            Status = DesktopText.Translate("Restoring PDF…");
            var restored = await Task.Run(() => library.RestoreUnavailablePdfAsync(relativePath, copyPath, token), token);
            Tags = library.GetTags();
            RefreshInboxCount();
            RefreshUnavailableCount();
            Navigate(inboxOnly: !restored.Reviewed, preferredId: restored.Id);
            Status = DesktopText.Translate("PDF restored.");
        });

    private Task RestoreFolderFromIndexReportAsync(InboxLibrary library) =>
        RunOperationAsync("Choose a folder of backup PDFs…", async token =>
        {
            string? folder = await _dialogs.PickFolderAsync("Choose a folder of backup PDFs");
            if (folder is null)
            {
                Status = DesktopText.Translate("Folder restore cancelled.");
                return;
            }

            Status = DesktopText.Translate("Matching backup PDFs…");
            var result = await Task.Run(() => library.RestoreUnavailablePdfsFromFolderAsync(folder, cancellationToken: token), token);
            FolderRestoreNotices = result.Issues.Select(issue => $"{issue.Path} — {issue.Reason}").ToArray();
            RefreshUnavailableCount();
            if (result.Restored > 0)
            {
                Tags = library.GetTags();
                RefreshInboxCount();
                LoadResults();
            }

            string prefix = result.Cancelled ? "Folder restore cancelled. " : "";
            Status = $"{prefix}{DocumentCount(result.Restored)} restored.";
            if (result.Unmatched > 0) Status += $" No matching copy for {DocumentCount(result.Unmatched)}.";
            if (UnavailableCount > 0) Status += $" {DocumentCount(UnavailableCount)} unavailable.";
            if (result.Issues.Count > 0) Status += result.Issues.Count == 1 ? " 1 restore notice." : $" {result.Issues.Count} restore notices.";
        });
}
