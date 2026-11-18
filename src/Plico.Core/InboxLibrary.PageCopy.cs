using PdfSharpCore.Pdf;
using PdfSharpCore.Pdf.IO;

namespace Plico.Core;

public sealed partial class InboxLibrary
{
    private const string PageCopySourceChanged = "The PDF changed while it was being copied. Refresh the index and choose the pages again.";
    private const int FullTurn = 360;

    /// <summary>Publishes selected pages with their embedded text, source tags, and document date.</summary>
    /// <param name="documentId">The source catalog ID.</param>
    /// <param name="selection">The validated page ranges for the source.</param>
    /// <param name="title">The title of the extracted document.</param>
    /// <param name="cancellationToken">Cancels before publication.</param>
    // O(source bytes + copied PDF objects + indexed text) processing. The writer keeps the selected object graph.
    public Task<DocumentSummary> ExtractPdfPagesAsync(long documentId, PdfPageSelection selection, string title,
        CancellationToken cancellationToken = default) => CopyPdfPagesAsync(documentId, selection, title, null, cancellationToken);

    /// <summary>Publishes a PDF copy with selected pages turned and the source tags and document date carried over.</summary>
    /// <param name="documentId">The source catalog ID.</param>
    /// <param name="selection">The validated pages to turn within the copy.</param>
    /// <param name="rotation">The turn relative to each selected page’s existing rotation.</param>
    /// <param name="title">The title of the rotated copy.</param>
    /// <param name="cancellationToken">Cancels before publication.</param>
    public Task<DocumentSummary> RotatePdfPagesAsync(long documentId, PdfPageSelection selection, PdfPageRotation rotation,
        string title, CancellationToken cancellationToken = default)
    {
        if (!Enum.IsDefined(rotation)) throw new ArgumentOutOfRangeException(nameof(rotation));
        return CopyPdfPagesAsync(documentId, selection, title, rotation, cancellationToken);
    }

    private Task<DocumentSummary> CopyPdfPagesAsync(long documentId, PdfPageSelection selection, string title,
        PdfPageRotation? rotation, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(selection);
        string displayTitle = NormalizeTitle(title);
        return InStageAsync(async (stage, token) =>
        {
            var source = ResolveSelectedPdfSources(_catalog.ReadSelectedFiles([documentId]), token).Single();
            string snapshot = Path.Combine(stage, StagedSourceStem + DocumentProcessor.PdfExtension);
            await CopyPdfAsync(source.Path, snapshot, source.Version, PageCopySourceChanged, token).ConfigureAwait(false);

            string output = Path.Combine(stage, StagedPdfName);
            var text = rotation is { } turn
                ? SaveRotatedPdf(snapshot, output, selection, turn, token)
                : SaveExtractedPdf(snapshot, Path.GetFileName(source.Path), output, selection, token);
            VerifySelectedPdfSource(source);
            return CommitPdf(output, displayTitle, text, token, metadataSourceIds: [documentId]);
        }, cancellationToken);
    }

    private static PdfText SaveExtractedPdf(string snapshot, string sourceName, string output, PdfPageSelection selection,
        CancellationToken cancellationToken)
    {
        using var copy = CreatePageCopyDocument();
        int pages = AppendPdfPages(copy, snapshot, sourceName, cancellationToken, selection);
        return SavePageCopy(copy, output, pages, cancellationToken);
    }

    private static PdfText SaveRotatedPdf(string snapshot, string output, PdfPageSelection selection, PdfPageRotation rotation,
        CancellationToken cancellationToken)
    {
        try
        {
            using var copy = PdfReader.Open(snapshot, PdfDocumentOpenMode.Modify);
            RequireSourcePageCount(selection, copy.PageCount);
            RotateSelectedPages(copy, selection, rotation, cancellationToken);
            return SavePageCopy(copy, output, copy.PageCount, cancellationToken);
        }
        catch (PdfReaderException error)
        {
            throw new InvalidDataException("Plico could not open this PDF for rotation.", error);
        }
    }

    private static void RequireSourcePageCount(PdfPageSelection selection, int pageCount)
    {
        if (selection.SourcePageCount != pageCount)
        {
            throw new InvalidDataException("The PDF page count changed. Refresh the index and choose pages again.");
        }
    }

    // O(selected pages) time and constant extra space beyond the copied document.
    private static void RotateSelectedPages(PdfDocument document, PdfPageSelection selection, PdfPageRotation rotation,
        CancellationToken cancellationToken)
    {
        foreach (var range in selection.Ranges)
        {
            for (int index = range.FirstPage - 1; index < range.LastPage; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var page = document.Pages[index];
                // Canonical angles keep PDF viewers consistent when a source uses negative or cumulative turns.
                page.Rotate = (page.Rotate % FullTurn + (int)rotation + FullTurn) % FullTurn;
            }
        }
    }
}
