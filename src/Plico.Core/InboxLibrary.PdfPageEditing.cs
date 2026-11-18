using System.Globalization;
using PdfSharpCore.Pdf;
using PdfSharpCore.Pdf.IO;

namespace Plico.Core;

public sealed partial class InboxLibrary
{
    /// <summary>Saves a PDF version with all pages arranged in the supplied one-based order.</summary>
    public Task<DocumentSummary> ReorderPdfPagesAsync(long documentId, IReadOnlyList<int> pageOrder,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(pageOrder);
        int[] order = pageOrder.ToArray();
        return ChangeActivePdfAsync(documentId, PdfRevisionKind.Reorder, (document, source, output, token) =>
        {
            RequirePagePermutation(order, document.PageCount);
            return Task.FromResult(EditPageTree(source, output, order, document.PageCount, token));
        }, cancellationToken);
    }

    /// <summary>Saves a PDF version with the selected pages removed. At least one page must remain.</summary>
    public Task<DocumentSummary> DeletePdfPagesAsync(long documentId, PdfPageSelection selection,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(selection);
        return ChangeActivePdfAsync(documentId, PdfRevisionKind.DeletePages, (document, source, output, token) =>
        {
            RequireSourcePageCount(selection, document.PageCount);
            int keptCount = document.PageCount - selection.SelectedPageCount;
            if (keptCount == 0) throw new ArgumentException("Keep at least one page in the PDF.", nameof(selection));
            var kept = new int[keptCount];
            int writeIndex = 0;
            int page = 1;
            foreach (var range in selection.Ranges)
            {
                while (page < range.FirstPage) kept[writeIndex++] = page++;
                page = range.LastPage + 1;
            }
            while (page <= document.PageCount) kept[writeIndex++] = page++;
            return Task.FromResult(EditPageTree(source, output, kept, document.PageCount, token));
        }, cancellationToken);
    }

    /// <summary>Saves a PDF version with the selected pages rotated and the previous file intact in history.</summary>
    public Task<DocumentSummary> RotatePdfVersionAsync(long documentId, PdfPageSelection selection, PdfPageRotation rotation,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(selection);
        if (!Enum.IsDefined(rotation)) throw new ArgumentOutOfRangeException(nameof(rotation));
        return ChangeActivePdfAsync(documentId, PdfRevisionKind.Rotate,
            (document, source, output, token) => Task.FromResult(SaveRotatedPdf(source, output, selection, rotation, token)),
            cancellationToken);
    }

    /// <summary>Creates consecutive documents at the requested split points and keeps the original in the library.</summary>
    /// <param name="documentId">The source catalog ID.</param>
    /// <param name="partStartPages">One-based first pages for new parts, excluding page one.</param>
    /// <param name="cancellationToken">Cancels preparation before publishing any parts.</param>
    // O(copied PDF objects + extracted text + s log s) time for s split points. Staged files use disk; indexed text is retained until publication.
    public Task<IReadOnlyList<DocumentSummary>> SplitPdfAsync(long documentId, IReadOnlyList<int> partStartPages,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(partStartPages);
        int[] splitPoints = partStartPages.Distinct().Order().ToArray();
        if (splitPoints.Length == 0) throw new ArgumentException("Choose at least one split point.", nameof(partStartPages));
        return InStageAsync<IReadOnlyList<DocumentSummary>>(async (stage, token) =>
        {
            var document = _catalog.Get(documentId);
            if (splitPoints[0] <= 1 || splitPoints[^1] > document.PageCount)
                throw new ArgumentException("Split points are first pages between page 2 and the end of the PDF.", nameof(partStartPages));
            var source = ResolveSelectedPdfSources(_catalog.ReadSelectedFiles([documentId]), token).Single();
            string snapshot = Path.Combine(stage, StagedSourceStem + DocumentProcessor.PdfExtension);
            await CopyPdfAsync(source.Path, snapshot, source.Version, VersionSourceChanged, token).ConfigureAwait(false);
            var parts = new (string Path, PdfText Text)[splitPoints.Length + 1];
            using (var pdf = PdfReader.Open(snapshot, PdfDocumentOpenMode.Import))
            {
                if (pdf.PageCount != document.PageCount) throw new InvalidDataException(VersionSourceChanged);
                int start = 1;
                for (int index = 0; index < parts.Length; index++)
                {
                    token.ThrowIfCancellationRequested();
                    int end = index == splitPoints.Length ? pdf.PageCount : splitPoints[index] - 1;
                    string output = Path.Combine(stage, $"part-{index.ToString(CultureInfo.InvariantCulture)}.pdf");
                    using var part = CreatePageCopyDocument();
                    // Split destinations have different page trees, so cross-page annotations cannot follow the copies.
                    part.Pages.InsertRange(0, pdf, start - 1, end - start + 1, AnnotationCopyingType.DoNotCopy);
                    parts[index] = (output, SavePageCopy(part, output, end - start + 1, token));
                    start = end + 1;
                }
            }
            VerifySelectedPdfSource(source);
            token.ThrowIfCancellationRequested();
            var results = new List<DocumentSummary>(parts.Length);
            try
            {
                for (int index = 0; index < parts.Length; index++)
                {
                    var part = parts[index];
                    results.Add(CommitPdf(part.Path, $"{document.Title} (part {index + 1})", part.Text, CancellationToken.None,
                        metadataSourceIds: [documentId]));
                }
            }
            catch (Exception error) when (IsFileError(error) || error is Microsoft.Data.Sqlite.SqliteException)
            {
                throw new IOException($"Saved {results.Count} of {parts.Length} parts. {error.Message}", error);
            }
            return results;
        }, cancellationToken);
    }

    private static void RequirePagePermutation(IReadOnlyList<int> order, int pageCount)
    {
        if (order.Count != pageCount) throw new ArgumentException("Choose each current PDF page once.", nameof(order));
        var seen = new bool[pageCount];
        foreach (int page in order)
        {
            if (page < 1 || page > pageCount || seen[page - 1])
                throw new ArgumentException("Choose each current PDF page once.", nameof(order));
            seen[page - 1] = true;
        }
    }

    // O(page count + PDF save and index work) time and O(page count) references beyond the loaded PDF.
    private static PdfText EditPageTree(string source, string output, IReadOnlyList<int> order, int expectedSourcePages,
        CancellationToken token)
    {
        try
        {
            using var pdf = PdfReader.Open(source, PdfDocumentOpenMode.Modify);
            if (pdf.PageCount != expectedSourcePages) throw new InvalidDataException(VersionSourceChanged);
            var children = pdf.Pages.PagesArray.Elements;
            var orderedReferences = new PdfItem[order.Count];
            for (int index = 0; index < order.Count; index++)
            {
                token.ThrowIfCancellationRequested();
                orderedReferences[index] = children[order[index] - 1];
            }
            // Reuse the current page objects so links and bookmarks to retained pages keep their identity.
            children.Clear();
            foreach (var reference in orderedReferences) children.Add(reference);
            pdf.Pages.Elements.SetInteger("/Count", order.Count);
            return SavePageCopy(pdf, output, order.Count, token);
        }
        catch (PdfReaderException error)
        {
            throw new InvalidDataException("Plico could not update the PDF pages.", error);
        }
    }
}
