using System.Text;
using NAPS2.Images;
using NAPS2.Scan;

namespace Plico.Core;

/// <summary>Keeps searchable PDFs and their portable catalog consistent within one local library.</summary>
public sealed partial class InboxLibrary : IDisposable
{
    private const string PdfFolderName = "PDFs";
    private const string MetadataFolderName = ".plico";
    private const string CatalogFilename = "catalog.sqlite3";
    private const string StagePrefix = "import-";
    private const int MaximumStemBytes = 180;
    private const string NonPortableFileCharacters = "<>:\"/\\|?*";
    /// <summary>The maximum user-supplied catalog title length.</summary>
    public const int MaximumTitleLength = 160;

    private static readonly HashSet<string> ReservedWindowsFileNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9", "COM¹", "COM²", "COM³",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9", "LPT¹", "LPT²", "LPT³"
    };

    private readonly string _metadataDirectory;
    private readonly LibraryCatalog _catalog;
    private readonly FileStream _libraryLease;
    private readonly DocumentProcessor _processor = new(Path.Combine(AppContext.BaseDirectory, "tessdata"));
    private readonly SemaphoreSlim _fileOperations = new(1);

    private InboxLibrary(string root, string pdfDirectory, string metadataDirectory, FileStream lease, LibraryCatalog catalog)
    {
        RootPath = root;
        PdfDirectory = pdfDirectory;
        _metadataDirectory = metadataDirectory;
        _libraryLease = lease;
        _catalog = catalog;
    }

    /// <summary>The directory users copy to move the library and its organization.</summary>
    public string RootPath { get; }

    /// <summary>The ordinary folder containing the library's PDF files.</summary>
    public string PdfDirectory { get; }

    /// <summary>Opens a library in an existing directory and takes its exclusive writer lease.</summary>
    public static InboxLibrary Open(string directory)
    {
        string root = Path.GetFullPath(directory);
        if (!Directory.Exists(root)) throw new DirectoryNotFoundException($"The library folder is unavailable. {root}");

        var pdfs = EnsureLibraryDirectory(root, PdfFolderName);
        var metadata = EnsureLibraryDirectory(root, MetadataFolderName);
        FileStream lease;
        try
        {
            lease = File.Open(Path.Combine(metadata, "library.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        }
        catch (IOException error)
        {
            throw new IOException("Plico could not lock this library. Close any other copy that has this folder open.", error);
        }
        try
        {
            var catalog = new LibraryCatalog(Path.Combine(metadata, CatalogFilename));
            return new InboxLibrary(root, pdfs, metadata, lease, catalog);
        }
        catch
        {
            lease.Dispose();
            throw;
        }
    }

    /// <summary>Imports a copy, recognizes scan-only pages and indexes the resulting document.</summary>
    public async Task<DocumentSummary> ImportAsync(string sourcePath, CancellationToken cancellationToken = default,
        OcrLanguage language = OcrLanguage.English) =>
        (await ImportFileAsync(sourcePath, cancellationToken, language).ConfigureAwait(false)).Document;

    /// <summary>Captures a scan job as a single searchable PDF in the inbox.</summary>
    public Task<DocumentSummary> ScanAsync(ScanRequest request, CancellationToken cancellationToken = default,
        OcrLanguage language = OcrLanguage.English)
    {
        var options = Scanners.CaptureOptions(request);
        return StoreScanAsync(Scanners.ScanTitle(), (context, token) => new ScanController(context).Scan(options, token),
            cancellationToken, language);
    }

    internal Task<DocumentSummary> StoreScanAsync(
        string title,
        Func<ScanningContext, CancellationToken, IAsyncEnumerable<ProcessedImage>> capture,
        CancellationToken cancellationToken, OcrLanguage language = OcrLanguage.English) =>
        InStageAsync(async (stage, token) =>
        {
            using var context = DocumentProcessor.CreateCaptureContext(stage);
            string output = Path.Combine(stage, StagedPdfName);
            var text = await _processor.ExportAsync(context, capture(context, token), output, language, token).ConfigureAwait(false);
            return CommitPdf(output, title, text, token);
        }, cancellationToken);

    /// <summary>Returns a bounded page of results for the selected filters.</summary>
    public DocumentPage Find(DocumentFilter filter) => _catalog.Find(filter);

    /// <summary>Returns a bounded page of results and honours cancellation during the catalog read.</summary>
    public DocumentPage Find(DocumentFilter filter, CancellationToken cancellationToken) => _catalog.Find(filter, cancellationToken);

    /// <summary>Returns the extracted text from all indexed pages, or an empty string for a PDF without text.</summary>
    /// <exception cref="InvalidOperationException">The document is unavailable in the catalog.</exception>
    public string ReadIndexedText(long documentId) => _catalog.ReadIndexedText(documentId);

    /// <summary>Returns the first one-based page whose embedded text matches the full search query, or null.</summary>
    public int? FindFirstMatchingPage(DocumentSummary document, string query, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        string expression = SearchTerms.FullTextExpression(query);
        if (expression.Length == 0) return null;

        string path = ResolvePdf(document);
        var version = FileVersion.Read(path);
        int? page = PdfPageSearch.FindFirstMatch(path, expression, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        EnsureCurrentFile(path, version);
        return page;
    }

    /// <summary>Lists the tags used by PDFs currently in the library.</summary>
    public IReadOnlyList<string> GetTags() => _catalog.Tags();

    /// <summary>Assigns a tag once, treating equivalent case and Unicode forms as one tag.</summary>
    public void AddTag(long documentId, string tag) => _catalog.AddTag(documentId, tag);

    /// <summary>Applies the tag to the entire selection in a single transaction.</summary>
    public void AddTagToDocuments(IReadOnlyList<long> documentIds, string tag)
    {
        ArgumentNullException.ThrowIfNull(documentIds);
        if (documentIds.Count > 0) _catalog.AddTagToDocuments(documentIds, tag);
    }

    /// <summary>Removes a tag from one document.</summary>
    public void RemoveTag(long documentId, string tag) => _catalog.RemoveTag(documentId, tag);

    /// <summary>Moves a document into or out of the review queue without moving its PDF.</summary>
    public void SetReviewed(long documentId, bool reviewed) => _catalog.SetReviewed(documentId, reviewed);

    /// <summary>Updates the review state of the whole selection in a single transaction.</summary>
    public void SetReviewedDocuments(IReadOnlyList<long> documentIds, bool reviewed)
    {
        ArgumentNullException.ThrowIfNull(documentIds);
        if (documentIds.Count > 0) _catalog.SetReviewedDocuments(documentIds, reviewed);
    }

    /// <summary>Updates the label and indexes it alongside the PDF filename.</summary>
    public void SetTitle(long documentId, string title)
    {
        string displayTitle = NormalizeTitle(title);
        string filename = Path.GetFileNameWithoutExtension(_catalog.Get(documentId).RelativePath);
        _catalog.SetTitle(documentId, displayTitle, filename);
    }

    /// <summary>Validates and normalizes a title entered in the desktop app.</summary>
    public static string NormalizeTitle(string title)
    {
        ArgumentNullException.ThrowIfNull(title);
        string cleaned = title.Trim().Normalize(NormalizationForm.FormC);
        if (cleaned.Length is 0 or > MaximumTitleLength || cleaned.Any(char.IsControl))
        {
            throw new ArgumentException($"Enter a title of 1 to {MaximumTitleLength} printable characters.", nameof(title));
        }
        return cleaned;
    }

    /// <summary>Resolves the catalog path within the portable PDF directory.</summary>
    public string ResolvePdf(DocumentSummary document) => ResolvePdfPath(document.RelativePath);

    private string ResolvePdfPath(string relativePath)
    {
        var path = Path.GetFullPath(Path.Combine(PdfDirectory, relativePath));
        var relative = Path.GetRelativePath(PdfDirectory, path);
        if (relative is "." or ".." || Path.IsPathRooted(relative) ||
            relative.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
        {
            throw new InvalidDataException("This document points outside the library.");
        }
        return path;
    }

    /// <summary>Renders the requested page as PNG. Page numbers start at one, and callers own the stream.</summary>
    public Task<MemoryStream> RenderPageAsync(DocumentSummary document, int pageNumber, CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(pageNumber, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(pageNumber, document.PageCount);
        return DocumentProcessor.RenderPageAsync(ResolvePdf(document), pageNumber, cancellationToken);
    }

    /// <summary>Reconciles copied, edited, moved, and missing PDFs with the index.</summary>
    public RefreshResult Refresh(CancellationToken cancellationToken = default)
    {
        _fileOperations.Wait(cancellationToken);
        try
        {
            return ReconcileFiles(cancellationToken);
        }
        finally
        {
            _fileOperations.Release();
        }
    }

    /// <summary>Releases the library so another Plico instance can open or move it.</summary>
    public void Dispose()
    {
        _libraryLease.Dispose();
        _fileOperations.Dispose();
    }

    private async Task<TResult> InStageAsync<TResult>(
        Func<string, CancellationToken, Task<TResult>> operation,
        CancellationToken cancellationToken)
    {
        await _fileOperations.WaitAsync(cancellationToken).ConfigureAwait(false);
        string stage = Path.Combine(_metadataDirectory, $"{StagePrefix}{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(stage);
            return await operation(stage, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            TryRemoveStage(stage);
            _fileOperations.Release();
        }
    }

    private DocumentSummary CommitPdf(string stagedPdf, string title, PdfText text, CancellationToken cancellationToken,
        byte[]? sourceFingerprint = null, byte[]? pdfFingerprint = null, long? copyTagsFromDocumentId = null)
    {
        var version = FileVersion.Read(stagedPdf);
        var fingerprint = pdfFingerprint ?? FingerprintPdf(stagedPdf, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        string publishedPdf = PublishPdf(stagedPdf, title);
        long id = _catalog.Upsert(RelativePdfPath(publishedPdf), title, text, version, fingerprint, sourceFingerprint, copyTagsFromDocumentId);
        return _catalog.Get(id);
    }

    private static string EnsureLibraryDirectory(string root, string folder)
    {
        var directory = Directory.CreateDirectory(Path.Combine(root, folder));
        if ((directory.Attributes & FileAttributes.ReparsePoint) != 0)
        {
            throw new IOException($"Choose a library with an ordinary {folder} directory.");
        }
        return directory.FullName;
    }

    private string RelativePdfPath(string path) =>
        Path.GetRelativePath(PdfDirectory, path).Replace(Path.DirectorySeparatorChar, '/');

    // O(k log n) catalog lookups for k occupied names among n entries. The move uses a constant file buffer.
    private string PublishPdf(string stagedPdf, string title)
    {
        string stem = PortableStem(title);
        for (int duplicate = 1; ; duplicate++)
        {
            string name = NumberedName(stem, duplicate);
            string destination = Path.Combine(PdfDirectory, name + DocumentProcessor.PdfExtension);
            if (_catalog.ReservesPath(RelativePdfPath(destination))) continue;
            try
            {
                File.Move(stagedPdf, destination, overwrite: false);
                return destination;
            }
            catch (IOException) when (Path.Exists(destination))
            {
                // An existing file or a concurrent external copy can take the name between attempts.
            }
        }
    }

    private static string PortableStem(string title)
    {
        int bytes = 0;
        var stem = new StringBuilder();
        foreach (var rune in title.EnumerateRunes())
        {
            if (Rune.IsControl(rune) || (rune.IsAscii && NonPortableFileCharacters.Contains((char)rune.Value)))
            {
                stem.Append('_');
                bytes++;
            }
            else
            {
                if (bytes + rune.Utf8SequenceLength > MaximumStemBytes) break;
                stem.Append(rune);
                bytes += rune.Utf8SequenceLength;
            }
            if (bytes >= MaximumStemBytes) break;
        }
        string trimmed = stem.ToString().Trim().Trim('.');
        if (trimmed.Length == 0) return "Document";
        return ReservedWindowsFileNames.Contains(trimmed.Split('.', 2)[0].TrimEnd()) ? $"_{trimmed}" : trimmed;
    }

    private static void TryRemoveStage(string stage)
    {
        try
        {
            if (Directory.Exists(stage)) Directory.Delete(stage, recursive: true);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            // A scanner or OCR child can still be releasing a handle as the operation unwinds.
        }
    }
}
