namespace Plico.Core;

public sealed partial class InboxLibrary
{
    /// <summary>Exports a rasterized and newly OCRed copy with the selected regions removed from the pixels.</summary>
    public async Task<string> ExportRedactedPdfAsync(long documentId, IReadOnlyList<PdfRedactionRegion> regions,
        string destination, OcrLanguage language, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(regions);
        if (regions.Count == 0) throw new ArgumentException("Mark at least one region to redact.", nameof(regions));
        var marks = regions.ToArray();
        var result = await TransformPdfForExportAsync(documentId, destination, "redacted", (document, pdf, output, token) =>
        {
            if (marks.Any(region => !region.Fits(document.PageCount)))
                throw new ArgumentException("Each redaction must be inside an existing PDF page.", nameof(regions));
            return PdfRedactor.WriteAsync(_processor, pdf, output, document.PageCount, marks, language, token);
        }, requireSmaller: false, cancellationToken).ConfigureAwait(false);
        return result.Location!;
    }

    /// <summary>Exports a PDF with an embedded, searchable text stamp on the requested pages.</summary>
    public async Task<string> ExportStampedPdfAsync(long documentId, PdfPageSelection pages, PdfStamp stamp, string destination,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(pages);
        ArgumentNullException.ThrowIfNull(stamp);
        string label = stamp.Validate();
        var result = await TransformPdfForExportAsync(documentId, destination, "stamped", (document, pdf, output, token) =>
        {
            RequireSourcePageCount(pages, document.PageCount);
            PdfStamper.Write(pdf, output, pages, label, stamp.Position, token);
            return Task.CompletedTask;
        }, requireSmaller: false, cancellationToken).ConfigureAwait(false);
        return result.Location!;
    }

    /// <summary>Exports the result when optimizing the PDF saved space. Library PDFs are preserved.</summary>
    public Task<PdfCompressionResult> ExportCompressedPdfAsync(long documentId, PdfCompressionMode mode, string destination,
        CancellationToken cancellationToken = default)
    {
        if (!Enum.IsDefined(mode)) throw new ArgumentOutOfRangeException(nameof(mode));
        return TransformPdfForExportAsync(documentId, destination, "compressed", (document, pdf, output, token) =>
        {
            PdfCompressor.Write(pdf, output, mode, token);
            return Task.CompletedTask;
        }, requireSmaller: true, cancellationToken);
    }

    /// <summary>Renders the visible page in the same orientation used for redaction.</summary>
    public Task<MemoryStream> RenderRedactionPreviewAsync(DocumentSummary document, int pageNumber, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentOutOfRangeException.ThrowIfLessThan(pageNumber, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(pageNumber, document.PageCount);
        return RenderPageAsync(document, pageNumber, PdfRedactor.RenderPreviewAsync, cancellationToken);
    }

    private Task<PdfCompressionResult> TransformPdfForExportAsync(long documentId, string destination, string suffix,
        Func<DocumentSummary, string, string, CancellationToken, Task> transform, bool requireSmaller, CancellationToken cancellationToken) =>
        InStageAsync(async (stage, token) =>
        {
            string target = ResolveExternalTarget(destination);
            var document = _catalog.Get(documentId);
            var source = ResolveSelectedPdfSources(_catalog.ReadSelectedFiles([documentId]), token).Single();
            string snapshot = Path.Combine(stage, StagedSourceStem + DocumentProcessor.PdfExtension);
            byte[] copiedHash = await CopyAndFingerprintAsync(source.Path, snapshot, token).ConfigureAwait(false);
            if (_catalog.ReadCurrentPdfFingerprint(documentId) is { } indexed && !indexed.AsSpan().SequenceEqual(copiedHash))
                throw new IOException(ExportSourceChanged);
            string output = Path.Combine(stage, StagedPdfName);
            await transform(document, snapshot, output, token).ConfigureAwait(false);
            if (DocumentProcessor.WithReadablePdf(output, pdf => pdf.NumberOfPages) != document.PageCount)
                throw new InvalidDataException("The exported PDF has a different page count.");

            VerifySelectedPdfSource(source);
            var resultFile = FileVersion.Read(output);
            if (requireSmaller && resultFile.Length >= source.Version.Length)
                return new PdfCompressionResult(null, source.Version.Length, resultFile.Length);

            string exportStage = CreateCopyStage(target, ExportStagePrefix);
            try
            {
                // Only the completed derivative leaves private library staging.
                string stagedResult = Path.Combine(exportStage, StagedPdfName);
                await CopyPdfAsync(output, stagedResult, resultFile, ExportSourceChanged, token).ConfigureAwait(false);
                if (Path.GetRelativePath(target, ResolveExternalTarget(target)) != ".")
                    throw new IOException("The export folder changed during the copy. Choose it again.");
                string path = PublishExportFile(stagedResult, target, PortableStem($"{document.Title} ({suffix})"),
                    DocumentProcessor.PdfExtension, token);
                return new PdfCompressionResult(path, source.Version.Length, resultFile.Length);
            }
            finally
            {
                TryRemoveStage(exportStage);
            }
        }, cancellationToken);
}
