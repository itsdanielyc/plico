namespace Plico.Core;

public sealed partial class InboxLibrary
{
    /// <summary>Imports converted content and applies source-keyed metadata while preserving existing local assignments.</summary>
    internal async Task<MigrationImportResult> ImportMigratedFileAsync(string sourcePath, MigrationSourceKey sourceKey,
        ImportedDocumentMetadata metadata, CancellationToken cancellationToken = default, OcrLanguage language = OcrLanguage.English,
        string? destinationFolder = null)
    {
        ArgumentNullException.ThrowIfNull(sourceKey);
        ArgumentNullException.ThrowIfNull(metadata);
        sourceKey.Validate();
        var write = new MigrationWrite(sourceKey, metadata, []);
        var result = await ImportSourceFileAsync(sourcePath, language, destinationFolder, write, cancellationToken).ConfigureAwait(false);
        return new(result.Document, result.Added, write.Conflicts);
    }

    private FileImportResult? ResumeMigratedPdf(MigrationWrite migration, CancellationToken token)
    {
        if (_catalog.FindMigratedDocument(migration.Key) is { } existing)
            return new(_catalog.ApplyMigrationToExisting(existing, migration), Added: false);
        if (_catalog.ReadPendingMigration(migration.Key) is not { } pending) return null;
        string file = ResolvePdfPath(pending.RelativePath);
        if (CheckMovedPdf(file, pending.PdfFingerprint, token) is not { } version)
        {
            _catalog.ClearPendingMigration(migration.Key);
            return null;
        }

        if (_catalog.FindIndexedPdfAtPath(pending.RelativePath) is { } indexed)
        {
            if (!indexed.Version.Present || !pending.PdfFingerprint.AsSpan().SequenceEqual(indexed.Fingerprint))
                throw new IOException("Refresh the index before resuming the migrated PDF at this path.");
            return new(_catalog.ApplyMigrationToExisting(indexed.Id, migration), Added: false);
        }

        var text = DocumentProcessor.ReadPdf(file, token);
        EnsureCurrentFile(file, version);
        long id = _catalog.Upsert(pending.RelativePath, Path.GetFileNameWithoutExtension(file), text, version,
            pending.PdfFingerprint, pending.SourceFingerprint, migration: migration);
        return new(_catalog.Get(id), Added: true);
    }
}
