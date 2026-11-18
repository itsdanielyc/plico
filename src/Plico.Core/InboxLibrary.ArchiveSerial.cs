using Microsoft.Data.Sqlite;

namespace Plico.Core;

public sealed partial class InboxLibrary
{
    /// <summary>Reads the serial assigned to a catalog document, including unavailable or trashed documents.</summary>
    public long? ReadArchiveSerialNumber(long documentId) => _catalog.ReadArchiveSerialNumber(documentId);

    /// <summary>Assigns a free positive serial to a present document, or clears its current assignment.</summary>
    public void SetArchiveSerialNumber(long documentId, long? serial) => _catalog.SetArchiveSerialNumber(documentId, serial);

    /// <summary>Finds a present document by its exact archive serial number.</summary>
    public DocumentSummary? FindByArchiveSerialNumber(long serial) =>
        _catalog.FindArchiveSerialDocumentId(serial, presentOnly: true) is { } id ? _catalog.Get(id) : null;
}

internal sealed partial class LibraryCatalog
{
    private const string ArchiveSerialMigration = """
        CREATE TABLE IF NOT EXISTS document_asns (
            document_id INTEGER PRIMARY KEY REFERENCES documents(id) ON DELETE CASCADE,
            asn INTEGER NOT NULL UNIQUE CHECK (asn > 0)
        );
        """;

    internal long? ReadArchiveSerialNumber(long documentId)
    {
        using var connection = Connect();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT asn FROM document_asns WHERE document_id = $id;";
        command.Parameters.AddWithValue("$id", documentId);
        return command.ExecuteScalar() as long?;
    }

    internal long? FindArchiveSerialDocumentId(long serial, bool presentOnly)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(serial);
        using var connection = Connect();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT asn.document_id FROM document_asns asn JOIN documents d ON d.id = asn.document_id
            WHERE asn.asn = $serial AND ($presentOnly = 0 OR d.is_present = 1);
            """;
        command.Parameters.AddWithValue("$serial", serial);
        command.Parameters.AddWithValue("$presentOnly", presentOnly);
        return command.ExecuteScalar() as long?;
    }

    internal void SetArchiveSerialNumber(long documentId, long? serial)
    {
        using var connection = Connect();
        using var transaction = connection.BeginTransaction();
        WriteArchiveSerialNumber(connection, transaction, documentId, serial);
        transaction.Commit();
    }

    private static void WriteArchiveSerialNumber(SqliteConnection connection, SqliteTransaction transaction, long documentId, long? serial)
    {
        if (serial is <= 0) throw new ArgumentOutOfRangeException(nameof(serial), "Enter a positive archive serial number.");
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.Parameters.AddWithValue("$id", documentId);
        command.CommandText = "SELECT 1 FROM documents WHERE id = $id AND is_present = 1;";
        if (command.ExecuteScalar() is null) throw new InvalidOperationException(DocumentUnavailableMessage);
        if (serial is null)
        {
            command.CommandText = "DELETE FROM document_asns WHERE document_id = $id;";
            command.ExecuteNonQuery();
            return;
        }

        command.Parameters.AddWithValue("$serial", serial.Value);
        command.CommandText = "SELECT document_id FROM document_asns WHERE asn = $serial;";
        if (command.ExecuteScalar() is long other && other != documentId)
            throw new ArgumentException($"Archive serial number {serial} is already assigned in this library.");
        command.CommandText = """
            INSERT INTO document_asns (document_id, asn) VALUES ($id, $serial)
            ON CONFLICT(document_id) DO UPDATE SET asn = excluded.asn;
            """;
        command.ExecuteNonQuery();
    }
}
