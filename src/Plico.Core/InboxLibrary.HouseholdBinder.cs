using PdfSharpCore.Pdf;
using PdfSharpCore.Pdf.IO;

namespace Plico.Core;

public sealed partial class InboxLibrary
{
    /// <summary>Exports the selected PDFs in order with a searchable contents section.</summary>
    /// <param name="documentIds">The ordered catalog IDs. Repeated IDs contribute pages once.</param>
    /// <param name="title">The binder title and basis for its filename.</param>
    /// <param name="destination">An existing directory outside the library.</param>
    /// <param name="progress">Receives the number of source PDFs copied.</param>
    /// <param name="cancellationToken">Cancels the export before publication.</param>
    /// <returns>The path of the published binder, with a numeric suffix if the name was occupied.</returns>
    // O(source bytes + copied PDF objects + title length) time, plus selected catalog lookups.
    // O(combined PDF objects + titles) memory and one copy buffer. Source snapshots are staged on disk.
    public async Task<string> ExportHouseholdBinderAsync(IReadOnlyList<long> documentIds, string title, string destination,
        IProgress<int>? progress = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(documentIds);
        if (documentIds.Count == 0) throw new ArgumentException("Select at least one PDF for the binder.", nameof(documentIds));
        var selection = documentIds.ToArray();
        string displayTitle = NormalizeTitle(title);
        await _fileOperations.WaitAsync(cancellationToken).ConfigureAwait(false);
        string? stage = null;
        try
        {
            string target = ResolveExternalTarget(destination);
            var documents = _catalog.ReadBinderSelection(selection);
            var sources = ResolveSelectedPdfSources(documents.Select(document => document.File).ToArray(), cancellationToken);
            stage = CreateCopyStage(target, ExportStagePrefix);
            using var binder = CreatePageCopyDocument();
            binder.Info.Title = displayTitle;
            var entries = new BinderContentsEntry[sources.Count];
            int sourcePageCount = 0;
            for (int index = 0; index < sources.Count; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var source = sources[index];
                string snapshot = Path.Combine(stage, Path.GetRandomFileName() + DocumentProcessor.PdfExtension);
                await CopyPdfAsync(source.Path, snapshot, source.Version, ExportSourceChanged, cancellationToken).ConfigureAwait(false);
                entries[index] = new BinderContentsEntry(documents[index].Title, checked(sourcePageCount + 1));
                sourcePageCount = checked(sourcePageCount + AppendPdfPages(binder, snapshot, Path.GetFileName(source.Path), cancellationToken));
                progress?.Report(index + 1);
            }

            string output = Path.Combine(stage, StagedPdfName);
            WriteBinder(binder, displayTitle, entries, sourcePageCount, output, cancellationToken);
            foreach (var source in sources)
            {
                cancellationToken.ThrowIfCancellationRequested();
                VerifySelectedPdfSource(source);
            }
            if (Path.GetRelativePath(target, ResolveExternalTarget(target)) != ".")
                throw new IOException("The export folder changed during the copy. Choose it again.");

            return PublishExportFile(output, target, PortableStem(displayTitle), DocumentProcessor.PdfExtension, cancellationToken);
        }
        finally
        {
            if (stage is not null) TryRemoveStage(stage);
            _fileOperations.Release();
        }
    }

    private static void WriteBinder(PdfDocument binder, string title, IReadOnlyList<BinderContentsEntry> entries, int sourcePageCount,
        string output, CancellationToken cancellationToken)
    {
        using var contentsStream = new MemoryStream();
        int contentsPageCount = HouseholdBinderContents.Write(contentsStream, title, entries, cancellationToken);
        contentsStream.Position = 0;
        using var contents = PdfReader.Open(contentsStream, PdfDocumentOpenMode.Import);
        binder.Pages.InsertRange(0, contents, 0, contentsPageCount, AnnotationCopyingType.DoNotCopy);
        foreach (var entry in entries)
            binder.Outlines.Add(entry.Title, binder.Pages[contentsPageCount + entry.FirstPage - 1]);

        cancellationToken.ThrowIfCancellationRequested();
        using (var stream = File.Open(output, FileMode.CreateNew, FileAccess.Write, FileShare.None)) binder.Save(stream);
        if (DocumentProcessor.WithReadablePdf(output, pdf => pdf.NumberOfPages) != checked(contentsPageCount + sourcePageCount))
            throw new InvalidDataException("The binder has an unexpected page count.");
    }
}
