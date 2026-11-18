using System.Globalization;
using System.Text;

namespace Plico.Core;

/// <summary>Keeps searchable PDFs and their portable catalog consistent within one local library.</summary>
public sealed class InboxLibrary : IDisposable
{
    private const string PdfFolderName = "PDFs";
    private const string MetadataFolderName = ".plico";
    private const string StagePrefix = "import-";
    private const int MaximumStemBytes = 180;
    private const string NonPortableFileCharacters = "<>:\"/\\|?*";

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
            var catalog = new LibraryCatalog(Path.Combine(metadata, "catalog.sqlite3"));
            return new InboxLibrary(root, pdfs, metadata, lease, catalog);
        }
        catch
        {
            lease.Dispose();
            throw;
        }
    }

    /// <summary>Imports a copy, recognizes scan-only pages and indexes the resulting document.</summary>
    public Task<DocumentSummary> ImportAsync(string sourcePath, CancellationToken cancellationToken = default) =>
        StoreAsync(Path.GetFileNameWithoutExtension(sourcePath),
            (output, token) => _processor.PrepareAsync(sourcePath, output, token), cancellationToken);

    /// <summary>Returns a bounded page of results for the selected filters.</summary>
    public DocumentPage Find(DocumentFilter filter) => _catalog.Find(filter);

    /// <summary>Lists the tags used by PDFs currently in the library.</summary>
    public IReadOnlyList<string> GetTags() => _catalog.Tags();

    /// <summary>Assigns a tag once, treating equivalent case and Unicode forms as one tag.</summary>
    public void AddTag(long documentId, string tag) => _catalog.AddTag(documentId, tag);

    /// <summary>Removes a tag from one document.</summary>
    public void RemoveTag(long documentId, string tag) => _catalog.RemoveTag(documentId, tag);

    /// <summary>Moves a document into or out of the review queue without moving its PDF.</summary>
    public void SetReviewed(long documentId, bool reviewed) => _catalog.SetReviewed(documentId, reviewed);

    /// <summary>Resolves the catalog path within the portable PDF directory.</summary>
    public string ResolvePdf(DocumentSummary document)
    {
        var path = Path.GetFullPath(Path.Combine(PdfDirectory, document.RelativePath));
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

    /// <summary>Reconciles copied, edited, missing, and recovered PDFs with the index.</summary>
    // O(files + catalog rows + updated PDF text) time and O(catalog rows + largest updated text) space.
    public RefreshResult Refresh(CancellationToken cancellationToken = default)
    {
        _fileOperations.Wait(cancellationToken);
        try
        {
            int indexed = 0;
            var warnings = new List<string>();
            var known = _catalog.ReadFileVersions();
            var options = new EnumerationOptions
            {
                RecurseSubdirectories = true,
                AttributesToSkip = FileAttributes.ReparsePoint,
                IgnoreInaccessible = false,
                MatchCasing = MatchCasing.CaseInsensitive
            };
            foreach (var path in Directory.EnumerateFiles(PdfDirectory, "*.pdf", options))
            {
                cancellationToken.ThrowIfCancellationRequested();
                string relativePath = RelativePdfPath(path);
                var previouslyIndexed = known.GetValueOrDefault(relativePath);
                try
                {
                    var current = FileVersion.Read(path);
                    if (previouslyIndexed != current)
                    {
                        var text = DocumentProcessor.ReadPdf(path, cancellationToken);
                        if (FileVersion.Read(path) != current)
                        {
                            throw new IOException("The file changed while Plico read it. Refresh again once it is saved.");
                        }
                        _catalog.Upsert(relativePath, Path.GetFileNameWithoutExtension(path), text, current);
                        indexed++;
                    }
                    known.Remove(relativePath);
                }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidDataException)
                {
                    warnings.Add($"{relativePath} — {error.Message}");
                }
            }
            _catalog.MarkMissing(known.Where(item => item.Value.Present).Select(item => item.Key));
            return new(indexed, warnings);
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

    internal async Task<DocumentSummary> StoreAsync(
        string title,
        Func<string, CancellationToken, Task<PdfText>> prepare,
        CancellationToken cancellationToken)
    {
        await _fileOperations.WaitAsync(cancellationToken).ConfigureAwait(false);
        string stage = Path.Combine(_metadataDirectory, $"{StagePrefix}{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(stage);
            string stagedPdf = Path.Combine(stage, "ready.pdf");
            var text = await prepare(stagedPdf, cancellationToken).ConfigureAwait(false);
            var version = FileVersion.Read(stagedPdf);
            cancellationToken.ThrowIfCancellationRequested();
            string publishedPdf = PublishPdf(stagedPdf, title);
            long id = _catalog.Upsert(RelativePdfPath(publishedPdf), title, text, version);
            return _catalog.Get(id);
        }
        finally
        {
            TryRemoveStage(stage);
            _fileOperations.Release();
        }
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

    // O(k) attempts for k occupied names and O(1) file buffers because staging and PDFs share a volume.
    private string PublishPdf(string stagedPdf, string title)
    {
        string stem = PortableStem(title);
        for (int duplicate = 1; ; duplicate++)
        {
            string name = duplicate == 1 ? stem : $"{stem} ({duplicate.ToString(CultureInfo.InvariantCulture)})";
            string destination = Path.Combine(PdfDirectory, name + DocumentProcessor.PdfExtension);
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
