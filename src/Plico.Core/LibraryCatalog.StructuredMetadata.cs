using Microsoft.Data.Sqlite;
using System.Globalization;
using System.Text;
using System.Text.Json;

namespace Plico.Core;

internal sealed partial class LibraryCatalog
{
    internal const int MaximumMetadataTextLength = 32_768;
    private const int MaximumMetadataNameLength = 96;
    private const int MaximumChoiceCount = 128;
    private const int MaximumAliasCount = 64;
    private const string CatalogRelationshipMissing = "A metadata definition is unavailable. Reopen the document and try again.";
    private static readonly JsonSerializerOptions MetadataJson = new(JsonSerializerDefaults.Web);
    private const string StructuredMetadataProjection = """
        m.type_id, COALESCE(m.notes, ''),
        (SELECT json_group_array(correspondent_id ORDER BY correspondent_id)
            FROM document_correspondents WHERE document_id = d.id),
        (SELECT json_group_array(json_object('fieldId', field_id, 'value', json(payload)) ORDER BY field_id)
            FROM document_custom_fields WHERE document_id = d.id)
        """;
    private const string StructuredMetadataMigration = """
        CREATE TABLE IF NOT EXISTS correspondents (
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            name TEXT NOT NULL
        );
        CREATE TABLE IF NOT EXISTS correspondent_names (
            name_key TEXT PRIMARY KEY,
            correspondent_id INTEGER NOT NULL REFERENCES correspondents(id) ON DELETE CASCADE,
            label TEXT NOT NULL,
            is_alias INTEGER NOT NULL CHECK (is_alias IN (0, 1))
        ) WITHOUT ROWID;
        CREATE INDEX IF NOT EXISTS correspondent_names_owner ON correspondent_names(correspondent_id, is_alias);
        CREATE TABLE IF NOT EXISTS document_types (
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            label TEXT NOT NULL,
            name_key TEXT NOT NULL UNIQUE
        );
        CREATE TABLE IF NOT EXISTS custom_fields (
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            label TEXT NOT NULL,
            name_key TEXT NOT NULL UNIQUE,
            kind TEXT NOT NULL,
            choices TEXT NOT NULL
        );
        CREATE TABLE IF NOT EXISTS document_metadata (
            document_id INTEGER PRIMARY KEY REFERENCES documents(id) ON DELETE CASCADE,
            type_id INTEGER REFERENCES document_types(id) ON DELETE SET NULL,
            notes TEXT NOT NULL
        );
        CREATE INDEX IF NOT EXISTS document_metadata_type ON document_metadata(type_id);
        CREATE TABLE IF NOT EXISTS document_correspondents (
            document_id INTEGER NOT NULL REFERENCES documents(id) ON DELETE CASCADE,
            correspondent_id INTEGER NOT NULL REFERENCES correspondents(id) ON DELETE CASCADE,
            PRIMARY KEY(document_id, correspondent_id)
        ) WITHOUT ROWID;
        CREATE INDEX IF NOT EXISTS document_correspondents_sender ON document_correspondents(correspondent_id, document_id);
        CREATE TABLE IF NOT EXISTS document_custom_fields (
            document_id INTEGER NOT NULL REFERENCES documents(id) ON DELETE CASCADE,
            field_id INTEGER NOT NULL REFERENCES custom_fields(id) ON DELETE CASCADE,
            payload TEXT NOT NULL,
            link_id INTEGER REFERENCES documents(id) ON DELETE CASCADE,
            PRIMARY KEY(document_id, field_id)
        ) WITHOUT ROWID;
        CREATE INDEX IF NOT EXISTS document_custom_fields_definition ON document_custom_fields(field_id, document_id);
        CREATE INDEX IF NOT EXISTS document_custom_fields_link ON document_custom_fields(link_id) WHERE link_id IS NOT NULL;
        CREATE TABLE IF NOT EXISTS tag_appearance (
            tag_key TEXT PRIMARY KEY REFERENCES tags(tag_key) ON DELETE CASCADE,
            parent_key TEXT REFERENCES tags(tag_key) ON DELETE SET NULL,
            color_hex TEXT,
            CHECK (tag_key <> parent_key)
        ) WITHOUT ROWID;
        CREATE INDEX IF NOT EXISTS tag_appearance_parent ON tag_appearance(parent_key);
        CREATE TABLE IF NOT EXISTS migrated_documents (
            source_format TEXT NOT NULL,
            library_key TEXT NOT NULL,
            record_key TEXT NOT NULL,
            document_id INTEGER NOT NULL REFERENCES documents(id) ON DELETE CASCADE,
            PRIMARY KEY(source_format, library_key, record_key)
        ) WITHOUT ROWID;
        CREATE INDEX IF NOT EXISTS migrated_documents_id ON migrated_documents(document_id);
        CREATE TABLE IF NOT EXISTS pending_migrated_documents (
            source_format TEXT NOT NULL,
            library_key TEXT NOT NULL,
            record_key TEXT NOT NULL,
            relative_path TEXT NOT NULL,
            pdf_sha256 BLOB NOT NULL,
            source_sha256 BLOB,
            PRIMARY KEY(source_format, library_key, record_key)
        ) WITHOUT ROWID;
        """;

    internal DocumentMetadata ReadDocumentMetadata(long documentId)
    {
        using var connection = Connect();
        using var transaction = connection.BeginTransaction(deferred: true);
        return ReadDocumentMetadata(connection, transaction, documentId);
    }

    private static DocumentMetadata ReadDocumentMetadata(SqliteConnection connection, SqliteTransaction transaction, long documentId)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"""
            SELECT {StructuredMetadataProjection}
            FROM documents d LEFT JOIN document_metadata m ON m.document_id = d.id WHERE d.id = $document;
            """;
        command.Parameters.AddWithValue("$document", documentId);
        using var row = command.ExecuteReader();
        if (!row.Read()) throw new InvalidOperationException(DocumentUnavailableMessage);
        return ReadStructuredMetadata(row, 0);
    }

    private static DocumentMetadata ReadStructuredMetadata(SqliteDataReader row, int firstColumn) =>
        new(row.IsDBNull(firstColumn) ? null : row.GetInt64(firstColumn), ReadMetadataJson<long[]>(row.GetString(firstColumn + 2)),
            row.GetString(firstColumn + 1), ReadMetadataJson<DocumentField[]>(row.GetString(firstColumn + 3)));

    internal void SaveDocumentMetadata(long documentId, DocumentMetadata metadata, CancellationToken token)
    {
        using var connection = Connect();
        using var transaction = connection.BeginTransaction();
        WriteDocumentMetadata(connection, transaction, documentId, metadata, token);
        transaction.Commit();
    }

    // O(c log C + f log F) for c selected contacts and f supplied fields, with O(c + f) working space.
    private static void WriteDocumentMetadata(SqliteConnection connection, SqliteTransaction transaction, long documentId,
        DocumentMetadata metadata, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        token.ThrowIfCancellationRequested();
        string notes = ValidateMetadataText(metadata.Notes);
        var contacts = metadata.CorrespondentIds.Distinct().ToArray();
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.Parameters.AddWithValue("$document", documentId);
        command.CommandText = "SELECT 1 FROM documents WHERE id = $document AND is_present = 1;";
        if (command.ExecuteScalar() is null) throw new InvalidOperationException(DocumentUnavailableMessage);

        if (metadata.DocumentTypeId is { } typeId)
        {
            command.CommandText = "SELECT 1 FROM document_types WHERE id = $reference;";
            command.Parameters.AddWithValue("$reference", typeId);
            if (command.ExecuteScalar() is null) throw new ArgumentException(CatalogRelationshipMissing);
            command.Parameters.RemoveAt("$reference");
        }

        var fields = new List<DocumentField>(metadata.Fields.Count);
        var seen = new HashSet<long>();
        foreach (var field in metadata.Fields)
        {
            token.ThrowIfCancellationRequested();
            if (!seen.Add(field.FieldId)) throw new ArgumentException("Each field can be set once.");
            var definition = ReadCustomField(connection, transaction, field.FieldId);
            var normalized = NormalizeCustomField(definition, field.Value);
            if (normalized is CustomFieldValue.DocumentLink link)
            {
                command.CommandText = "SELECT 1 FROM documents WHERE id = $reference AND id <> $document;";
                command.Parameters.AddWithValue("$reference", link.DocumentId);
                if (command.ExecuteScalar() is null) throw new ArgumentException("Choose another document in this library for the link.");
                command.Parameters.RemoveAt("$reference");
            }
            fields.Add(new(field.FieldId, normalized));
        }

        command.CommandText = """
            INSERT INTO document_metadata (document_id, type_id, notes) VALUES ($document, $type, $notes)
            ON CONFLICT(document_id) DO UPDATE SET type_id = excluded.type_id, notes = excluded.notes;
            DELETE FROM document_correspondents WHERE document_id = $document;
            DELETE FROM document_custom_fields WHERE document_id = $document;
            INSERT INTO document_correspondents (document_id, correspondent_id)
                SELECT $document, c.id FROM json_each($contacts) wanted JOIN correspondents c ON c.id = wanted.value;
            SELECT changes();
            """;
        command.Parameters.AddWithValue("$type", DbValue(metadata.DocumentTypeId));
        command.Parameters.AddWithValue("$notes", notes);
        command.Parameters.AddWithValue("$contacts", JsonSerializer.Serialize(contacts, MetadataJson));
        if ((long)command.ExecuteScalar()! != contacts.Length) throw new ArgumentException(CatalogRelationshipMissing);

        command.Parameters.Clear();
        command.Parameters.AddWithValue("$document", documentId);
        var id = command.Parameters.Add("$field", SqliteType.Integer);
        var value = command.Parameters.Add("$payload", SqliteType.Text);
        var linkId = command.Parameters.Add("$link", SqliteType.Integer);
        command.CommandText = """
            INSERT INTO document_custom_fields (document_id, field_id, payload, link_id)
            VALUES ($document, $field, $payload, $link);
            """;
        command.Prepare();
        foreach (var field in fields)
        {
            token.ThrowIfCancellationRequested();
            id.Value = field.FieldId;
            value.Value = JsonSerializer.Serialize(field.Value, MetadataJson);
            linkId.Value = field.Value is CustomFieldValue.DocumentLink link ? link.DocumentId : DBNull.Value;
            command.ExecuteNonQuery();
        }
        token.ThrowIfCancellationRequested();
    }

    internal MetadataDefinitions ReadMetadataDefinitions()
    {
        using var connection = Connect();
        using var transaction = connection.BeginTransaction(deferred: true);
        return ReadMetadataDefinitions(connection, transaction);
    }

    private static MetadataDefinitions ReadMetadataDefinitions(SqliteConnection connection, SqliteTransaction transaction)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT c.id, c.name, (SELECT json_group_array(label ORDER BY name_key)
                FROM correspondent_names WHERE correspondent_id = c.id AND is_alias = 1)
            FROM correspondents c ORDER BY name COLLATE NOCASE, id;
            SELECT id, label FROM document_types ORDER BY name_key;
            SELECT id, label, kind, choices FROM custom_fields ORDER BY name_key;
            """;
        using var rows = command.ExecuteReader();
        var contacts = new List<Correspondent>();
        while (rows.Read()) contacts.Add(new(rows.GetInt64(0), rows.GetString(1), ReadMetadataJson<string[]>(rows.GetString(2))));
        rows.NextResult();
        var types = new List<CatalogDocumentType>();
        while (rows.Read()) types.Add(new(rows.GetInt64(0), rows.GetString(1)));
        rows.NextResult();
        var fields = new List<CustomFieldDefinition>();
        while (rows.Read()) fields.Add(ReadCustomField(rows));
        return new(contacts, types, fields);
    }

    internal Correspondent SaveCorrespondent(long? id, string name, IReadOnlyList<string> aliases)
    {
        using var connection = Connect();
        using var transaction = connection.BeginTransaction();
        var contact = WriteCorrespondent(connection, transaction, id, name, aliases);
        transaction.Commit();
        return contact;
    }

    private static Correspondent WriteCorrespondent(SqliteConnection connection, SqliteTransaction transaction,
        long? id, string name, IReadOnlyList<string> aliases)
    {
        ArgumentNullException.ThrowIfNull(aliases);
        string label = CleanMetadataName(name);
        var alternateNames = NormalizeMetadataLabels(aliases, MaximumAliasCount, "A correspondent has too many aliases.")
            .Where(alias => MetadataNameKey(alias) != MetadataNameKey(label)).ToArray();
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.Parameters.AddWithValue("$label", label);
        var contact = SaveDefinitionRow(command, id, "INSERT INTO correspondents (name) VALUES ($label) RETURNING id;",
            "UPDATE correspondents SET name = $label WHERE id = $id RETURNING id;");
        command.Parameters.Clear();
        command.Parameters.AddWithValue("$id", contact);
        command.CommandText = "DELETE FROM correspondent_names WHERE correspondent_id = $id;";
        command.ExecuteNonQuery();

        command.CommandText = """
            INSERT INTO correspondent_names (name_key, correspondent_id, label, is_alias)
            VALUES ($key, $id, $name, $alias) ON CONFLICT(name_key) DO NOTHING;
            """;
        var key = command.Parameters.Add("$key", SqliteType.Text);
        var entry = command.Parameters.Add("$name", SqliteType.Text);
        var isAlias = command.Parameters.Add("$alias", SqliteType.Integer);
        foreach (var accepted in alternateNames.Prepend(label))
        {
            key.Value = MetadataNameKey(accepted);
            entry.Value = accepted;
            isAlias.Value = accepted != label;
            if (command.ExecuteNonQuery() != 1)
                throw new ArgumentException($"“{accepted}” is already the name or alias of a correspondent.");
        }
        return new(contact, label, alternateNames);
    }

    internal CatalogDocumentType SaveDocumentType(long? id, string name)
    {
        using var connection = Connect();
        using var transaction = connection.BeginTransaction();
        var item = WriteDocumentType(connection, transaction, id, name);
        transaction.Commit();
        return item;
    }

    private static CatalogDocumentType WriteDocumentType(SqliteConnection connection, SqliteTransaction transaction, long? id, string name)
    {
        string label = CleanMetadataName(name);
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.Parameters.AddWithValue("$label", label);
        command.Parameters.AddWithValue("$key", MetadataNameKey(label));
        RequireUniqueName(command, "document_types", id, "A document type with this name already exists.");
        long saved = SaveDefinitionRow(command, id,
            "INSERT INTO document_types (label, name_key) VALUES ($label, $key) RETURNING id;",
            "UPDATE document_types SET label = $label, name_key = $key WHERE id = $id RETURNING id;");
        return new(saved, label);
    }

    internal CustomFieldDefinition SaveCustomField(long? id, string name, CustomFieldKind kind, IReadOnlyList<string> choices)
    {
        using var connection = Connect();
        using var transaction = connection.BeginTransaction();
        var field = WriteCustomField(connection, transaction, id, name, kind, choices);
        transaction.Commit();
        return field;
    }

    private static CustomFieldDefinition WriteCustomField(SqliteConnection connection, SqliteTransaction transaction, long? id,
        string name, CustomFieldKind kind, IReadOnlyList<string> choices)
    {
        ArgumentNullException.ThrowIfNull(choices);
        if (!Enum.IsDefined(kind)) throw new ArgumentOutOfRangeException(nameof(kind));
        string label = CleanMetadataName(name);
        var options = NormalizeMetadataLabels(choices, MaximumChoiceCount, "A field has too many choices.");
        if ((kind == CustomFieldKind.Choice) != (options.Length > 0))
            throw new ArgumentException("A choice field needs at least one option. Other fields use their declared type.");
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.Parameters.AddWithValue("$label", label);
        command.Parameters.AddWithValue("$key", MetadataNameKey(label));
        command.Parameters.AddWithValue("$kind", kind.ToString());
        command.Parameters.AddWithValue("$choices", JsonSerializer.Serialize(options, MetadataJson));
        RequireUniqueName(command, "custom_fields", id, "A custom field with this name already exists.");
        if (id is { } existing) ValidateDefinitionChange(command, existing, kind, options);
        long saved = SaveDefinitionRow(command, id,
            "INSERT INTO custom_fields (label, name_key, kind, choices) VALUES ($label, $key, $kind, $choices) RETURNING id;",
            "UPDATE custom_fields SET label = $label, name_key = $key, kind = $kind, choices = $choices WHERE id = $id RETURNING id;");
        return new(saved, label, kind, options);
    }

    private static void ValidateDefinitionChange(SqliteCommand command, long id, CustomFieldKind kind, string[] choices)
    {
        command.Parameters.AddWithValue("$existing", id);
        command.CommandText = "SELECT kind FROM custom_fields WHERE id = $existing;";
        if (command.ExecuteScalar() is not string existingKind) throw new ArgumentException(CatalogRelationshipMissing);

        command.CommandText = """
            SELECT EXISTS (
                SELECT 1 FROM document_custom_fields WHERE field_id = $existing AND
                    ($kind <> $previous OR ($kind = 'Choice' AND json_extract(payload, '$.label') NOT IN (SELECT value FROM json_each($choices)))));
            """;
        command.Parameters.AddWithValue("$previous", existingKind);
        if ((long)command.ExecuteScalar()! != 0)
            throw new ArgumentException("This change would invalidate a field value already used in the library.");
        command.Parameters.RemoveAt("$previous");
        command.Parameters.RemoveAt("$existing");
    }

    private static void RequireUniqueName(SqliteCommand command, string table, long? except, string message)
    {
        command.Parameters.AddWithValue("$except", DbValue(except));
        command.CommandText = $"SELECT 1 FROM {table} WHERE name_key = $key AND ($except IS NULL OR id <> $except);";
        if (command.ExecuteScalar() is not null) throw new ArgumentException(message);
        command.Parameters.RemoveAt("$except");
    }

    private static long SaveDefinitionRow(SqliteCommand command, long? id, string insert, string update)
    {
        command.CommandText = id is null ? insert : update;
        if (id is { } identity) command.Parameters.AddWithValue("$id", identity);
        return command.ExecuteScalar() is long stored ? stored : throw new ArgumentException(CatalogRelationshipMissing);
    }

    private static CustomFieldDefinition ReadCustomField(SqliteConnection connection, SqliteTransaction transaction, long id)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT id, label, kind, choices FROM custom_fields WHERE id = $id;";
        command.Parameters.AddWithValue("$id", id);
        using var rows = command.ExecuteReader();
        if (!rows.Read()) throw new ArgumentException(CatalogRelationshipMissing);
        return ReadCustomField(rows);
    }

    private static CustomFieldDefinition ReadCustomField(SqliteDataReader rows) =>
        new(rows.GetInt64(0), rows.GetString(1), Enum.Parse<CustomFieldKind>(rows.GetString(2)), ReadMetadataJson<string[]>(rows.GetString(3)));

    private static CustomFieldValue NormalizeCustomField(CustomFieldDefinition field, CustomFieldValue value) =>
        (field.Kind, value) switch
        {
            (CustomFieldKind.Text, CustomFieldValue.Text text) => new CustomFieldValue.Text(ValidateMetadataText(text.Value)),
            (CustomFieldKind.Boolean, CustomFieldValue.Boolean boolean) => boolean,
            (CustomFieldKind.Date, CustomFieldValue.Date date) => date,
            (CustomFieldKind.Url, CustomFieldValue.Url url) when Uri.TryCreate(url.Value, UriKind.Absolute, out var address) &&
                address.Scheme is "http" or "https" && url.Value.Length <= MaximumMetadataTextLength => url,
            (CustomFieldKind.Integer, CustomFieldValue.Integer integer) => integer,
            (CustomFieldKind.Decimal, CustomFieldValue.Decimal number) => number,
            (CustomFieldKind.Money, CustomFieldValue.Money money) when money.Currency is null ||
                (money.Currency.Length == 3 && money.Currency.All(char.IsAsciiLetter)) =>
                    money with { Currency = money.Currency?.ToUpperInvariant() },
            (CustomFieldKind.DocumentLink, CustomFieldValue.DocumentLink link) when link.DocumentId > 0 => link,
            (CustomFieldKind.Choice, CustomFieldValue.Choice choice) when field.Choices.Contains(choice.Label, StringComparer.Ordinal) => choice,
            _ => throw new ArgumentException($"Enter a valid value for “{field.Name}”.")
        };

    private static string ValidateMetadataText(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (text.Length > MaximumMetadataTextLength || text.Contains('\0'))
            throw new ArgumentException($"Enter up to {MaximumMetadataTextLength.ToString(CultureInfo.CurrentCulture)} characters.");
        return text;
    }

    private static string CleanMetadataName(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        string label = value.Trim().Normalize(NormalizationForm.FormC);
        if (label.Length is 0 or > MaximumMetadataNameLength || label.Any(char.IsControl))
            throw new ArgumentException($"Enter a name between 1 and {MaximumMetadataNameLength} characters.");
        return label;
    }

    private static string[] NormalizeMetadataLabels(IReadOnlyList<string> source, int maximum, string message)
    {
        if (source.Count > maximum) throw new ArgumentException(message);
        return source.Select(CleanMetadataName).DistinctBy(MetadataNameKey).ToArray();
    }

    private static string MetadataNameKey(string label) => label.ToUpperInvariant();
    private static object DbValue<T>(T? value) => (object?)value ?? DBNull.Value;
    private static T ReadMetadataJson<T>(string json) => JsonSerializer.Deserialize<T>(json, MetadataJson) ??
        throw new InvalidDataException("The library contains an invalid metadata value.");

    internal IReadOnlyList<MetadataLinkTarget> ReadMetadataLinkTargets(long exceptDocumentId, CancellationToken token)
    {
        using var connection = Connect();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT id, COALESCE(display_title, title) FROM documents WHERE id <> $document
            ORDER BY COALESCE(display_title, title) COLLATE NOCASE, id;
            """;
        command.Parameters.AddWithValue("$document", exceptDocumentId);
        using var rows = command.ExecuteReader();
        var targets = new List<MetadataLinkTarget>();
        while (rows.Read())
        {
            token.ThrowIfCancellationRequested();
            targets.Add(new(rows.GetInt64(0), rows.GetString(1)));
        }
        return targets;
    }
}
