using PdfSharpCore.Pdf;
using PdfSharpCore.Pdf.IO;

namespace Plico.Core;

public sealed partial class InboxLibrary
{
    private const int MinimumCombinedPdfs = 2;
    private const int CombinedPdfVersion = 17;
    private const string CombineSourceChanged = "A selected PDF changed while it was being copied. Refresh the index and combine the PDFs again.";

    /// <summary>Publishes a new PDF in the specified order and links each tag present on the source documents.</summary>
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
            using var combined = new PdfDocument { Version = CombinedPdfVersion };
            combined.Info.Creator = "Plico";
            int expectedPages = 0;
            int appended = 0;
            foreach (var source in sources)
            {
                token.ThrowIfCancellationRequested();
                string snapshot = Path.Combine(stage, Path.GetRandomFileName() + DocumentProcessor.PdfExtension);
                await CopyPdfAsync(source.Path, snapshot, source.Version, CombineSourceChanged, token).ConfigureAwait(false);
                expectedPages = checked(expectedPages + AppendPdfPages(combined, snapshot, Path.GetFileName(source.Path)));
                progress?.Report(++appended);
            }

            token.ThrowIfCancellationRequested();
            string output = Path.Combine(stage, StagedPdfName);
            using (var stream = File.Open(output, FileMode.CreateNew, FileAccess.Write, FileShare.None)) combined.Save(stream);
            var text = DocumentProcessor.ReadPdf(output, token);
            if (text.PageCount != expectedPages) throw new InvalidDataException("The combined PDF has an incomplete page count.");
            foreach (var source in sources)
            {
                token.ThrowIfCancellationRequested();
                VerifySelectedPdfSource(source);
            }
            return CommitPdf(output, displayTitle, text, token, tagSourceIds: files.Select(file => file.Id).ToArray());
        }, cancellationToken);
    }

    private static int AppendPdfPages(PdfDocument target, string snapshot, string sourceName)
    {
        try
        {
            using var input = PdfReader.Open(snapshot, PdfDocumentOpenMode.Import);
            int pages = input.PageCount;
            if (pages == 0) throw new InvalidDataException($"The PDF “{sourceName}” has no pages.");
            target.Pages.InsertRange(target.PageCount, input, 0, pages, AnnotationCopyingType.DeepCopy);
            return pages;
        }
        catch (PdfReaderException error)
        {
            throw new InvalidDataException($"Plico could not read the pages of “{sourceName}”.", error);
        }
    }
}
