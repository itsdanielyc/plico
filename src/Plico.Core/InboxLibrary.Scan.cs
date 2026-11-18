using System.Net.Sockets;
using NAPS2.Images;
using NAPS2.Scan;
using NAPS2.Scan.Exceptions;

namespace Plico.Core;

public sealed partial class InboxLibrary
{
    /// <summary>Captures a scan job as a single searchable PDF in the inbox.</summary>
    public Task<DocumentSummary> ScanAsync(ScanRequest request, CancellationToken cancellationToken = default,
        OcrLanguage language = OcrLanguage.English) =>
        StoreScanAsync(Scanners.ScanTitle(), CreateScanSource(request), cancellationToken, language);

    /// <summary>Captures a scan and asks whether to keep completed pages if the scanner fails.</summary>
    /// <param name="request">The scanner and capture settings for this job.</param>
    /// <param name="keepCapturedPages">Chooses whether to write the available pages. The callback must honor cancellation and avoid starting another library write.</param>
    /// <param name="cancellationToken">Cancels capture, confirmation, or OCR before the PDF is published.</param>
    /// <param name="language">The bundled model used to recognize the pages.</param>
    public Task<ScanOutcome> ScanWithRecoveryAsync(ScanRequest request,
        Func<ScanInterruption, CancellationToken, Task<bool>> keepCapturedPages,
        CancellationToken cancellationToken = default, OcrLanguage language = OcrLanguage.English) =>
        ScanWithRecoveryAsync(request, keepCapturedPages, cancellationToken, language, destinationFolder: null);

    /// <summary>Captures a scan into a selected library folder and offers to keep completed pages after an interruption.</summary>
    /// <param name="request">The scanner and capture settings for this job.</param>
    /// <param name="keepCapturedPages">Chooses whether to write the available pages. The callback must honor cancellation and avoid starting another library write.</param>
    /// <param name="cancellationToken">Cancels capture, confirmation, or OCR before the PDF is published.</param>
    /// <param name="language">The bundled model used to recognize the pages.</param>
    /// <param name="destinationFolder">An existing directory within the library's PDF directory, or null for the root.</param>
    public Task<ScanOutcome> ScanWithRecoveryAsync(ScanRequest request,
        Func<ScanInterruption, CancellationToken, Task<bool>> keepCapturedPages,
        CancellationToken cancellationToken, OcrLanguage language, string? destinationFolder)
    {
        ArgumentNullException.ThrowIfNull(keepCapturedPages);
        return StoreScanWithRecoveryAsync(Scanners.ScanTitle(), CreateScanSource(request), keepCapturedPages,
            cancellationToken, language, destinationFolder);
    }

    private static Func<ScanningContext, CancellationToken, IAsyncEnumerable<ProcessedImage>> CreateScanSource(ScanRequest request,
        bool allowBatch = false)
    {
        if (request.RequiresBatchCapture && !allowBatch)
        {
            throw new ArgumentException("Use the scan batch operation for separators or manual duplex.", nameof(request));
        }
        var options = Scanners.CaptureOptions(request);
        if (request.Device.NativeDevice.Driver == Driver.Apple)
            return (context, token) => MacScannerBridge.CaptureAsync(context, options, token);
        return (context, token) => new ScanController(context).Scan(options, token);
    }

    internal async Task<DocumentSummary> StoreScanAsync(string title,
        Func<ScanningContext, CancellationToken, IAsyncEnumerable<ProcessedImage>> capture,
        CancellationToken cancellationToken, OcrLanguage language = OcrLanguage.English)
    {
        var scan = await StoreScanWithRecoveryAsync(title, capture, null, cancellationToken, language).ConfigureAwait(false);
        return scan.Document ?? throw new InvalidOperationException("A completed scan must have a PDF.");
    }

    internal Task<ScanOutcome> StoreScanWithRecoveryAsync(string title,
        Func<ScanningContext, CancellationToken, IAsyncEnumerable<ProcessedImage>> capture,
        Func<ScanInterruption, CancellationToken, Task<bool>>? keepCapturedPages,
        CancellationToken cancellationToken, OcrLanguage language = OcrLanguage.English, string? destinationFolder = null) =>
        InStageAsync(async (stage, token) =>
        {
            string? target = destinationFolder is null ? null : ResolveIntakeDirectory(destinationFolder);
            using var context = DocumentProcessor.CreateCaptureContext(stage);
            var captured = new List<ProcessedImage>();
            var interruption = await CaptureScanPagesAsync(context, capture, captured, keepCapturedPages is not null, token)
                .ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            if (interruption is not null && keepCapturedPages is not null)
            {
                bool savePages = await keepCapturedPages(interruption, token).ConfigureAwait(false);
                token.ThrowIfCancellationRequested();
                if (!savePages) return new ScanOutcome(null, interruption);
            }

            string output = Path.Combine(stage, StagedPdfName);
            var text = await _processor.ExportCapturedAsync(context, captured, output, language, token).ConfigureAwait(false);
            if (text.PageCount != captured.Count) throw new InvalidDataException("The scan PDF is missing captured pages.");
            string documentTitle = interruption is null ? title : $"{title} (partial)";
            return new ScanOutcome(CommitPdf(output, documentTitle, text, token,
                destinationFolder: target, recognizedLanguage: language), interruption);
        }, cancellationToken);

    // O(pages) references in memory. The scanning context owns the page files until the job exits.
    private static async Task<ScanInterruption?> CaptureScanPagesAsync(ScanningContext context,
        Func<ScanningContext, CancellationToken, IAsyncEnumerable<ProcessedImage>> capture, List<ProcessedImage> captured,
        bool offerRecovery, CancellationToken cancellationToken)
    {
        try
        {
            await DocumentProcessor.CollectPagesAsync(capture(context, cancellationToken), captured, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception error) when (error is ScanDriverException or HttpRequestException or IOException)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!offerRecovery || captured.Count == 0) throw;
            string reason = error is HttpRequestException or HttpIOException or IOException { InnerException: SocketException }
                ? "The network scanner stopped sending pages."
                : error.Message;
            return new ScanInterruption(captured.Count, reason);
        }
        return null;
    }
}
