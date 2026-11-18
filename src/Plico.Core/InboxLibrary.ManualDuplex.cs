using System.Text.Json;
using NAPS2.Images;
using NAPS2.Images.Transforms;
using NAPS2.ImportExport;
using NAPS2.Pdf;
using NAPS2.Scan;

namespace Plico.Core;

public sealed partial class InboxLibrary
{
    private const string PendingDuplexFolder = "pending-manual-duplex";
    private const string FrontSidesFile = "front-sides.pdf";
    private const string BackSidesFile = "back-sides.pdf";
    private const string DuplexSettingsFile = "settings.json";
    private const int BackSideRotation = 180;

    private sealed record DuplexSettings(string JobId, string Title, ScanBatchOptions? Batch, bool FlipBacks);

    private async Task<ScanBatchOutcome> FinishDuplexBatchAsync(string stage, ScanningContext context, string title,
        Func<ScanningContext, CancellationToken, IAsyncEnumerable<ProcessedImage>> capture,
        Func<ManualDuplexPrompt, CancellationToken, Task<ManualDuplexDecision>> chooseDuplexAction,
        ScanBatchOptions? options, bool flipBacks, string? target, OcrLanguage language, CancellationToken cancellationToken)
    {
        string pending = Path.Combine(_metadataDirectory, PendingDuplexFolder);
        if (!Directory.Exists(pending))
        {
            await SaveDuplexFrontsAsync(stage, pending, context, capture, new(Guid.NewGuid().ToString("N"), title, options, flipBacks),
                cancellationToken).ConfigureAwait(false);
        }

        var settings = JsonSerializer.Deserialize<DuplexSettings>(File.ReadAllText(Path.Combine(pending, DuplexSettingsFile)))
            ?? throw new InvalidDataException("The saved manual duplex settings could not be read.");
        var fronts = await ReadDuplexPassAsync(context, Path.Combine(pending, FrontSidesFile), settings.Batch, cancellationToken)
            .ConfigureAwait(false);
        string savedBacks = Path.Combine(pending, BackSidesFile);
        if (!File.Exists(savedBacks))
        {
            var decision = await chooseDuplexAction(new ManualDuplexPrompt(fronts.Count), cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            if (decision == ManualDuplexDecision.SaveForLater) return new ScanBatchOutcome([], HasPendingDuplex: true);
            if (decision == ManualDuplexDecision.Discard)
            {
                Directory.Move(pending, Path.Combine(stage, PendingDuplexFolder));
                return new ScanBatchOutcome([]);
            }
            if (decision != ManualDuplexDecision.ScanBacks) throw new ArgumentOutOfRangeException(nameof(decision));

            var backPass = new List<ProcessedImage>();
            await DocumentProcessor.CollectPagesAsync(capture(context, cancellationToken), backPass, cancellationToken)
                .ConfigureAwait(false);
            ScanBatchPages.RequireMatchingPasses(fronts.Count, backPass.Count);
            string stagedBacks = Path.Combine(stage, BackSidesFile);
            await WriteDuplexPassAsync(context, backPass, stagedBacks, cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            File.Move(stagedBacks, savedBacks);
        }

        var backs = await ReadDuplexPassAsync(context, savedBacks, settings.Batch, cancellationToken).ConfigureAwait(false);
        if (settings.FlipBacks)
        {
            for (int index = 0; index < backs.Count; index++)
            {
                backs[index] = backs[index].WithTransform(new RotationTransform(BackSideRotation), disposeSelf: true);
            }
        }
        var batch = await FileScanBatchAsync(stage, context, settings.Title, ScanBatchPages.Interleave(fronts, backs),
            settings.Batch, target, language, $"{PendingDuplexFolder}\n{settings.JobId}", cancellationToken).ConfigureAwait(false);
        Directory.Move(pending, Path.Combine(stage, PendingDuplexFolder));
        return batch;
    }

    private static async Task SaveDuplexFrontsAsync(string stage, string pending, ScanningContext context,
        Func<ScanningContext, CancellationToken, IAsyncEnumerable<ProcessedImage>> capture, DuplexSettings settings,
        CancellationToken cancellationToken)
    {
        var fronts = new List<ProcessedImage>();
        await DocumentProcessor.CollectPagesAsync(capture(context, cancellationToken), fronts, cancellationToken).ConfigureAwait(false);
        string stagedFolder = Directory.CreateDirectory(Path.Combine(stage, PendingDuplexFolder)).FullName;
        await WriteDuplexPassAsync(context, fronts, Path.Combine(stagedFolder, FrontSidesFile), cancellationToken)
            .ConfigureAwait(false);
        File.WriteAllText(Path.Combine(stagedFolder, DuplexSettingsFile), JsonSerializer.Serialize(settings));
        cancellationToken.ThrowIfCancellationRequested();
        Directory.Move(stagedFolder, pending);
    }

    private static async Task WriteDuplexPassAsync(ScanningContext context, List<ProcessedImage> pages, string destination,
        CancellationToken cancellationToken)
    {
        if (pages.Count == 0) throw new InvalidDataException("The scanner returned no pages. Check that the feeder has paper.");
        if (!await new PdfExporter(context).Export(destination, pages, progress: cancellationToken).ConfigureAwait(false))
        {
            cancellationToken.ThrowIfCancellationRequested();
            throw new IOException("Plico could not save the manual duplex pass.");
        }
    }

    private static async Task<List<ProcessedImage>> ReadDuplexPassAsync(ScanningContext context, string path,
        ScanBatchOptions? options, CancellationToken cancellationToken)
    {
        var pages = new List<ProcessedImage>();
        var import = new ImportParams { BarcodeDetectionOptions = ScanBatchPages.BarcodeOptions(options) };
        await DocumentProcessor.CollectPagesAsync(new PdfImporter(context).Import(path, import, cancellationToken), pages, cancellationToken)
            .ConfigureAwait(false);
        return pages;
    }
}
