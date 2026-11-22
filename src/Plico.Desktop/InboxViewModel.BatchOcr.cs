using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Plico.Core;

namespace Plico.Desktop;

internal sealed partial class InboxViewModel
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasOcrCopyBatchResults))]
    private IReadOnlyList<OcrCopyBatchItem> _ocrCopyBatchResults = [];

    public bool HasOcrCopyBatchResults => OcrCopyBatchResults.Count > 0;

    [RelayCommand]
    private Task CreateSelectedOcrCopiesAsync()
    {
        if (!HasBatchSelection) return Task.CompletedTask;
        var sources = _batchSelection.ToArray();
        var language = RecognitionLanguage;
        return RunOperationAsync("Creating OCR copies…", token => RecognizeSelectedPdfsAsync(sources, language, token));
    }

    // O(n) scheduling and report space for n PDFs, plus the single-file OCR costs.
    private async Task RecognizeSelectedPdfsAsync(DocumentSummary[] sources, OcrLanguage language, CancellationToken token)
    {
        var library = RequireLibrary();
        var results = new List<OcrCopyBatchItem>(sources.Length);
        long? latestCopyId = null;
        int added = 0;
        int reused = 0;
        int failed = 0;
        for (int index = 0; index < sources.Length && !token.IsCancellationRequested; index++)
        {
            var source = sources[index];
            ReportProgress(index, sources.Length);
            Status = $"{language} OCR {index + 1} of {sources.Length}. {source.Title}";
            FileImportResult copy;
            try
            {
                copy = await Task.Run(() => library.CreateSearchableCopyAsync(source.Id, token, language), token);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                break;
            }
            catch (Exception error)
            {
                results.Add(new(source.Title, source.RelativePath, OcrCopyOutcome.Failed, error.Message));
                failed++;
                continue;
            }

            results.Add(new(source.Title, source.RelativePath, copy.Added ? OcrCopyOutcome.Added : OcrCopyOutcome.Reused,
                copy.Document.RelativePath));
            if (copy.Added)
            {
                latestCopyId = copy.Document.Id;
                added++;
            }
            else reused++;
        }

        int remaining = sources.Length - results.Count;
        for (int index = results.Count; index < sources.Length; index++)
        {
            var source = sources[index];
            results.Add(new(source.Title, source.RelativePath, OcrCopyOutcome.Unprocessed, ""));
        }
        OcrCopyBatchResults = results;
        if (latestCopyId is long copyId)
        {
            RefreshInboxCount();
            Navigate(inboxOnly: true, preferredId: copyId);
        }
        Status = DescribeOcrBatch(added, reused, failed, remaining);
    }

    private static string DescribeOcrBatch(int added, int reused, int failed, int remaining)
    {
        var summary = new List<string>(4);
        if (added > 0) summary.Add(added == 1 ? "1 OCR copy added." : $"{added} OCR copies added.");
        if (reused > 0) summary.Add($"{reused} reused.");
        if (failed > 0) summary.Add($"{failed} failed.");
        if (remaining > 0) summary.Add($"Cancelled with {DocumentCount(remaining)} left.");
        return string.Join(" ", summary);
    }

    [RelayCommand]
    private Task InspectOcrCopyBatchResultsAsync() => CanWork && HasOcrCopyBatchResults
        ? _dialogs.ShowOcrCopyBatchResultsAsync(OcrCopyBatchResults)
        : Task.CompletedTask;
}
