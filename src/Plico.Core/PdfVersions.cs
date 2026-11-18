namespace Plico.Core;

/// <summary>The action that saved the preceding PDF to version history.</summary>
public enum PdfRevisionKind
{
    Reorder = 1,
    DeletePages = 2,
    Rotate = 3,
    Replace = 4,
    Restore = 5,
    Unlock = 6
}

/// <summary>An archived PDF belonging to the same catalog document as the active PDF.</summary>
public sealed record PdfVersion(Guid Key, DateTimeOffset ArchivedAt, PdfRevisionKind Change, int PageCount)
{
    /// <summary>Whether opening this archived original requires its original password.</summary>
    public bool IsEncrypted => Change == PdfRevisionKind.Unlock;
}

internal sealed record PdfVersionFile(long DocumentId, PdfVersion Version, byte[] Fingerprint);

internal sealed record PendingPdfRevision(string RelativePath, PdfVersionFile Archived, byte[] ActiveFingerprint);
