using Microsoft.Data.Sqlite;

namespace Plico.Core;

internal sealed partial class LibraryCatalog
{
    private static string MetadataSearchCondition(SqliteCommand command, SearchTerms.MetadataSearchTerm term)
    {
        string parameter = $"$metadata{command.Parameters.Count}";
        command.Parameters.AddWithValue(parameter, term.Kind == SearchTerms.MetadataSearchKind.ArchiveSerial ? (object)term.Serial : term.NameKey);
        return term.Kind switch
        {
            SearchTerms.MetadataSearchKind.Correspondent => $"""
                d.id IN (SELECT dc.document_id FROM correspondent_names n
                         JOIN document_correspondents dc ON dc.correspondent_id = n.correspondent_id WHERE n.name_key = {parameter})
                """,
            SearchTerms.MetadataSearchKind.DocumentType => $"""
                d.id IN (SELECT m.document_id FROM document_types t JOIN document_metadata m ON m.type_id = t.id
                         WHERE t.name_key = {parameter})
                """,
            SearchTerms.MetadataSearchKind.Field => $"""
                d.id IN (SELECT v.document_id FROM custom_fields f JOIN document_custom_fields v ON v.field_id = f.id
                         WHERE f.name_key = {parameter})
                """,
            SearchTerms.MetadataSearchKind.ArchiveSerial => $"d.id IN (SELECT document_id FROM document_asns WHERE asn = {parameter})",
            _ => throw new InvalidOperationException("Unknown metadata filter.")
        };
    }
}
