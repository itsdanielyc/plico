using Microsoft.Data.Sqlite;

namespace Plico.Core;

internal sealed partial class LibraryCatalog
{
    internal void Relocate(long documentId, string relativePath, string filename, FileVersion version, PdfMoveIntent? move = null)
    {
        using var connection = Connect();
        using var transaction = move is null ? null : connection.BeginTransaction();
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        ConfigureRelocationCommand(command);
        int changed = UpdateRelocatedPdf(command, documentId, relativePath, filename, version, move?.SourceRelativePath, move?.Fingerprint);
        if (transaction is null) return;
        RequireSelection(changed, 1);
        command.Parameters.Clear();
        command.CommandText = "DELETE FROM pending_pdf_move WHERE slot = 1;";
        command.ExecuteNonQuery();
        transaction.Commit();
    }

    /// <summary>Changes all matched paths together, including paths exchanged between documents.</summary>
    internal void RelocateBatch(IReadOnlyList<CatalogRelocation> moves, CancellationToken cancellationToken)
    {
        using var connection = Connect();
        using var transaction = connection.BeginTransaction();
        using var reserve = connection.CreateCommand();
        reserve.Transaction = transaction;
        reserve.CommandText = """
            UPDATE documents SET relative_path = $prefix || id
            WHERE id = $id AND relative_path = $source;
            """;
        reserve.Parameters.AddWithValue("$prefix", $".plico-relocation-{Guid.NewGuid():N}/");
        var reservedId = reserve.Parameters.Add("$id", SqliteType.Integer);
        var reservedSource = reserve.Parameters.Add("$source", SqliteType.Text);
        // Vacate the old keys inside the transaction before assigning any of the final paths.
        foreach (var move in moves)
        {
            cancellationToken.ThrowIfCancellationRequested();
            reservedId.Value = move.DocumentId;
            reservedSource.Value = move.SourceRelativePath;
            RequireSelection(reserve.ExecuteNonQuery(), 1);
        }

        using var relocate = connection.CreateCommand();
        relocate.Transaction = transaction;
        ConfigureRelocationCommand(relocate);
        foreach (var move in moves)
        {
            cancellationToken.ThrowIfCancellationRequested();
            RequireSelection(UpdateRelocatedPdf(relocate, move.DocumentId, move.DestinationRelativePath, move.Filename,
                move.Version, previousPath: null, fingerprint: null), 1);
        }
        cancellationToken.ThrowIfCancellationRequested();
        transaction.Commit();
    }

    private static void ConfigureRelocationCommand(SqliteCommand command)
    {
        command.CommandText = """
            UPDATE documents SET relative_path = $path, modified_ticks = $modified, file_bytes = $bytes, is_present = 1,
                title = CASE WHEN display_title IS NULL THEN $name ELSE display_title || char(10) || $name END,
                pdf_sha256 = COALESCE($fingerprint, pdf_sha256)
            WHERE id = $id AND ($previous IS NULL OR relative_path = $previous);
            """;
        command.Parameters.Add("$path", SqliteType.Text);
        command.Parameters.Add("$modified", SqliteType.Integer);
        command.Parameters.Add("$bytes", SqliteType.Integer);
        command.Parameters.Add("$name", SqliteType.Text);
        command.Parameters.Add("$id", SqliteType.Integer);
        command.Parameters.Add("$previous", SqliteType.Text);
        command.Parameters.Add("$fingerprint", SqliteType.Blob);
    }

    private static int UpdateRelocatedPdf(SqliteCommand command, long documentId, string relativePath, string filename,
        FileVersion version, string? previousPath, byte[]? fingerprint)
    {
        command.Parameters["$path"].Value = relativePath;
        command.Parameters["$modified"].Value = version.LastModifiedTicks;
        command.Parameters["$bytes"].Value = version.Length;
        command.Parameters["$name"].Value = filename;
        command.Parameters["$id"].Value = documentId;
        command.Parameters["$previous"].Value = (object?)previousPath ?? DBNull.Value;
        command.Parameters["$fingerprint"].Value = (object?)fingerprint ?? DBNull.Value;
        return command.ExecuteNonQuery();
    }
}
