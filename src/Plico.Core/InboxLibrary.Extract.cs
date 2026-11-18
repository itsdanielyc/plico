namespace Plico.Core;

public sealed partial class InboxLibrary
{
    private const string ExtractSourceChanged = "The PDF changed while it was being copied. Refresh the index and choose the pages again.";

    /// <summary>Publishes selected page content and embedded text into a new tagged PDF.</summary>
    /// <param name="documentId">The source catalog ID.</param>
    /// <param name="selection">The validated page ranges for the source.</param>
    /// <param name="title">The title of the extracted document.</param>
    /// <param name="cancellationToken">Cancels before publication.</param>
    // O(source bytes + copied PDF objects + indexed text) processing. The writer keeps the selected object graph.
    public Task<DocumentSummary> ExtractPdfPagesAsync(long documentId, PdfPageSelection selection, string title,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(selection);
        string displayTitle = NormalizeTitle(title);
        return InStageAsync(async (stage, token) =>
        {
            var source = ResolveSelectedPdfSources(_catalog.ReadSelectedFiles([documentId]), token).Single();
            string snapshot = Path.Combine(stage, StagedSourceStem + DocumentProcessor.PdfExtension);
            await CopyPdfAsync(source.Path, snapshot, source.Version, ExtractSourceChanged, token).ConfigureAwait(false);

            using var excerpt = CreatePageCopyDocument();
            int expectedPages = AppendPdfPages(excerpt, snapshot, Path.GetFileName(source.Path), token, selection);
            string output = Path.Combine(stage, StagedPdfName);
            var text = SavePageCopy(excerpt, output, expectedPages, token);
            VerifySelectedPdfSource(source);
            return CommitPdf(output, displayTitle, text, token, tagSourceIds: [documentId]);
        }, cancellationToken);
    }
}
