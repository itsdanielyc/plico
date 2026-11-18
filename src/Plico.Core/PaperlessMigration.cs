using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Plico.Core;

/// <summary>Maps original and split Paperless exports to portable PDFs and structured catalog metadata.</summary>
internal static class PaperlessMigration
{
    private const string FormatName = "paperless-ngx";
    private sealed record ExportRecord(string Model, long Id, JsonElement Fields, JsonElement Data);

    internal static IEnumerable<MigrationEntry> Read(string source, CancellationToken cancellationToken)
    {
        using var files = new PaperlessExportFiles(source);
        using var manifest = files.Open(PaperlessExportFiles.ManifestName)
            ?? throw new InvalidDataException("Choose a Paperless-ngx export containing manifest.json.");
        var records = ReadManifest(manifest);
        var splitNames = files.SplitManifests().Order(StringComparer.Ordinal).ToArray();
        foreach (string name in splitNames)
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var split = files.Open(name)!;
            records.AddRange(ReadManifest(split));
        }
        string libraryKey = GetLibraryKey(files, splitNames);
        var tags = OfModel(records, "tag").ToDictionary(record => record.Id);
        var correspondents = OfModel(records, "correspondent").ToDictionary(record => record.Id);
        var types = OfModel(records, "documenttype").ToDictionary(record => record.Id);
        var definitions = OfModel(records, "customfield").ToDictionary(record => record.Id);
        var notes = OfModel(records, "note").ToLookup(record => Number(Property(record.Fields, "document")));
        var customFields = OfModel(records, "customfieldinstance").ToLookup(record => Number(Property(record.Fields, "document")));
        bool hasInboxTags = tags.Values.Any(tag => Boolean(Property(tag.Fields, "is_inbox_tag")) == true);

        foreach (var record in OfModel(records, "document"))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var key = new MigrationSourceKey(FormatName, libraryKey, record.Id.ToString(CultureInfo.InvariantCulture));
            string title = Text(Property(record.Fields, "title")) ?? $"Paperless record {record.Id}";
            var notices = new List<LibraryMigrationNotice>();
            string? added = Text(Property(record.Fields, "added"));
            if (added is not null) notices.Add(new(title, "Source added", "Original Paperless intake timestamp", added));
            var documentTagIds = Array(Property(record.Fields, "tags")).Select(Number).OfType<long>().ToArray();
            var documentTags = documentTagIds.Select(id => Tag(id, tags)).OfType<ImportedTag>().ToArray();
            bool? reviewed = hasInboxTags
                ? !documentTagIds.Any(id => tags.TryGetValue(id, out var tag) && Boolean(Property(tag.Fields, "is_inbox_tag")) == true)
                : null;
            var correspondent = Definition(record.Fields, "correspondent", correspondents);
            var documentType = Definition(record.Fields, "document_type", types);
            string? noteText = string.Join(Environment.NewLine + Environment.NewLine, notes[record.Id]
                .Select(note => Text(Property(note.Fields, "note")) ?? Text(Property(note.Fields, "content")))
                .OfType<string>());
            var fields = new List<ImportedCustomField>();
            var instances = customFields[record.Id].Select(instance => instance.Fields)
                .Concat(Array(Property(record.Fields, "custom_fields")));
            foreach (var instance in instances)
            {
                MapCustomField(instance, key, definitions, fields, notices, title);
            }
            var metadata = new ImportedDocumentMetadata(title, Date(Property(record.Fields, "created")), reviewed, documentTags,
                correspondent is null ? [] : [new ImportedCorrespondent(Name(correspondent) ?? "Unknown correspondent",
                    Array(Property(correspondent.Fields, "aliases")).Select(Text).OfType<string>().ToArray())],
                documentType is null ? null : Name(documentType), noteText, fields,
                Number(Property(record.Fields, "archive_serial_number")) is > 0 and var serial ? serial : null);
            yield return new MigrationEntry(key, metadata,
                (stage, token) => StagePdfAsync(files, record, stage, token), notices);
        }
    }

    private static List<ExportRecord> ReadManifest(Stream stream)
    {
        using var json = JsonDocument.Parse(stream);
        if (json.RootElement.ValueKind != JsonValueKind.Array)
            throw new InvalidDataException("The Paperless manifest must contain serialized records.");
        var records = new List<ExportRecord>();
        foreach (var item in json.RootElement.EnumerateArray())
        {
            if (Text(Property(item, "model")) is not { } model || Number(Property(item, "pk")) is not long id) continue;
            var data = item.Clone();
            records.Add(new ExportRecord(model, id, Property(data, "fields"), data));
        }
        return records;
    }

    private static string GetLibraryKey(PaperlessExportFiles files, string[] splitNames)
    {
        using var manifest = files.Open(PaperlessExportFiles.ManifestName)!;
        using var identity = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        identity.AppendData(SHA256.HashData(manifest));
        foreach (string name in splitNames)
        {
            using var split = files.Open(name)!;
            identity.AppendData(SHA256.HashData(split));
        }
        return Convert.ToHexString(identity.GetHashAndReset());
    }

    private static async Task<string> StagePdfAsync(PaperlessExportFiles files, ExportRecord record, string stage, CancellationToken token)
    {
        string[] candidates = new[]
        {
            Text(Property(record.Data, "__exported_archive_name")),
            Text(Property(record.Data, "__exported_file_name")),
            Text(Property(record.Fields, "filename"))
        }.OfType<string>().Distinct(StringComparer.Ordinal).ToArray();
        foreach (string name in candidates)
        {
            using var source = files.Open(name);
            if (source is null) continue;
            return await MigrationFiles.StageAsync(source, stage, name, token).ConfigureAwait(false);
        }
        throw new FileNotFoundException("The export does not contain the document's archived PDF or original file.");
    }

    private static ExportRecord? Definition(JsonElement from, string field, Dictionary<long, ExportRecord> records) =>
        Number(Property(from, field)) is long id && records.TryGetValue(id, out var result) ? result : null;

    private static ImportedTag? Tag(long id, Dictionary<long, ExportRecord> definitions)
    {
        var visited = new HashSet<long>();
        var path = new List<string>();
        string? colour = null;
        while (visited.Add(id) && definitions.TryGetValue(id, out var record) && Name(record) is { } name)
        {
            if (path.Count == 0) colour = Text(Property(record.Fields, "color"));
            path.Add(name);
            if (Number(Property(record.Fields, "parent")) is not long parent) break;
            id = parent;
        }
        path.Reverse();
        return path.Count == 0 ? null : new ImportedTag(path, colour);
    }

    private static void MapCustomField(JsonElement field, MigrationSourceKey key, Dictionary<long, ExportRecord> definitions,
        List<ImportedCustomField> mapped, List<LibraryMigrationNotice> notices, string title)
    {
        if (Number(Property(field, "field")) is not long id)
            id = Number(Property(field, "field_id")) ?? -1;
        string name = definitions.TryGetValue(id, out var definition) ? Name(definition) ?? $"Field {id}" : $"Field {id}";
        JsonElement value = Property(field, "value");
        if (value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            var typed = new[] { "value_text", "value_bool", "value_date", "value_url", "value_int", "value_float",
                "value_monetary", "value_document_ids", "value_select" };
            value = typed.Select(property => Property(field, property))
                .FirstOrDefault(item => item.ValueKind is not (JsonValueKind.Null or JsonValueKind.Undefined));
        }
        if (value.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null) return;
        string? type = definition is null ? null : Text(Property(definition.Fields, "data_type"));
        var converted = ConvertField(type, value, definition, key);
        if (converted is not null) mapped.Add(new ImportedCustomField(name, converted));
        else notices.Add(new(title, name, $"Unmapped Paperless field ({type ?? "unknown type"})", value.GetRawText()));
    }

    private static ImportedFieldValue? ConvertField(string? type, JsonElement value, ExportRecord? definition, MigrationSourceKey key)
    {
        string? text = Text(value);
        CustomFieldValue? converted = type switch
        {
            "string" or "text" when text is not null => new CustomFieldValue.Text(text),
            "boolean" when Boolean(value) is bool flag => new CustomFieldValue.Boolean(flag),
            "date" when Date(value) is DateOnly date => new CustomFieldValue.Date(date),
            "url" when text is not null => new CustomFieldValue.Url(text),
            "integer" when Number(value) is long integer => new CustomFieldValue.Integer(integer),
            "float" or "decimal" when Decimal(value) is decimal number => new CustomFieldValue.Decimal(number),
            "monetary" => Money(text),
            _ => null
        };
        if (converted is not null) return new ImportedFieldValue.Value(converted);
        if (type == "documentlink")
        {
            var linked = value.ValueKind == JsonValueKind.Array ? value.EnumerateArray().ToArray() : [value];
            return linked.Length == 1 && Number(linked[0]) is long id
                ? new ImportedFieldValue.SourceLink(key with { RecordKey = id.ToString(CultureInfo.InvariantCulture) })
                : null;
        }
        if (type != "select" || definition is null || text is null) return null;
        var choices = Array(Property(Property(definition.Fields, "extra_data"), "select_options")).ToArray();
        string? selected = choices.Where(choice => Text(Property(choice, "id")) == text || Text(Property(choice, "label")) == text)
            .Select(choice => Text(Property(choice, "label"))).FirstOrDefault();
        return selected is null ? null : new ImportedFieldValue.Value(new CustomFieldValue.Choice(selected),
            choices.Select(choice => Text(Property(choice, "label"))).OfType<string>().ToArray());
    }

    private static CustomFieldValue.Money? Money(string? text)
    {
        if (text is null) return null;
        string? currency = text.Length > 3 && text.AsSpan(0, 3).IndexOfAnyExceptInRange('A', 'Z') == -1 ? text[..3] : null;
        var amount = currency is null ? text.AsSpan() : text.AsSpan(3);
        return decimal.TryParse(amount, NumberStyles.Number, CultureInfo.InvariantCulture, out decimal parsed)
            ? new CustomFieldValue.Money(parsed, currency) : null;
    }

    private static IEnumerable<ExportRecord> OfModel(IEnumerable<ExportRecord> records, string name) =>
        records.Where(record => record.Model == "documents." + name);
    private static string? Name(ExportRecord record) => Text(Property(record.Fields, "name"));
    private static JsonElement Property(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) ? value : default;
    private static IEnumerable<JsonElement> Array(JsonElement value) =>
        value.ValueKind == JsonValueKind.Array ? value.EnumerateArray() : [];
    private static string? Text(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.String => value.GetString(),
        JsonValueKind.Number => value.GetRawText(),
        _ => null
    };
    private static long? Number(JsonElement value) =>
        long.TryParse(Text(value), NumberStyles.Integer, CultureInfo.InvariantCulture, out long number) ? number : null;
    private static decimal? Decimal(JsonElement value) =>
        decimal.TryParse(Text(value), NumberStyles.Float, CultureInfo.InvariantCulture, out decimal number) ? number : null;
    private static DateOnly? Date(JsonElement value) =>
        Text(value) is { Length: >= 10 } text && DateOnly.TryParseExact(text.AsSpan(0, 10), "yyyy-MM-dd",
            CultureInfo.InvariantCulture, DateTimeStyles.None, out var day) ? day : null;
    private static bool? Boolean(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        _ => null
    };
}
