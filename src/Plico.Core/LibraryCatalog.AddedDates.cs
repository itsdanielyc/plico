using Microsoft.Data.Sqlite;

namespace Plico.Core;

internal sealed partial class LibraryCatalog
{
    // A leading equality lets SQLite narrow the time range before sorting the matches.
    private const string AddedDateIndexMigration = """
        CREATE INDEX documents_added_at ON documents(is_present, added_at) WHERE is_present = 1;
        """;
    private const long SecondsPerDay = 24 * 60 * 60;
    private const long MaximumLocalOffsetSeconds = 14 * 60 * 60;
    private static readonly int UnixEpochDay = DateOnly.FromDateTime(DateTime.UnixEpoch).DayNumber;
    private readonly TimeZoneInfo? _addedDateTimeZone;

    private void ApplyAddedDateFilter(SqliteConnection connection, SqliteCommand command, List<string> conditions,
        SearchTerms.DatePeriod period)
    {
        int firstDay = period.FirstDay.DayNumber;
        int lastDay = period.LastDay.DayNumber;
        long nominalStart = (firstDay - UnixEpochDay) * SecondsPerDay;
        long nominalEnd = (lastDay + 1 - UnixEpochDay) * SecondsPerDay;
        RegisterLocalAddedDay(connection);

        // Keep the timestamp range indexable. Only candidates near the calendar edges need exact time-zone conversion.
        conditions.Add("""
            d.added_at >= $earliestAdded AND d.added_at < $latestAdded AND
            CASE WHEN d.added_at >= $certainStart AND d.added_at < $certainEnd THEN 1
                 ELSE plico_local_added_day(d.added_at) BETWEEN $firstDay AND $lastDay END
            """);
        command.Parameters.AddWithValue("$earliestAdded", nominalStart - MaximumLocalOffsetSeconds);
        command.Parameters.AddWithValue("$latestAdded", nominalEnd + MaximumLocalOffsetSeconds);
        command.Parameters.AddWithValue("$certainStart", nominalStart + MaximumLocalOffsetSeconds);
        command.Parameters.AddWithValue("$certainEnd", nominalEnd - MaximumLocalOffsetSeconds);
        command.Parameters.AddWithValue("$firstDay", firstDay);
        command.Parameters.AddWithValue("$lastDay", lastDay);
    }

    private void RegisterLocalAddedDay(SqliteConnection connection)
    {
        var localZone = _addedDateTimeZone ?? TimeZoneInfo.Local;
        connection.CreateFunction<long, int>("plico_local_added_day", timestamp =>
            DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(DateTimeOffset.FromUnixTimeSeconds(timestamp), localZone).DateTime)
                .DayNumber, isDeterministic: true);
    }
}
