using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using NAPS2.Images;
using NAPS2.Scan;

namespace Plico.Core;

public sealed partial class InboxLibrary
{
    private sealed record StagedScanDocument(string Output, string Title, byte[]? Identity, PdfText? Text, DocumentSummary? Existing);

    /// <summary>Files a capture batch, splitting at enabled barcode markers and resuming a saved manual duplex pass.</summary>
    public Task<ScanBatchOutcome> ScanBatchAsync(ScanRequest request,
        Func<ScanInterruption, CancellationToken, Task<bool>> keepCapturedPages,
        Func<ManualDuplexPrompt, CancellationToken, Task<ManualDuplexDecision>> chooseDuplexAction,
        CancellationToken cancellationToken = default, OcrLanguage language = OcrLanguage.English, string? destinationFolder = null)
    {
        ArgumentNullException.ThrowIfNull(chooseDuplexAction);
        ArgumentNullException.ThrowIfNull(keepCapturedPages);
        return StoreScanBatchAsync(Scanners.ScanTitle(), CreateScanSource(request, allowBatch: true), keepCapturedPages,
            request.Feed == ScanFeed.ManualDuplex ? chooseDuplexAction : null, request.Batch,
            request.Cleanup?.FlipBackSides == true, cancellationToken, language, destinationFolder);
    }

    internal Task<ScanBatchOutcome> StoreScanBatchAsync(string title,
        Func<ScanningContext, CancellationToken, IAsyncEnumerable<ProcessedImage>> capture,
        Func<ScanInterruption, CancellationToken, Task<bool>> keepCapturedPages,
        Func<ManualDuplexPrompt, CancellationToken, Task<ManualDuplexDecision>>? chooseDuplexAction,
        ScanBatchOptions? options, bool flipBacks, CancellationToken cancellationToken,
        OcrLanguage language = OcrLanguage.English, string? destinationFolder = null) =>
        InStageAsync(async (stage, token) =>
        {
            if (options?.SeparatorBarcode?.Length > ScanBatchPages.MaximumSeparatorLength)
            {
                throw new ArgumentException("Enter a shorter separator barcode.", nameof(options));
            }
            string? target = destinationFolder is null ? null : ResolveIntakeDirectory(destinationFolder);
            using var context = DocumentProcessor.CreateCaptureContext(stage);
            if (chooseDuplexAction is not null)
            {
                return await FinishDuplexBatchAsync(stage, context, title, capture, chooseDuplexAction, options, flipBacks,
                    target, language, token).ConfigureAwait(false);
            }

            var pages = new List<ProcessedImage>();
            var interruption = await CaptureScanPagesAsync(context, capture, pages, offerRecovery: true, token).ConfigureAwait(false);
            if (interruption is not null && !await keepCapturedPages(interruption, token).ConfigureAwait(false))
            {
                return new ScanBatchOutcome([], interruption);
            }
            string batchTitle = interruption is null ? title : $"{title} (partial)";
            var documents = await FileScanBatchAsync(stage, context, batchTitle, pages, options, target, language,
                identity: null, token).ConfigureAwait(false);
            return new ScanBatchOutcome(documents, interruption);
        }, cancellationToken);

    // O(pages + output bytes) time and O(pages) page references before OCR and publication.
    private async Task<IReadOnlyList<DocumentSummary>> FileScanBatchAsync(string stage, ScanningContext context, string title,
        List<ProcessedImage> pages, ScanBatchOptions? options, string? target, OcrLanguage language, string? identity,
        CancellationToken cancellationToken)
    {
        var groups = ScanBatchPages.Split(pages, options);
        if (groups.Count == 0)
        {
            throw new InvalidDataException("No document pages were read. Check the feeder and separator sheets.");
        }

        var staged = new List<StagedScanDocument>(groups.Count);
        for (int index = 0; index < groups.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string number = (index + 1).ToString(CultureInfo.InvariantCulture);
            string documentTitle = groups.Count == 1 ? title : $"{title} - {number}";
            string output = Path.Combine(stage, $"batch-{number}.pdf");
            byte[]? source = identity is null ? null : SHA256.HashData(Encoding.UTF8.GetBytes($"{identity}\n{number}"));
            var existing = source is null ? null : FindUnchangedCandidate(_catalog.FindSource(source), cancellationToken);
            PdfText? text = null;
            if (existing is null)
            {
                text = await _processor.ExportCapturedAsync(context, groups[index], output, language, cancellationToken)
                    .ConfigureAwait(false);
                if (text.PageCount != groups[index].Count) throw new InvalidDataException("A scan document is missing captured pages.");
            }
            staged.Add(new StagedScanDocument(output, documentTitle, source, text, existing));
        }

        var documents = new List<DocumentSummary>(groups.Count);
        foreach (var document in staged)
        {
            cancellationToken.ThrowIfCancellationRequested();
            documents.Add(document.Existing ?? CommitPdf(document.Output, document.Title, document.Text!, cancellationToken,
                document.Identity, destinationFolder: target, recognizedLanguage: language));
        }
        return documents;
    }
}
