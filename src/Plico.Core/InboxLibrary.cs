using System.Text;

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

    /// <summary>The maximum length of a tag label in the catalog.</summary>
    public const int MaximumTagNameLength = SearchTerms.MaximumTagLength;

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
    // Windows PDF readers can deny a rename. The managed gate also lets an abandoned preview finish after window close.
    private readonly SemaphoreSlim _pdfFileAccess = new(1);

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
        InboxLibrary? library = null;
        try
        {
            var catalog = new LibraryCatalog(Path.Combine(metadata, CatalogFilename));
            library = new InboxLibrary(root, pdfs, metadata, lease, catalog);
            library.MoveRecoveryNotice = library.RecoverPendingPdfMove(CancellationToken.None).Notice;
            return library;
        }
        catch
        {
            if (library is null) lease.Dispose();
            else library.Dispose();
            throw;
        }
    }

    /// <summary>Imports a copy, recognizes scan-only pages and indexes the resulting document.</summary>
    public async Task<DocumentSummary> ImportAsync(string sourcePath, CancellationToken cancellationToken = default,
        OcrLanguage language = OcrLanguage.English) =>
        (await ImportFileAsync(sourcePath, cancellationToken, language).ConfigureAwait(false)).Document;

    /// <summary>Returns a bounded page of results for the selected filters.</summary>
    public DocumentPage Find(DocumentFilter filter) => _catalog.Find(filter);

    /// <summary>Returns a bounded page of results and honours cancellation during the catalog read.</summary>
    public DocumentPage Find(DocumentFilter filter, CancellationToken cancellationToken) => _catalog.Find(filter, cancellationToken);

    /// <summary>Returns the extracted text from all indexed pages, or an empty string for a PDF without text.</summary>
    /// <exception cref="InvalidOperationException">The document is unavailable in the catalog.</exception>
    public string ReadIndexedText(long documentId) => _catalog.ReadIndexedText(documentId);

    /// <summary>Reads the embedded text on one page of the library PDF, or an empty string for a page without text.</summary>
    /// <param name="document">The current library record for the PDF.</param>
    /// <param name="pageNumber">The one-based page number.</param>
    /// <param name="cancellationToken">Cancels the request before its text is returned.</param>
    /// <exception cref="ArgumentOutOfRangeException">The page falls outside the catalog record.</exception>
    /// <exception cref="InvalidDataException">The PDF cannot be read or its page count no longer includes the page.</exception>
    public string ReadPageText(DocumentSummary document, int pageNumber, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentOutOfRangeException.ThrowIfLessThan(pageNumber, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(pageNumber, document.PageCount);
        return ReadStablePdf(document, path => DocumentProcessor.ReadPageText(path, pageNumber, cancellationToken), cancellationToken);
    }

    /// <summary>Returns the first one-based page whose embedded text matches the text terms in the query, or null.</summary>
    public int? FindFirstMatchingPage(DocumentSummary document, string query, CancellationToken cancellationToken = default) =>
        FindMatchingPage(document, query, 0, PageMatchDirection.Forward, cancellationToken);

    /// <summary>Returns the closest matching page strictly before or after the one-based anchor, or null when none remains.</summary>
    public int? FindAdjacentMatchingPage(DocumentSummary document, string query, int anchorPage, PageMatchDirection direction,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentOutOfRangeException.ThrowIfLessThan(anchorPage, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(anchorPage, document.PageCount);
        if (direction is not (PageMatchDirection.Forward or PageMatchDirection.Backward))
        {
            throw new ArgumentOutOfRangeException(nameof(direction));
        }
        return FindMatchingPage(document, query, anchorPage, direction, cancellationToken);
    }

    private int? FindMatchingPage(DocumentSummary document, string query, int anchorPage, PageMatchDirection direction,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var search = SearchTerms.Compile(query);
        if (!search.IsValid || search.FullTextExpression.Length == 0) return null;

        return ReadStablePdf(document,
            path => PdfPageSearch.FindMatch(path, search.FullTextExpression, anchorPage, direction, cancellationToken), cancellationToken);
    }

    private TResult ReadStablePdf<TResult>(DocumentSummary document, Func<string, TResult> read, CancellationToken cancellationToken)
    {
        _pdfFileAccess.Wait(cancellationToken);
        try
        {
            string path = ResolvePdf(document);
            var version = FileVersion.Read(path);
            TResult result = read(path);
            cancellationToken.ThrowIfCancellationRequested();
            EnsureCurrentFile(path, version);
            return result;
        }
        finally
        {
            _pdfFileAccess.Release();
        }
    }

    /// <summary>Lists the tags used by PDFs currently in the library.</summary>
    public IReadOnlyList<string> GetTags() => _catalog.Tags();

    /// <summary>Counts present PDFs that are awaiting review in the library.</summary>
    public long CountUnreviewedDocuments() => _catalog.CountUnreviewedDocuments();

    /// <summary>Counts catalog records whose PDFs were unavailable at the last refresh.</summary>
    public long CountUnavailableDocuments() => _catalog.CountUnavailableDocuments();

    /// <summary>Returns the cataloged paths of unavailable PDFs, relative to the library's PDF directory.</summary>
    public IReadOnlyList<string> ReadUnavailablePaths(CancellationToken cancellationToken = default) =>
        _catalog.ReadUnavailablePaths(cancellationToken);

    /// <summary>Lists tag names held by the catalog, including tags on missing documents.</summary>
    public IReadOnlyList<string> GetCatalogTagNames() => _catalog.Tags(includeUnused: true);

    /// <summary>Validates a label and returns its case-insensitive catalog key.</summary>
    public static string GetTagKey(string label) => SearchTerms.TagKey(label);

    /// <summary>Renames a tag across the catalog and returns the resulting label.</summary>
    /// <param name="currentName">The source tag's name.</param>
    /// <param name="newName">The proposed label or the name of an existing destination tag.</param>
    /// <param name="mergeIntoExisting">Permits merging into a different tag with the destination key.</param>
    /// <param name="cancellationToken">Cancels the change before the transaction commits.</param>
    public string RenameTag(string currentName, string newName, bool mergeIntoExisting = false,
        CancellationToken cancellationToken = default) =>
        _catalog.RenameTag(currentName, newName, mergeIntoExisting, cancellationToken);

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

    /// <summary>Removes a tag from the selection in one catalog transaction.</summary>
    /// <param name="documentIds">The catalog IDs of the selected, present documents.</param>
    /// <param name="tag">The tag label, matched by its normalized catalog key.</param>
    /// <param name="cancellationToken">Cancels the removal before it commits.</param>
    /// <returns>The number of documents that had the tag removed.</returns>
    /// <exception cref="InvalidOperationException">A selected document is unavailable.</exception>
    public int RemoveTagFromDocuments(IReadOnlyList<long> documentIds, string tag, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(documentIds);
        return documentIds.Count == 0 ? 0 : _catalog.RemoveTagFromDocuments(documentIds, tag, cancellationToken);
    }

    /// <summary>Moves a document into or out of the review queue without moving its PDF.</summary>
    public void SetReviewed(long documentId, bool reviewed) => _catalog.SetReviewed(documentId, reviewed);

    /// <summary>Sets or clears the archived document’s calendar date in the library catalog.</summary>
    public void SetDocumentDate(long documentId, DateOnly? date) => _catalog.SetDocumentDate(documentId, date);

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
        return RenderPageAsync(document, pageNumber, PdfPreviewRenderer.RenderPageAsync, cancellationToken);
    }

    internal async Task<MemoryStream> RenderPageAsync(DocumentSummary document, int pageNumber,
        Func<string, int, CancellationToken, Task<MemoryStream>> render, CancellationToken cancellationToken)
    {
        await _pdfFileAccess.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await render(ResolvePdf(document), pageNumber, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _pdfFileAccess.Release();
        }
    }

    /// <summary>Reconciles copied, edited, moved, and missing PDFs with the index.</summary>
    public RefreshResult Refresh(CancellationToken cancellationToken = default)
    {
        _fileOperations.Wait(cancellationToken);
        try
        {
            var recovery = RecoverPendingPdfMove(cancellationToken);
            var result = ReconcileFiles(cancellationToken, recovery.PendingDestination);
            return recovery.Notice is { } notice ? result with { Warnings = [notice, .. result.Warnings] } : result;
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
        byte[]? sourceFingerprint = null, byte[]? pdfFingerprint = null, OcrCopySource? ocrCopySource = null,
        IReadOnlyList<long>? metadataSourceIds = null, string? destinationFolder = null)
    {
        var version = FileVersion.Read(stagedPdf);
        var fingerprint = pdfFingerprint ?? FingerprintPdf(stagedPdf, cancellationToken);
        string target = destinationFolder is null ? PdfDirectory : ResolveIntakeDirectory(destinationFolder);
        if (destinationFolder is not null && Path.GetRelativePath(destinationFolder, target) != ".")
        {
            throw new IOException("The intake folder changed during processing. Select it again and retry.");
        }
        cancellationToken.ThrowIfCancellationRequested();
        string publishedPdf = PublishPdf(stagedPdf, title, target);
        long id = _catalog.Upsert(RelativePdfPath(publishedPdf), title, text, version, fingerprint, sourceFingerprint, ocrCopySource, metadataSourceIds);
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
    private string PublishPdf(string stagedPdf, string title, string destinationFolder)
    {
        string stem = PortableStem(title);
        for (int duplicate = 1; ; duplicate++)
        {
            string name = NumberedName(stem, duplicate);
            string destination = Path.Combine(destinationFolder, name + DocumentProcessor.PdfExtension);
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
