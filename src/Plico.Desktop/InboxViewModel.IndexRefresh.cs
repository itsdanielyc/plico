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

    /// <summary>"1 document unavailable.", worded for one or several so French agrees with the number.</summary>
    private string UnavailableSentence() =>
        Counted(UnavailableCount, "1 document unavailable.", $"{UnavailableCount} documents unavailable.");

    [RelayCommand]
    private Task RefreshIndexAsync() => RunOperationAsync(DesktopText.Translate("Checking the PDF folder…"), async token =>
    {
        var library = RequireLibrary();
        RefreshResult? result = null;
        try
        {
            result = await Task.Run(() => library.Refresh(token), token);
        }
        finally
        {
            // An interrupted refresh can already have committed changes to individual PDFs.
            if (result is not { Indexed: 0, Relocated: 0, UnavailablePaths.Count: 0 }) RetireUndo();
        }
        Tags = library.GetTags();
        RefreshInboxCount();
        RefreshUnavailableCount();
        LoadResults(reloadPreview: result.Indexed > 0 || result.Relocated > 0);
        LatestIndexRefresh = result;
        FolderRestoreNotices = [];
        int notices = result.Warnings.Count;
        var summary = new List<string>();
        if (result.Indexed > 0) summary.Add(Counted(result.Indexed, "1 document indexed.", $"{result.Indexed} documents indexed."));
        if (result.Relocated > 0) summary.Add(Counted(result.Relocated, "1 document moved.", $"{result.Relocated} documents moved."));
        if (UnavailableCount > 0) summary.Add(UnavailableSentence());
        if (notices > 0) summary.Add(Counted(notices, "1 index notice.", $"{notices} index notices."));
        // The summary opens on what happened, never on "0 documents indexed"; with nothing to report the index is current.
        Status = summary.Count > 0 ? string.Join(" ", summary) : DesktopText.Translate("Index up to date.");
    });

    [RelayCommand]
    private async Task ShowIndexReportAsync()
    {
        if (!ShowIndexReport || _library is not { } library) return;
        var notices = LatestIndexRefresh?.Warnings ?? [];
        if (FolderRestoreNotices.Count > 0) notices = [.. notices, .. FolderRestoreNotices];
        IndexReport? report = null;
        await RunOperationAsync(DesktopText.Translate("Reading index report…"), async token =>
        {
            var unavailable = await Task.Run(() => library.ReadUnavailablePaths(token), token);
            token.ThrowIfCancellationRequested();
            UnavailableCount = unavailable.Count;
            // Reading the report has nothing to announce, so no toast rises.
            report = new(unavailable, notices);
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
            RetireUndo();
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
            RetireUndo();
            var result = await Task.Run(() => library.RestoreUnavailablePdfsFromFolderAsync(folder, cancellationToken: token), token);
            FolderRestoreNotices = result.Issues.Select(issue => $"{issue.Path} — {issue.Reason}").ToArray();
            RefreshUnavailableCount();
            if (result.Restored > 0)
            {
                Tags = library.GetTags();
                RefreshInboxCount();
                LoadResults();
            }

            string prefix = result.Cancelled ? DesktopText.Translate("Folder restore cancelled.") + " " : "";
            Status = prefix + Counted(result.Restored, "1 document restored.", $"{result.Restored} documents restored.");
            if (result.Unmatched > 0)
                Status += " " + Counted(result.Unmatched, "No matching copy for 1 document.", $"No matching copy for {result.Unmatched} documents.");
            if (UnavailableCount > 0) Status += " " + UnavailableSentence();
            if (result.Issues.Count > 0)
                Status += " " + Counted(result.Issues.Count, "1 restore notice.", $"{result.Issues.Count} restore notices.");
        });
}
