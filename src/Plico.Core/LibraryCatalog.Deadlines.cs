using Microsoft.Data.Sqlite;
using System.Text.Json;

namespace Plico.Core;

internal sealed partial class LibraryCatalog
{
    private static string DeadlineMigration => $"""
        CREATE TABLE IF NOT EXISTS document_deadlines (
            document_id INTEGER NOT NULL REFERENCES documents(id) ON DELETE CASCADE,
            kind INTEGER NOT NULL,
            day INTEGER NOT NULL CHECK (day BETWEEN 0 AND {DateOnly.MaxValue.DayNumber}),
            lead_days INTEGER CHECK (lead_days BETWEEN 0 AND {DocumentDeadline.MaximumLeadDays}),
            dismissed INTEGER NOT NULL DEFAULT 0 CHECK (dismissed IN (0, 1)),
            event_uid TEXT NOT NULL UNIQUE,
            PRIMARY KEY (document_id, kind)
        ) WITHOUT ROWID;
        CREATE INDEX IF NOT EXISTS deadlines_due ON document_deadlines(day - lead_days)
            WHERE dismissed = 0 AND lead_days IS NOT NULL;
        """;

    internal IReadOnlyList<DocumentDeadline> ReadDeadlines(long documentId)
    {
        using var connection = Connect();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT kind, day, lead_days FROM document_deadlines WHERE document_id = $id ORDER BY kind;";
        command.Parameters.AddWithValue("$id", documentId);
        using var rows = command.ExecuteReader();
        var deadlines = new List<DocumentDeadline>();
        while (rows.Read()) deadlines.Add(ReadDeadline(rows, 0));
        return deadlines;
    }

    internal void SaveDeadlines(long documentId, IReadOnlyList<DocumentDeadline> deadlines, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(deadlines);
        token.ThrowIfCancellationRequested();
        var kinds = new HashSet<DeadlineKind>();
        foreach (var deadline in deadlines)
        {
            if (!Enum.IsDefined(deadline.Kind) || !kinds.Add(deadline.Kind))
                throw new ArgumentException("Set one date for each deadline type.");
            if (deadline.LeadDays is < 0 or > DocumentDeadline.MaximumLeadDays)
                throw new ArgumentException($"Enter a reminder between 0 and {DocumentDeadline.MaximumLeadDays} days before the date.");
        }
        using var connection = Connect();
        using var transaction = connection.BeginTransaction();
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.Parameters.AddWithValue("$id", documentId);
        command.CommandText = "SELECT 1 FROM documents WHERE id = $id AND is_present = 1;";
        if (command.ExecuteScalar() is null) throw new InvalidOperationException(DocumentUnavailableMessage);
        command.CommandText = "DELETE FROM document_deadlines WHERE document_id = $id AND kind NOT IN (SELECT value FROM json_each($kinds));";
        command.Parameters.AddWithValue("$kinds", JsonSerializer.Serialize(kinds.Select(kind => (int)kind)));
        command.ExecuteNonQuery();
        command.Parameters.RemoveAt("$kinds");
        command.CommandText = """
            INSERT INTO document_deadlines (document_id, kind, day, lead_days, event_uid) VALUES ($id, $kind, $day, $lead, $uid)
            ON CONFLICT(document_id, kind) DO UPDATE SET
                dismissed = CASE WHEN day = excluded.day AND lead_days IS excluded.lead_days THEN dismissed ELSE 0 END,
                day = excluded.day, lead_days = excluded.lead_days;
            """;
        var kindParameter = command.Parameters.Add("$kind", SqliteType.Integer);
        var day = command.Parameters.Add("$day", SqliteType.Integer);
        var lead = command.Parameters.Add("$lead", SqliteType.Integer);
        var uid = command.Parameters.Add("$uid", SqliteType.Text);
        foreach (var deadline in deadlines)
        {
            token.ThrowIfCancellationRequested();
            kindParameter.Value = (int)deadline.Kind;
            day.Value = deadline.Date.DayNumber;
            lead.Value = DbValue(deadline.LeadDays);
            uid.Value = Guid.NewGuid().ToString("N");
            command.ExecuteNonQuery();
        }
        token.ThrowIfCancellationRequested();
        transaction.Commit();
    }

    internal IReadOnlyList<DueReminder> ReadDueReminders(DateOnly date, CancellationToken token)
    {
        using var connection = Connect();
        using var command = CreateDueReminderQuery(connection, date,
            "a.document_id, COALESCE(d.display_title, d.title), a.kind, a.day, a.lead_days");
        var reminders = new List<DueReminder>();
        using var rows = command.ExecuteReader();
        while (rows.Read())
        {
            token.ThrowIfCancellationRequested();
            reminders.Add(new(rows.GetInt64(0), rows.GetString(1), ReadDeadline(rows, 2)));
        }
        return reminders;
    }

    internal long CountDueReminders(DateOnly date)
    {
        using var connection = Connect();
        using var command = CreateDueReminderQuery(connection, date, "COUNT(*)", order: false);
        return (long)command.ExecuteScalar()!;
    }

    private static SqliteCommand CreateDueReminderQuery(SqliteConnection connection, DateOnly date, string columns, bool order = true)
    {
        var command = connection.CreateCommand();
        command.CommandText = $"""
            SELECT {columns} FROM document_deadlines a JOIN documents d ON d.id = a.document_id
            WHERE d.is_present = 1 AND a.lead_days IS NOT NULL AND a.dismissed = 0 AND a.day - a.lead_days <= $today
            {(order ? "ORDER BY a.day, a.document_id, a.kind" : "")};
            """;
        command.Parameters.AddWithValue("$today", date.DayNumber);
        return command;
    }

    internal bool DismissReminder(long documentId, DeadlineKind kind)
    {
        using var connection = Connect();
        using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE document_deadlines SET dismissed = 1 WHERE document_id = $id AND kind = $kind AND dismissed = 0;
            """;
        command.Parameters.AddWithValue("$id", documentId);
        command.Parameters.AddWithValue("$kind", (int)kind);
        return command.ExecuteNonQuery() > 0;
    }

    internal IEnumerable<CalendarDeadline> ReadCalendarDeadlines(IReadOnlyList<long>? documentIds, CancellationToken token)
    {
        using var connection = Connect();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT a.event_uid, COALESCE(d.display_title, d.title), a.kind, a.day, a.lead_days
            FROM document_deadlines a JOIN documents d ON d.id = a.document_id
            WHERE d.is_present = 1 AND ($ids IS NULL OR a.document_id IN (SELECT value FROM json_each($ids)))
            ORDER BY a.day, a.document_id, a.kind;
            """;
        command.Parameters.AddWithValue("$ids", documentIds is null ? DBNull.Value : JsonSerializer.Serialize(documentIds));
        using var rows = command.ExecuteReader();
        while (rows.Read())
        {
            token.ThrowIfCancellationRequested();
            yield return new(rows.GetString(0), rows.GetString(1), ReadDeadline(rows, 2));
        }
    }

    private static DocumentDeadline ReadDeadline(SqliteDataReader row, int first) =>
        new((DeadlineKind)row.GetInt32(first), DateOnly.FromDayNumber(row.GetInt32(first + 1)),
            row.IsDBNull(first + 2) ? null : row.GetInt32(first + 2));
}
