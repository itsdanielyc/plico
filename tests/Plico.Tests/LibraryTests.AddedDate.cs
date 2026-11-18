using Microsoft.Data.Sqlite;
using Plico.Core;
using Xunit;

namespace Plico.Tests;

public sealed partial class LibraryTests
{
    [Fact]
    public void AddedDateSearchIntersectsTheLocalDayWithTextAndTags()
    {
        using var temporary = new TemporaryLibrary();
        using var library = InboxLibrary.Open(temporary.Path);
        var token = TestContext.Current.CancellationToken;
        var midnight = new DateTime(2024, 2, 29, 0, 0, 0, DateTimeKind.Local);
        var documents = CreateDatedLeases(library, new Dictionary<string, DateTimeOffset>
        {
            ["Before leap day"] = new(midnight.AddSeconds(-1)),
            ["Start of leap day"] = new(midnight),
            ["End of leap day"] = new(midnight.AddDays(1).AddSeconds(-1)),
            ["Start of March"] = new(midnight.AddDays(1))
        }, token);
        library.AddTag(documents["Start of leap day"].Id, "Rental");
        library.AddTag(documents["Before leap day"].Id, "Rental");

        var exactDay = library.Find(new DocumentFilter(Query: "ADDED:2024-02-29"), token).Items;
        Assert.Equal(new[] { "End of leap day", "Start of leap day" }, exactDay.Select(item => item.Title).Order(StringComparer.Ordinal));
        var combination = Assert.Single(library.Find(new DocumentFilter(Query: "norths added:2024-02-29 tag:rental"), token).Items);
        Assert.Equal(documents["Start of leap day"].Id, combination.Id);
        Assert.Contains(LetterKeyword, combination.Match, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(3, library.Find(new DocumentFilter(Query: "added:2024-02 added:2024"), token).Items.Count);
        Assert.Equal(4, library.Find(new DocumentFilter(Query: "added:2024"), token).Items.Count);
        Assert.Empty(library.Find(new DocumentFilter(Query: "added:2024-02 added:2024-03"), token).Items);

        library.SetTitle(documents["Start of March"].Id, "Added 2024 02 notes");
        Assert.Equal(documents["Start of March"].Id,
            Assert.Single(library.Find(new DocumentFilter(Query: "\"added:2024-02\""), token).Items).Id);
        foreach (string query in new[] { "added:", "added:2024-02-", "added:2024-2", "added:2023-02-29", "added:0000" })
        {
            Assert.Empty(library.Find(new DocumentFilter(Query: query), token).Items);
        }
    }

    [Fact]
    public void AddedDateSearchHandlesAClockChangeAcrossMidnight()
    {
        using var temporary = new TemporaryLibrary();
        using var library = InboxLibrary.Open(temporary.Path);
        var token = TestContext.Current.CancellationToken;
        var firstMidnight = new DateTimeOffset(2024, 10, 31, 23, 0, 0, TimeSpan.Zero);
        var documents = CreateDatedLeases(library, new Dictionary<string, DateTimeOffset>
        {
            ["During first November morning"] = firstMidnight.AddMinutes(10),
            ["Back in October"] = firstMidnight.AddMinutes(45),
            ["During second November morning"] = firstMidnight.AddMinutes(75)
        }, token);
        var daylightStart = TimeZoneInfo.TransitionTime.CreateFixedDateRule(new DateTime(1, 1, 1), 3, 1);
        var daylightEnd = TimeZoneInfo.TransitionTime.CreateFixedDateRule(new DateTime(1, 1, 1, 0, 30, 0), 11, 1);
        var rule = TimeZoneInfo.AdjustmentRule.CreateAdjustmentRule(new DateTime(2024, 1, 1), new DateTime(2024, 12, 31),
            TimeSpan.FromHours(1), daylightStart, daylightEnd);
        var zone = TimeZoneInfo.CreateCustomTimeZone("Midnight rollback", TimeSpan.Zero, "Midnight rollback", "Standard",
            "Daylight", [rule]);
        var catalog = new LibraryCatalog(Path.Combine(library.RootPath, ".plico", "catalog.sqlite3"), zone);

        var november = catalog.Find(new DocumentFilter(Query: "added:2024-11-01"), token).Items;
        Assert.Equal(new[] { "During first November morning", "During second November morning" },
            november.Select(item => item.Title).Order(StringComparer.Ordinal));
        var october = Assert.Single(catalog.Find(new DocumentFilter(Query: "added:2024-10-31"), token).Items);
        Assert.Equal(documents["Back in October"].Id, october.Id);
    }

    private static Dictionary<string, DocumentSummary> CreateDatedLeases(InboxLibrary library,
        IReadOnlyDictionary<string, DateTimeOffset> addedDates, CancellationToken cancellationToken)
    {
        foreach (string title in addedDates.Keys)
        {
            File.Copy(Fixture(Letter), Path.Combine(library.PdfDirectory, title + ".pdf"));
        }
        Assert.Equal(addedDates.Count, library.Refresh(cancellationToken).Indexed);
        var documents = library.Find(new DocumentFilter(), cancellationToken).Items.ToDictionary(item => item.Title);
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = Path.Combine(library.RootPath, ".plico", "catalog.sqlite3"),
            Pooling = false
        }.ToString());
        connection.Open();
        using var transaction = connection.BeginTransaction();
        using var update = connection.CreateCommand();
        update.Transaction = transaction;
        update.CommandText = "UPDATE documents SET added_at = $added WHERE id = $id;";
        var added = update.Parameters.Add("$added", SqliteType.Integer);
        var id = update.Parameters.Add("$id", SqliteType.Integer);
        foreach (var (title, timestamp) in addedDates)
        {
            cancellationToken.ThrowIfCancellationRequested();
            added.Value = timestamp.ToUnixTimeSeconds();
            id.Value = documents[title].Id;
            Assert.Equal(1, update.ExecuteNonQuery());
        }
        transaction.Commit();
        return documents;
    }
}
