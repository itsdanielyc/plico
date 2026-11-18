using Microsoft.Data.Sqlite;

namespace Plico.Core;

internal sealed partial class LibraryCatalog
{
    private static string DocumentDateMigration => $$"""
        ALTER TABLE documents ADD COLUMN document_day INTEGER
            CHECK (document_day IS NULL OR (typeof(document_day) = 'integer' AND
                document_day BETWEEN {{DateOnly.MinValue.DayNumber}} AND {{DateOnly.MaxValue.DayNumber}}));
        CREATE INDEX documents_document_day ON documents(document_day DESC, id DESC) WHERE is_present = 1;
        """;

    // O(k log n) for k source lookups across n catalog rows, with constant aggregate space.
    private const string SharedSourceDocumentDay = """
        (SELECT CASE WHEN count(d.document_day) = count(*) AND min(d.document_day) = max(d.document_day)
                     THEN min(d.document_day) END
         FROM json_each($ids) source
         LEFT JOIN documents d ON d.id = source.value AND d.is_present = 1)
        """;

    private static void ApplyDocumentDateFilter(SqliteCommand command, List<string> conditions, SearchTerms.DatePeriod period)
    {
        conditions.Add("d.document_day BETWEEN $firstDocumentDay AND $lastDocumentDay");
        command.Parameters.AddWithValue("$firstDocumentDay", period.FirstDay.DayNumber);
        command.Parameters.AddWithValue("$lastDocumentDay", period.LastDay.DayNumber);
    }

    internal void SetDocumentDate(long documentId, DateOnly? date)
    {
        using var connection = Connect();
        using var command = connection.CreateCommand();
        command.CommandText = "UPDATE documents SET document_day = $day WHERE id = $id AND is_present = 1;";
        command.Parameters.AddWithValue("$day", (object?)date?.DayNumber ?? DBNull.Value);
        command.Parameters.AddWithValue("$id", documentId);
        if (command.ExecuteNonQuery() != 1) throw new InvalidOperationException(DocumentUnavailableMessage);
    }
}
