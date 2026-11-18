namespace Plico.Core;

/// <summary>The stable external identity of a record across conversion attempts and library moves.</summary>
internal sealed record MigrationSourceKey(string Format, string LibraryKey, string RecordKey)
{
    internal void Validate()
    {
        const int maximumKeyLength = 1_024;
        foreach (string value in new[] { Format, LibraryKey, RecordKey })
        {
            if (string.IsNullOrWhiteSpace(value) || value.Length > maximumKeyLength || value.Any(char.IsControl))
                throw new ArgumentException("The source record needs a stable migration key.");
        }
    }
}

internal sealed record ImportedTag(IReadOnlyList<string> Path, string? ColorHex = null);
internal sealed record ImportedCorrespondent(string Name, IReadOnlyList<string> Aliases);
internal abstract record ImportedFieldValue
{
    internal sealed record Value(CustomFieldValue Data, IReadOnlyList<string>? Choices = null) : ImportedFieldValue;
    internal sealed record SourceLink(MigrationSourceKey Target) : ImportedFieldValue;
}

internal sealed record ImportedCustomField(string Name, ImportedFieldValue Value);
internal sealed record ImportedDocumentMetadata(
    string? Title, DateOnly? DocumentDate, bool? Reviewed,
    IReadOnlyList<ImportedTag> Tags, IReadOnlyList<ImportedCorrespondent> Correspondents,
    string? DocumentType, string? Notes, IReadOnlyList<ImportedCustomField> Fields,
    long? ArchiveSerialNumber);

internal sealed record MigrationFieldConflict(string Field, string Message);
internal sealed record MigrationImportResult(DocumentSummary Document, bool Added, IReadOnlyList<MigrationFieldConflict> Conflicts);
internal sealed record MigrationWrite(MigrationSourceKey Key, ImportedDocumentMetadata Metadata, List<MigrationFieldConflict> Conflicts);
internal sealed record PendingMigrationPdf(string RelativePath, byte[] PdfFingerprint, byte[]? SourceFingerprint);
