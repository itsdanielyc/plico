namespace Plico.Core;

/// <summary>The bundled model used to recognize text in new scans and imports.</summary>
public enum OcrLanguage { English, French, German, Spanish }

/// <summary>The order applied to all matching records before paging.</summary>
public enum DocumentSort { Automatic, Newest, Title }

/// <summary>The direction to look for another matching page from the current one.</summary>
public enum PageMatchDirection { Forward, Backward }

/// <summary>A relative right-angle turn applied to selected pages in a PDF copy.</summary>
public enum PdfPageRotation { Clockwise = 90, HalfTurn = 180, Counterclockwise = 270 }

/// <summary>A page of results, scoped by inbox status, a tag, or literal search terms.</summary>
public sealed record DocumentFilter(string Query = "", string? Tag = null, bool InboxOnly = false, int Offset = 0,
    DocumentSort Sort = DocumentSort.Automatic);

/// <summary>The catalog fields needed to select and open a document.</summary>
public sealed record DocumentSummary(
    long Id,
    string Title,
    string RelativePath,
    DateTimeOffset AddedAt,
    bool Reviewed,
    int PageCount,
    bool HasText,
    IReadOnlyList<string> Tags,
    string Match);

/// <summary>Results in display order and whether there are more at the next offset.</summary>
public sealed record DocumentPage(IReadOnlyList<DocumentSummary> Items, bool HasMore);

/// <summary>Newly indexed files, recovery of moved PDFs, and files that could not be read.</summary>
public sealed record RefreshResult(int Indexed, IReadOnlyList<string> Warnings, int Relocated = 0);

/// <summary>The portable backup folder and number of PDFs copied into it.</summary>
public sealed record BackupResult(string Location, int PdfCount);

/// <summary>The folder containing the exported PDFs and the number copied.</summary>
public sealed record ExportResult(string Location, int PdfCount);

/// <summary>The original path and reason when a batch stops at a particular PDF.</summary>
public sealed record BatchMoveFailure(string RelativePath, string Reason);

/// <summary>The files committed, already filed, and left to finish after a batch move.</summary>
public sealed record BatchMoveResult(int Moved, int AlreadyInFolder, int Remaining, bool Cancelled = false,
    BatchMoveFailure? Failure = null);

/// <summary>The number of PDFs copied so far and the total in the backup.</summary>
public readonly record struct BackupProgress(int Copied, int Total);

/// <summary>The existing or newly created catalog entry for a supplied file.</summary>
public sealed record FileImportResult(DocumentSummary Document, bool Added);

internal sealed record PdfText(string Body, int PageCount, bool HasTextOnEveryPage);
internal readonly record struct PreparedPdf(PdfText Text, bool CopiedSource);
internal readonly record struct OcrCopySource(long DocumentId, OcrLanguage Language);

internal readonly record struct CatalogFileReference(long Id, string RelativePath, FileVersion Version);
internal sealed record IndexedPdf(long Id, string RelativePath, FileVersion Version, byte[]? Fingerprint);

internal readonly record struct FileVersion(long LastModifiedTicks, long Length, bool Present)
{
    internal static FileVersion Read(string path)
    {
        var file = new FileInfo(path);
        return new FileVersion(file.LastWriteTimeUtc.Ticks, file.Length, Present: true);
    }
}
