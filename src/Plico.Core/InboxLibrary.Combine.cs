using PdfSharpCore.Pdf;
using PdfSharpCore.Pdf.IO;

namespace Plico.Core;

public sealed partial class InboxLibrary
{
    private const int MinimumCombinedPdfs = 2;
    private const int PdfPageCopyVersion = 17;
    private const string CombineSourceChanged = "A selected PDF changed while it was being copied. Refresh the index and combine the PDFs again.";

    /// <summary>Publishes a PDF with the selected pages, source tags, and a shared document date when present on every source.</summary>
    /// <param name="documentIds">The ordered source IDs. Each document contributes its pages once.</param>
    /// <param name="title">The title of the combined document.</param>
    /// <param name="progress">Receives the number of PDFs whose pages have been appended.</param>
    /// <param name="cancellationToken">Cancels the combination before publication.</param>
    // O(source bytes + copied PDF objects + indexed text) processing, plus ordered catalog lookup.
    // The writer holds the combined object graph. Staging uses disk snapshots and a bounded copy buffer.
    public Task<DocumentSummary> CombinePdfsAsync(IReadOnlyList<long> documentIds, string title,
        IProgress<int>? progress = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(documentIds);
        if (documentIds.Count < MinimumCombinedPdfs) throw new ArgumentException("Select at least two PDFs to combine.", nameof(documentIds));
        long[] selection = documentIds.ToArray();
        string displayTitle = NormalizeTitle(title);
        return InStageAsync(async (stage, token) =>
        {
            var files = _catalog.ReadSelectedFiles(selection);
            if (files.Count < MinimumCombinedPdfs) throw new ArgumentException("Select at least two distinct PDFs to combine.", nameof(documentIds));
            var sources = ResolveSelectedPdfSources(files, token);
            using var combined = CreatePageCopyDocument();
            int expectedPages = 0;
            int appended = 0;
            foreach (var source in sources)
            {
                token.ThrowIfCancellationRequested();
                string snapshot = Path.Combine(stage, Path.GetRandomFileName() + DocumentProcessor.PdfExtension);
                await CopyPdfAsync(source.Path, snapshot, source.Version, CombineSourceChanged, token).ConfigureAwait(false);
                expectedPages = checked(expectedPages + AppendPdfPages(combined, snapshot, Path.GetFileName(source.Path), token));
                progress?.Report(++appended);
            }

            string output = Path.Combine(stage, StagedPdfName);
            var text = SavePageCopy(combined, output, expectedPages, token);
            foreach (var source in sources)
            {
                token.ThrowIfCancellationRequested();
                VerifySelectedPdfSource(source);
            }
            return CommitPdf(output, displayTitle, text, token, metadataSourceIds: files.Select(file => file.Id).ToArray());
        }, cancellationToken);
    }

    private static PdfDocument CreatePageCopyDocument()
    {
        var document = new PdfDocument { Version = PdfPageCopyVersion };
        document.Info.Creator = "Plico";
        return document;
    }

    private static PdfText SavePageCopy(PdfDocument document, string output, int expectedPages, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using (var stream = File.Open(output, FileMode.CreateNew, FileAccess.Write, FileShare.None)) document.Save(stream);
        var text = DocumentProcessor.ReadPdf(output, cancellationToken);
        if (text.PageCount != expectedPages) throw new InvalidDataException("The PDF copy has an unexpected page count.");
        return text;
    }

    private static int AppendPdfPages(PdfDocument target, string snapshot, string sourceName,
        CancellationToken cancellationToken, PdfPageSelection? selection = null)
    {
        try
        {
            using var input = PdfReader.Open(snapshot, PdfDocumentOpenMode.Import);
            int pages = input.PageCount;
            if (pages == 0) throw new InvalidDataException($"The PDF “{sourceName}” has no pages.");
            cancellationToken.ThrowIfCancellationRequested();
            if (selection is null)
            {
                target.Pages.InsertRange(target.PageCount, input, 0, pages, AnnotationCopyingType.DeepCopy);
                return pages;
            }
            RequireSourcePageCount(selection, pages);
            foreach (var range in selection.Ranges)
            {
                cancellationToken.ThrowIfCancellationRequested();
                // Annotations can link to pages outside the excerpt, so the range copy uses page content only.
                target.Pages.InsertRange(target.PageCount, input, range.FirstPage - 1, range.Count, AnnotationCopyingType.DoNotCopy);
            }
            return selection.SelectedPageCount;
        }
        catch (PdfReaderException error)
        {
            throw new InvalidDataException($"Plico could not read the pages of “{sourceName}”.", error);
        }
    }
}
