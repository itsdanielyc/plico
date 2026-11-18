using System.Text.Json;

namespace Plico.Core;

internal enum PortableDocumentState { Present, Missing, Trashed }

internal sealed record PortableDocumentMetadata(long Id, string Title, string RelativePath, PortableDocumentState State,
    DateTimeOffset AddedAt, DateOnly? DocumentDate, bool Reviewed, bool HasText, int PageCount, IReadOnlyList<string> Tags,
    long FileBytes, string? Sha256, DateTimeOffset? TrashedAt, string? OcrLanguage, long? ArchiveSerialNumber,
    DocumentMetadata Metadata, IReadOnlyList<DocumentDeadline> Deadlines);

internal sealed partial class LibraryCatalog
{
    internal IEnumerable<PortableDocumentMetadata> ReadPortableMetadata(CancellationToken token)
    {
        using var connection = Connect();
        using var command = connection.CreateCommand();
        command.CommandText = $"""
            SELECT d.id, COALESCE(d.display_title, d.title), COALESCE(trash.original_path, d.relative_path), d.is_present,
                   d.added_at, d.document_day, d.reviewed, d.has_text, d.page_count, {FilingTagsSql},
                   d.file_bytes, d.pdf_sha256, trash.trashed_at, COALESCE(language.language, d.ocr_copy_language),
                   serial.asn, {StructuredMetadataProjection},
                   (SELECT json_group_array(json_object('kind', kind, 'day', day, 'leadDays', lead_days) ORDER BY kind)
                       FROM document_deadlines WHERE document_id = d.id)
            FROM documents d
            LEFT JOIN document_trash trash ON trash.document_id = d.id
            LEFT JOIN document_ocr_languages language ON language.document_id = d.id
            LEFT JOIN document_asns serial ON serial.document_id = d.id
            LEFT JOIN document_metadata m ON m.document_id = d.id
            ORDER BY d.id;
            """;
        using var rows = command.ExecuteReader();
        while (rows.Read())
        {
            token.ThrowIfCancellationRequested();
            var state = rows.GetInt32(3) switch
            {
                1 => PortableDocumentState.Present,
                0 => PortableDocumentState.Missing,
                _ => PortableDocumentState.Trashed
            };
            yield return new(rows.GetInt64(0), rows.GetString(1), rows.GetString(2), state,
                DateTimeOffset.FromUnixTimeSeconds(rows.GetInt64(4)),
                rows.IsDBNull(5) ? null : DateOnly.FromDayNumber(rows.GetInt32(5)),
                rows.GetBoolean(6), rows.GetBoolean(7), rows.GetInt32(8),
                JsonSerializer.Deserialize<string[]>(rows.GetString(9)) ?? [], rows.GetInt64(10),
                rows.IsDBNull(11) ? null : Convert.ToHexString(rows.GetFieldValue<byte[]>(11)),
                rows.IsDBNull(12) ? null : DateTimeOffset.FromUnixTimeSeconds(rows.GetInt64(12)),
                rows.IsDBNull(13) ? null : rows.GetString(13),
                rows.IsDBNull(14) ? null : rows.GetInt64(14), ReadStructuredMetadata(rows, 15), ReadPortableDeadlines(rows.GetString(19)));
        }
    }

    private sealed record PortableDeadline(DeadlineKind Kind, int Day, int? LeadDays);

    private static IReadOnlyList<DocumentDeadline> ReadPortableDeadlines(string json) => ReadMetadataJson<PortableDeadline[]>(json)
        .Select(value => new DocumentDeadline(value.Kind, DateOnly.FromDayNumber(value.Day), value.LeadDays)).ToArray();

    internal void VisitSearchResults(DocumentFilter filter, Action<DocumentSummary> visit, CancellationToken token)
    {
        var result = Query(filter with { Offset = 0 }, cancellationToken: token, maximumResults: int.MaxValue, visit: visit);
        if (result.QueryIssue is not null) throw new ArgumentException("Complete the search before exporting its results.");
    }
}
