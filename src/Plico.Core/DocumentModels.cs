namespace Plico.Core;

/// <summary>The bundled model used to recognize text in new scans and imports.</summary>
public enum OcrLanguage { English, French, German }

/// <summary>A page of results, scoped by inbox status, a tag, or literal search terms.</summary>
public sealed record DocumentFilter(string Query = "", string? Tag = null, bool InboxOnly = false, int Offset = 0);

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

/// <summary>The existing or newly created catalog entry for a supplied file.</summary>
public sealed record FileImportResult(DocumentSummary Document, bool Added);

internal sealed record PdfText(string Body, int PageCount, bool HasTextOnEveryPage);
internal readonly record struct PreparedPdf(PdfText Text, bool CopiedSource);

internal readonly record struct ImportCandidate(long Id, string RelativePath, FileVersion Version);
internal sealed record IndexedPdf(long Id, string RelativePath, FileVersion Version, byte[]? Fingerprint);

internal readonly record struct FileVersion(long LastModifiedTicks, long Length, bool Present)
{
    internal static FileVersion Read(string path)
    {
        var file = new FileInfo(path);
        return new FileVersion(file.LastWriteTimeUtc.Ticks, file.Length, Present: true);
    }
}
