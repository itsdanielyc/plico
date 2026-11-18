using System.Text;

namespace Plico.Core;

public sealed partial class InboxLibrary
{
    private const string ExportStagePrefix = ".plico-export-";
    private const string ExportFolderLabel = "Plico export";
    private const string ExportSourceChanged = "A selected PDF changed while it was being copied. Refresh the index and export it again.";
    private const string SelectedSourceUnavailable = "Choose ordinary files in the library’s PDF folder. Refresh the index and select the PDFs again.";

    private readonly record struct SelectedPdfSource(string Path, FileVersion Version);

    /// <summary>Copies selected PDFs to a new folder under an existing destination outside the library.</summary>
    /// <param name="documentIds">The selected catalog IDs. Repeated IDs are copied once.</param>
    /// <param name="destination">The existing folder where Plico creates the export.</param>
    /// <param name="progress">Receives the number of PDFs staged so far.</param>
    /// <param name="cancellationToken">Cancels the copy before publication.</param>
    // O(n log(c) + n log(n) + copied bytes) time for n selected rows in a catalog of size c, plus directory resolution.
    // O(n) application space and a bounded stream buffer.
    public async Task<ExportResult> ExportPdfsAsync(IReadOnlyList<long> documentIds, string destination,
        IProgress<int>? progress = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(documentIds);
        if (documentIds.Count == 0) throw new ArgumentException("Select PDFs to export.", nameof(documentIds));
        string target = ResolveExternalTarget(destination);
        await _fileOperations.WaitAsync(cancellationToken).ConfigureAwait(false);
        string? stage = null;
        try
        {
            var sources = ResolveSelectedPdfSources(_catalog.ReadSelectedFiles(documentIds), cancellationToken);
            stage = CreateCopyStage(target, ExportStagePrefix);
            var nextSuffixes = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var allocatedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            int copied = 0;
            foreach (var source in sources)
            {
                cancellationToken.ThrowIfCancellationRequested();
                string output = ReserveExportPath(stage, source.Path, nextSuffixes, allocatedNames);
                await CopyPdfAsync(source.Path, output, source.Version, ExportSourceChanged, cancellationToken).ConfigureAwait(false);
                copied++;
                progress?.Report(copied);
            }

            foreach (var source in sources)
            {
                cancellationToken.ThrowIfCancellationRequested();
                VerifySelectedPdfSource(source);
            }
            return new ExportResult(PublishCopyFolder(stage, target, ExportFolderLabel, cancellationToken), sources.Count);
        }
        finally
        {
            if (stage is not null) TryRemoveStage(stage);
            _fileOperations.Release();
        }
    }

    private List<SelectedPdfSource> ResolveSelectedPdfSources(IReadOnlyList<CatalogFileReference> files, CancellationToken cancellationToken,
        string? canonicalPdfDirectory = null)
    {
        var sources = new List<SelectedPdfSource>(files.Count);
        var canonicalParents = new Dictionary<string, string>(StringComparer.Ordinal);
        string pdfDirectory = canonicalPdfDirectory ?? ResolveDirectoryLinks(PdfDirectory);
        foreach (var file in files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string path = ResolvePdfPath(file.RelativePath);
            string parent = Path.GetDirectoryName(path)!;
            if (!canonicalParents.TryGetValue(parent, out var resolvedParent))
            {
                resolvedParent = ResolveDirectoryLinks(parent);
                canonicalParents.Add(parent, resolvedParent);
            }
            string source = Path.Combine(resolvedParent, Path.GetFileName(path));
            if (!IsAtOrWithinFolder(pdfDirectory, resolvedParent)) throw new IOException(SelectedSourceUnavailable);
            var selected = new SelectedPdfSource(source, file.Version);
            VerifySelectedPdfSource(selected);
            sources.Add(selected);
        }
        return sources;
    }

    private static void VerifySelectedPdfSource(SelectedPdfSource source)
    {
        if ((File.GetAttributes(source.Path) & FileAttributes.ReparsePoint) != 0) throw new IOException(SelectedSourceUnavailable);
        EnsureCurrentFile(source.Path, source.Version);
    }

    private static string ReserveExportPath(string stage, string source, Dictionary<string, int> nextSuffixes, HashSet<string> allocatedNames)
    {
        string stem = PortableStem(Path.GetFileNameWithoutExtension(source).Normalize(NormalizationForm.FormC));
        int suffix = nextSuffixes.GetValueOrDefault(stem, 1);
        while (true)
        {
            string name = NumberedName(stem, suffix++) + DocumentProcessor.PdfExtension;
            string path = Path.Combine(stage, name);
            if (!allocatedNames.Add(name) || Path.Exists(path)) continue;
            nextSuffixes[stem] = suffix;
            return path;
        }
    }
}
