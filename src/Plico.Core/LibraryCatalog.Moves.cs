namespace Plico.Core;

internal sealed record PdfMoveIntent(long DocumentId, string SourceRelativePath, string DestinationRelativePath, byte[] Fingerprint);

internal sealed partial class LibraryCatalog
{
    private const string PdfMoveMigration = """
        CREATE TABLE pending_pdf_move (
            slot INTEGER PRIMARY KEY CHECK (slot = 1),
            document_id INTEGER NOT NULL REFERENCES documents(id) ON DELETE CASCADE,
            source_path TEXT NOT NULL,
            destination_path TEXT NOT NULL,
            pdf_sha256 BLOB NOT NULL CHECK (length(pdf_sha256) = 32)
        );
        """;

    internal PdfMoveIntent? ReadPendingPdfMove()
    {
        using var connection = Connect();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT document_id, source_path, destination_path, pdf_sha256 FROM pending_pdf_move WHERE slot = 1;";
        using var row = command.ExecuteReader();
        return row.Read() ? new PdfMoveIntent(row.GetInt64(0), row.GetString(1), row.GetString(2), row.GetFieldValue<byte[]>(3)) : null;
    }

    internal void RecordPdfMove(PdfMoveIntent move)
    {
        using var connection = Connect();
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO pending_pdf_move (slot, document_id, source_path, destination_path, pdf_sha256)
            VALUES (1, $id, $source, $destination, $hash);
            """;
        command.Parameters.AddWithValue("$id", move.DocumentId);
        command.Parameters.AddWithValue("$source", move.SourceRelativePath);
        command.Parameters.AddWithValue("$destination", move.DestinationRelativePath);
        command.Parameters.AddWithValue("$hash", move.Fingerprint);
        command.ExecuteNonQuery();
    }

    internal void ClearPendingPdfMove()
    {
        using var connection = Connect();
        using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM pending_pdf_move WHERE slot = 1;";
        command.ExecuteNonQuery();
    }
}
