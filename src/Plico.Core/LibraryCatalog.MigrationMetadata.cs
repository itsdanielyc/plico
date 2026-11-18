using Microsoft.Data.Sqlite;

namespace Plico.Core;

internal sealed partial class LibraryCatalog
{
    private const string MigrationValueSavepoint = "migration_value";
    private const string MigrationKeyPredicate = "source_format = $format AND library_key = $library AND record_key = $record";

    internal long? FindMigratedDocument(MigrationSourceKey key)
    {
        using var connection = Connect();
        return FindMigratedDocument(connection, transaction: null, key);
    }

    private static long? FindMigratedDocument(SqliteConnection connection, SqliteTransaction? transaction, MigrationSourceKey key)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"SELECT document_id FROM migrated_documents WHERE {MigrationKeyPredicate};";
        BindMigrationKey(command, key);
        return command.ExecuteScalar() as long?;
    }

    internal PendingMigrationPdf? ReadPendingMigration(MigrationSourceKey key)
    {
        using var connection = Connect();
        using var command = connection.CreateCommand();
        command.CommandText = $"SELECT relative_path, pdf_sha256, source_sha256 FROM pending_migrated_documents WHERE {MigrationKeyPredicate};";
        BindMigrationKey(command, key);
        using var row = command.ExecuteReader();
        return row.Read() ? new(row.GetString(0), row.GetFieldValue<byte[]>(1),
            row.IsDBNull(2) ? null : row.GetFieldValue<byte[]>(2)) : null;
    }

    internal void RecordPendingMigration(MigrationSourceKey key, PendingMigrationPdf pdf)
    {
        using var connection = Connect();
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO pending_migrated_documents (source_format, library_key, record_key, relative_path, pdf_sha256, source_sha256)
                VALUES ($format, $library, $record, $path, $pdf, $source)
            ON CONFLICT(source_format, library_key, record_key)
                DO UPDATE SET relative_path = excluded.relative_path, pdf_sha256 = excluded.pdf_sha256, source_sha256 = excluded.source_sha256;
            """;
        BindMigrationKey(command, key);
        command.Parameters.AddWithValue("$path", pdf.RelativePath);
        command.Parameters.AddWithValue("$pdf", pdf.PdfFingerprint);
        command.Parameters.AddWithValue("$source", DbValue(pdf.SourceFingerprint));
        command.ExecuteNonQuery();
    }

    internal void ClearPendingMigration(MigrationSourceKey key)
    {
        using var connection = Connect();
        DeletePendingMigration(connection, transaction: null, key);
    }

    private static void DeletePendingMigration(SqliteConnection connection, SqliteTransaction? transaction, MigrationSourceKey key)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"DELETE FROM pending_migrated_documents WHERE {MigrationKeyPredicate};";
        BindMigrationKey(command, key);
        command.ExecuteNonQuery();
    }

    internal IndexedPdf? FindIndexedPdfAtPath(string relativePath)
    {
        using var connection = Connect();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT id, modified_ticks, file_bytes, is_present, pdf_sha256
            FROM documents WHERE relative_path = $path AND is_present >= 0;
            """;
        command.Parameters.AddWithValue("$path", relativePath);
        using var row = command.ExecuteReader();
        if (!row.Read()) return null;
        return new(row.GetInt64(0), relativePath, new(row.GetInt64(1), row.GetInt64(2), row.GetBoolean(3)),
            row.IsDBNull(4) ? null : row.GetFieldValue<byte[]>(4));
    }

    internal DocumentSummary ApplyMigrationToExisting(long documentId, MigrationWrite write)
    {
        using var connection = Connect();
        using var transaction = connection.BeginTransaction();
        WriteMigratedMetadata(connection, transaction, documentId, write, isNew: false);
        transaction.Commit();
        return Get(documentId);
    }

    private static void WriteMigratedMetadata(SqliteConnection connection, SqliteTransaction transaction, long documentId,
        MigrationWrite write, bool isNew)
    {
        WriteMigratedBaseFields(connection, transaction, documentId, write, isNew);
        var previous = ReadDocumentMetadata(connection, transaction, documentId);
        long? typeId = previous.DocumentTypeId;
        string notes = previous.Notes;
        var contacts = previous.CorrespondentIds.ToHashSet();
        var values = previous.Fields.ToDictionary(field => field.FieldId);

        if (write.Metadata.DocumentType is { } type)
        {
            TryMigratedValue(transaction, write, "Document type", () =>
            {
                string label = CleanMetadataName(type);
                long? existing = FindMetadataDefinitionId(connection, transaction, "document_types", "id", MetadataNameKey(label));
                if (typeId is { } prior && prior != existing) throw new ArgumentException("The local document type was kept.");
                typeId = existing ?? WriteDocumentType(connection, transaction, null, label).Id;
            });
        }
        if (write.Metadata.Notes is { Length: > 0 } sourceNotes)
        {
            TryMigratedValue(transaction, write, "Notes", () =>
            {
                if (notes.Length > 0 && notes != sourceNotes) throw new ArgumentException("The local notes were kept.");
                notes = ValidateMetadataText(sourceNotes);
            });
        }
        foreach (var correspondent in write.Metadata.Correspondents)
        {
            TryMigratedValue(transaction, write, $"Correspondent {correspondent.Name}", () =>
            {
                string name = CleanMetadataName(correspondent.Name);
                long? id = FindMetadataDefinitionId(connection, transaction, "correspondent_names", "correspondent_id", MetadataNameKey(name));
                if (id is { } existing)
                {
                    var known = ReadMigrationCorrespondent(connection, transaction, existing);
                    var aliases = known.Aliases.Concat(correspondent.Aliases).Select(CleanMetadataName).DistinctBy(MetadataNameKey).ToArray();
                    WriteCorrespondent(connection, transaction, known.Id, known.Name, aliases);
                    contacts.Add(existing);
                    return;
                }
                contacts.Add(WriteCorrespondent(connection, transaction, null, name, correspondent.Aliases).Id);
            });
        }
        foreach (var tag in write.Metadata.Tags)
        {
            TryMigratedValue(transaction, write, $"Tag {string.Join(" / ", tag.Path)}",
                () => WriteMigratedTag(connection, transaction, documentId, tag));
        }
        foreach (var field in write.Metadata.Fields)
        {
            TryMigratedValue(transaction, write, field.Name, () =>
            {
                var entry = MapMigratedField(connection, transaction, documentId, field);
                if (values.TryGetValue(entry.FieldId, out var prior) && prior != entry)
                    throw new ArgumentException("The local field value was kept.");
                values[entry.FieldId] = entry;
            });
        }
        if (write.Metadata.ArchiveSerialNumber is { } serial)
        {
            TryMigratedValue(transaction, write, "Archive serial number", () =>
            {
                using var command = connection.CreateCommand();
                command.Transaction = transaction;
                command.CommandText = "SELECT asn FROM document_asns WHERE document_id = $id;";
                command.Parameters.AddWithValue("$id", documentId);
                if (command.ExecuteScalar() is long assigned && assigned != serial)
                    throw new ArgumentException("The local archive serial number was kept.");
                WriteArchiveSerialNumber(connection, transaction, documentId, serial);
            });
        }

        WriteDocumentMetadata(connection, transaction, documentId, new(typeId, contacts.ToArray(), notes, values.Values.ToArray()),
            CancellationToken.None);
        using var provenance = connection.CreateCommand();
        provenance.Transaction = transaction;
        provenance.CommandText = """
            INSERT INTO migrated_documents (source_format, library_key, record_key, document_id)
                VALUES ($format, $library, $record, $id)
            ON CONFLICT(source_format, library_key, record_key) DO NOTHING;
            """;
        BindMigrationKey(provenance, write.Key);
        provenance.Parameters.AddWithValue("$id", documentId);
        provenance.ExecuteNonQuery();
        DeletePendingMigration(connection, transaction, write.Key);
    }

    private static Correspondent ReadMigrationCorrespondent(SqliteConnection connection, SqliteTransaction transaction, long id)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT c.name, (SELECT json_group_array(label ORDER BY name_key)
                FROM correspondent_names WHERE correspondent_id = c.id AND is_alias = 1)
            FROM correspondents c WHERE c.id = $id;
            """;
        command.Parameters.AddWithValue("$id", id);
        using var row = command.ExecuteReader();
        return row.Read() ? new(id, row.GetString(0), ReadMetadataJson<string[]>(row.GetString(1)))
            : throw new ArgumentException(CatalogRelationshipMissing);
    }

    private static void WriteMigratedBaseFields(SqliteConnection connection, SqliteTransaction transaction, long documentId,
        MigrationWrite write, bool isNew)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.Parameters.AddWithValue("$id", documentId);
        command.CommandText = "SELECT relative_path, display_title, document_day, reviewed FROM documents WHERE id = $id AND is_present = 1;";
        string filename;
        string? title;
        int? day;
        bool reviewed;
        using (var row = command.ExecuteReader())
        {
            if (!row.Read()) throw new InvalidOperationException("Restore the previously imported document before reapplying migration metadata.");
            filename = Path.GetFileNameWithoutExtension(row.GetString(0));
            title = row.IsDBNull(1) ? null : row.GetString(1);
            day = row.IsDBNull(2) ? null : row.GetInt32(2);
            reviewed = row.GetBoolean(3);
        }
        if (write.Metadata.Title is { } sourceTitle)
        {
            TryMigratedValue(transaction, write, "Title", () =>
            {
                string label = InboxLibrary.NormalizeTitle(sourceTitle);
                if (title is not null && title != label) throw new ArgumentException("The local title was kept.");
                title = label;
            });
        }
        if (write.Metadata.DocumentDate is { } date)
        {
            if (day is int prior && prior != date.DayNumber) write.Conflicts.Add(new("Document date", "The local date was kept."));
            else day = date.DayNumber;
        }
        if (write.Metadata.Reviewed is bool sourceReviewed)
        {
            if (isNew) reviewed = sourceReviewed;
            else if (reviewed != sourceReviewed) write.Conflicts.Add(new("Review state", "The local review state was kept."));
        }
        command.CommandText = """
            UPDATE documents SET display_title = $label,
                title = CASE WHEN $label IS NULL THEN title ELSE $label || char(10) || $filename END,
                document_day = $day, reviewed = $reviewed WHERE id = $id;
            """;
        command.Parameters.AddWithValue("$label", DbValue(title));
        command.Parameters.AddWithValue("$filename", filename);
        command.Parameters.AddWithValue("$day", DbValue(day));
        command.Parameters.AddWithValue("$reviewed", reviewed);
        command.ExecuteNonQuery();
    }

    private static void WriteMigratedTag(SqliteConnection connection, SqliteTransaction transaction, long documentId, ImportedTag tag)
    {
        if (tag.Path.Count == 0) throw new ArgumentException("The source tag has no name.");
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        var keyParameter = command.Parameters.Add("$key", SqliteType.Text);
        string? parent = null;
        foreach (string label in tag.Path)
        {
            string key = EnsureMetadataTag(connection, transaction, label);
            keyParameter.Value = key;
            command.CommandText = """
                SELECT p.label, a.color_hex FROM tag_appearance a LEFT JOIN tags p ON p.tag_key = a.parent_key WHERE a.tag_key = $key;
                """;
            string? existingParent = null;
            string? color = null;
            using (var row = command.ExecuteReader())
            {
                if (row.Read())
                {
                    existingParent = row.IsDBNull(0) ? null : row.GetString(0);
                    color = row.IsDBNull(1) ? null : row.GetString(1);
                }
            }
            if (existingParent is not null && parent is not null && SearchTerms.TagKey(existingParent) != SearchTerms.TagKey(parent))
                throw new ArgumentException("The tag already belongs to a different branch.");
            bool leaf = label == tag.Path[^1];
            string? desiredColor = leaf ? NormalizeTagColor(tag.ColorHex) : null;
            if (color is not null && desiredColor is not null && color != desiredColor)
                throw new ArgumentException("The local tag colour was kept.");
            if (parent is not null || desiredColor is not null)
                WriteTagAppearance(connection, transaction, label, existingParent ?? parent, color ?? desiredColor);
            parent = label;
        }
        command.Parameters.AddWithValue("$id", documentId);
        command.CommandText = "INSERT OR IGNORE INTO document_tags (document_id, tag_key) VALUES ($id, $key);";
        command.ExecuteNonQuery();
    }

    private static DocumentField MapMigratedField(SqliteConnection connection, SqliteTransaction transaction, long documentId,
        ImportedCustomField field)
    {
        string label = CleanMetadataName(field.Name);
        CustomFieldValue value = field.Value switch
        {
            ImportedFieldValue.SourceLink source => new CustomFieldValue.DocumentLink(
                FindMigratedDocument(connection, transaction, source.Target) ??
                throw new ArgumentException("The linked source document is pending. Run link resolution after migration.")),
            ImportedFieldValue.Value data => data.Data,
            _ => throw new ArgumentException("This field type has no structured mapping.")
        };
        if (value is CustomFieldValue.DocumentLink link && link.DocumentId == documentId)
            throw new ArgumentException("A document cannot link to itself.");
        long? id = FindMetadataDefinitionId(connection, transaction, "custom_fields", "id", MetadataNameKey(label));
        var definition = id is { } existing ? ReadCustomField(connection, transaction, existing)
            : WriteCustomField(connection, transaction, null, label, GetCustomFieldKind(value),
                field.Value is ImportedFieldValue.Value { Choices: { Count: > 0 } choices } ? choices :
                    value is CustomFieldValue.Choice choice ? [choice.Label] : []);
        return new(definition.Id, NormalizeCustomField(definition, value));
    }

    private static CustomFieldKind GetCustomFieldKind(CustomFieldValue value) => value switch
    {
        CustomFieldValue.Text => CustomFieldKind.Text,
        CustomFieldValue.Boolean => CustomFieldKind.Boolean,
        CustomFieldValue.Date => CustomFieldKind.Date,
        CustomFieldValue.Url => CustomFieldKind.Url,
        CustomFieldValue.Integer => CustomFieldKind.Integer,
        CustomFieldValue.Decimal => CustomFieldKind.Decimal,
        CustomFieldValue.Money => CustomFieldKind.Money,
        CustomFieldValue.DocumentLink => CustomFieldKind.DocumentLink,
        CustomFieldValue.Choice => CustomFieldKind.Choice,
        _ => throw new ArgumentException("This field type has no structured mapping.")
    };

    private static long? FindMetadataDefinitionId(SqliteConnection connection, SqliteTransaction transaction,
        string table, string idColumn, string key)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"SELECT {idColumn} FROM {table} WHERE name_key = $key;";
        command.Parameters.AddWithValue("$key", key);
        return command.ExecuteScalar() as long?;
    }

    private static void TryMigratedValue(SqliteTransaction transaction, MigrationWrite write, string field, Action apply)
    {
        transaction.Save(MigrationValueSavepoint);
        try { apply(); }
        catch (ArgumentException error)
        {
            transaction.Rollback(MigrationValueSavepoint);
            write.Conflicts.Add(new(field, error.Message));
        }
        transaction.Release(MigrationValueSavepoint);
    }

    private static void BindMigrationKey(SqliteCommand command, MigrationSourceKey key)
    {
        command.Parameters.AddWithValue("$format", key.Format);
        command.Parameters.AddWithValue("$library", key.LibraryKey);
        command.Parameters.AddWithValue("$record", key.RecordKey);
    }
}
