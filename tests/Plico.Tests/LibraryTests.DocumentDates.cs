using Microsoft.Data.Sqlite;
using Plico.Core;
using Xunit;

namespace Plico.Tests;

public sealed partial class LibraryTests
{
    [Fact]
    public void DatedSearchUsesTheDocumentDayAlongsideIntakeDateTextAndTags()
    {
        const string leapLease = "February lease";
        const string earlierImport = "Earlier import";
        const string nextMonth = "March lease";
        const string undated = "Undated lease";
        using var temporary = new TemporaryLibrary();
        using var library = InboxLibrary.Open(temporary.Path);
        var token = TestContext.Current.CancellationToken;
        var documents = CreateDatedLeases(library, new Dictionary<string, DateTimeOffset>
        {
            [leapLease] = new(new DateTime(2026, 11, 18, 12, 0, 0, DateTimeKind.Local)),
            [earlierImport] = new(new DateTime(2026, 10, 31, 12, 0, 0, DateTimeKind.Local)),
            [nextMonth] = new(new DateTime(2026, 11, 18, 12, 0, 0, DateTimeKind.Local)),
            [undated] = new(new DateTime(2024, 2, 29, 12, 0, 0, DateTimeKind.Local))
        }, token);
        var leapDay = new DateOnly(2024, 2, 29);
        library.SetDocumentDate(documents[leapLease].Id, leapDay);
        library.SetDocumentDate(documents[earlierImport].Id, leapDay);
        library.SetDocumentDate(documents[nextMonth].Id, leapDay.AddDays(1));
        foreach (string label in new[] { leapLease, earlierImport, undated }) library.AddTag(documents[label].Id, "Rental");

        Assert.Equal(3, library.Find(new DocumentFilter(Query: "dated:2024"), token).Items.Count);
        Assert.Equal(2, library.Find(new DocumentFilter(Query: "DATED:2024-02 dated:2024"), token).Items.Count);
        Assert.Equal(2, library.Find(new DocumentFilter(Query: "dated:2024-02-29"), token).Items.Count);
        const string query = "norths dated:2024-02 added:2026-11 folder:.";
        var saved = library.SaveSearch("February rent", query, DocumentSort.DocumentDate, "Rental");
        var match = Assert.Single(library.Find(new(saved.Query, saved.ScopeTag, Sort: saved.Sort), token).Items);
        Assert.Equal(documents[leapLease].Id, match.Id);
        Assert.Equal(leapDay, match.DocumentDate);
        Assert.Contains(LetterKeyword, match.Match, StringComparison.OrdinalIgnoreCase);

        var byIntake = Assert.Single(library.Find(new DocumentFilter(Query: "added:2024-02-29"), token).Items);
        Assert.Equal(documents[undated].Id, byIntake.Id);
        Assert.Null(byIntake.DocumentDate);
        library.SetTitle(byIntake.Id, "Dated 2024 02 note");
        Assert.Equal(byIntake.Id, Assert.Single(library.Find(new DocumentFilter(Query: "\"dated:2024-02\""), token).Items).Id);
        foreach (string invalid in new[] { "dated:", "dated:2023-02-29", "dated:2024-2", "dated:2024-02 dated:2024-03" })
        {
            Assert.Empty(library.Find(new DocumentFilter(Query: invalid), token).Items);
        }
        library.SetDocumentDate(match.Id, null);
        Assert.Empty(library.Find(new(saved.Query, saved.ScopeTag, Sort: saved.Sort), token).Items);
    }

    [Fact]
    public async Task DocumentDateSurvivesAnUpgradeMoveRefreshAndBackup()
    {
        var token = TestContext.Current.CancellationToken;
        using var temporary = new TemporaryLibrary();
        using var backupFolder = new TemporaryLibrary();
        long documentId;
        using (var library = InboxLibrary.Open(temporary.Path))
        {
            var document = await library.ImportAsync(Fixture(Letter), token);
            documentId = document.Id;
            library.AddTag(document.Id, "Property");
            library.SetReviewed(document.Id, true);
            library.SaveSearch("Renewals", LetterKeyword, DocumentSort.Title, "Property");
        }

        using (var database = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = Path.Combine(temporary.Path, ".plico", "catalog.sqlite3"), Pooling = false
        }.ToString()))
        {
            database.Open();
            using var command = database.CreateCommand();
            command.CommandText = """
                DROP INDEX documents_text_presence;
                DROP INDEX documents_document_day;
                ALTER TABLE documents DROP COLUMN document_day;
                PRAGMA user_version = 8;
                """;
            command.ExecuteNonQuery();
        }

        using var reopened = InboxLibrary.Open(temporary.Path);
        var original = Assert.Single(reopened.Find(new DocumentFilter(), token).Items);
        Assert.Equal(documentId, original.Id);
        Assert.Null(original.DocumentDate);
        Assert.Equal("Renewals", Assert.Single(reopened.GetSavedSearches()).Name);
        var date = new DateOnly(2024, 2, 29);
        reopened.SetDocumentDate(documentId, date);
        string folder = Directory.CreateDirectory(Path.Combine(reopened.PdfDirectory, "Filed")).FullName;
        var filed = await reopened.MovePdfAsync(documentId, folder, token);
        Assert.Equal(date, filed.DocumentDate);
        string path = reopened.ResolvePdf(filed);
        Assert.Equal(File.ReadAllBytes(Fixture(Letter)), File.ReadAllBytes(path));
        // Replace the file stamp and fingerprint so refresh reindexes the document content.
        File.AppendAllText(path, "\n");
        byte[] editedPdf = File.ReadAllBytes(path);
        Assert.Equal(1, reopened.Refresh(token).Indexed);
        var backup = await reopened.BackupAsync(backupFolder.Path, cancellationToken: token);
        using var restored = InboxLibrary.Open(backup.Location);
        var record = Assert.Single(restored.Find(new DocumentFilter(Query: "dated:2024-02 tag:property folder:Filed"), token).Items);
        Assert.Equal(documentId, record.Id);
        Assert.True(record.Reviewed);
        Assert.Equal(original.AddedAt, record.AddedAt);
        Assert.Equal(date, record.DocumentDate);
        Assert.Equal(editedPdf, File.ReadAllBytes(restored.ResolvePdf(record)));
        Assert.Throws<InvalidOperationException>(() => restored.SetDocumentDate(long.MaxValue, date));
    }
}
