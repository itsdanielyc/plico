using Microsoft.Data.Sqlite;
using Plico.Core;
using Xunit;

namespace Plico.Tests;

public sealed partial class LibraryTests
{
    [Fact]
    public async Task TextAvailabilityComposesWithOtherFiltersAndTracksThePdf()
    {
        const string scanFixture = "Scanned Receipt.pdf";
        const string archiveName = "Archive scan";
        var date = new DateOnly(2024, 2, 29);
        var token = TestContext.Current.CancellationToken;
        using var temporary = new TemporaryLibrary();
        using var library = InboxLibrary.Open(temporary.Path);
        string filed = Directory.CreateDirectory(Path.Combine(library.PdfDirectory, "Filed")).FullName;
        File.Copy(Fixture(scanFixture), Path.Combine(filed, $"{archiveName}.pdf"));
        File.Copy(Fixture(scanFixture), Path.Combine(library.PdfDirectory, "Inbox scan.pdf"));
        File.Copy(Fixture(Mixed), Path.Combine(library.PdfDirectory, Mixed));
        Assert.Equal(3, library.Refresh(token).Indexed);
        var documents = library.Find(new DocumentFilter(), token).Items;
        var archive = Assert.Single(documents, document => document.Title == archiveName);
        var mixed = Assert.Single(documents, document => document.PageCount == 2);
        var inbox = Assert.Single(documents, document => document.Title == "Inbox scan");
        library.AddTag(archive.Id, "Tax");
        library.SetDocumentDate(archive.Id, date);
        library.SetReviewed(archive.Id, true);

        Assert.Equal(2, library.Find(new DocumentFilter(Query: "text:missing"), token).Items.Count);
        Assert.Equal(mixed.Id, Assert.Single(library.Find(new DocumentFilter(Query: "TEXT:Present text:present"), token).Items).Id);
        const string narrow = "archive text:missing tag:tax dated:2024-02 folder:Filed status:reviewed";
        Assert.Equal(archive.Id, Assert.Single(library.Find(new DocumentFilter(Query: narrow), token).Items).Id);
        Assert.Equal(mixed.Id, Assert.Single(library.Find(new DocumentFilter(Query: $"{LetterKeyword} text:present"), token).Items).Id);
        library.SetTitle(mixed.Id, "Note text missing");
        Assert.Equal(mixed.Id, Assert.Single(library.Find(new DocumentFilter(Query: "\"text:missing\""), token).Items).Id);

        var copy = (await library.CreateSearchableCopyAsync(archive.Id, token)).Document;
        Assert.Equal(copy.Id, Assert.Single(library.Find(new DocumentFilter(Query: "text:present tag:tax dated:2024-02"), token).Items).Id);
        Assert.Equal(archive.Id, Assert.Single(library.Find(new DocumentFilter(Query: narrow), token).Items).Id);

        File.Copy(Fixture(Letter), library.ResolvePdf(archive), overwrite: true);
        File.Delete(library.ResolvePdf(inbox));
        Assert.Equal(1, library.Refresh(token).Indexed);
        Assert.Empty(library.Find(new DocumentFilter(Query: narrow), token).Items);
        Assert.Empty(library.Find(new DocumentFilter(Query: "text:missing"), token).Items);
        var refreshed = Assert.Single(library.Find(new DocumentFilter(Query: "text:present folder:Filed status:reviewed"), token).Items);
        Assert.Equal(archive.Id, refreshed.Id);
        Assert.Equal(date, refreshed.DocumentDate);
    }

    [Fact]
    public async Task VersionNineLibraryGainsTextAvailabilityWithoutLosingOrganization()
    {
        var token = TestContext.Current.CancellationToken;
        using var temporary = new TemporaryLibrary();
        var date = new DateOnly(2024, 2, 29);
        long originalId;
        using (var library = InboxLibrary.Open(temporary.Path))
        {
            var document = await library.ImportAsync(Fixture(Letter), token);
            originalId = document.Id;
            library.AddTag(originalId, "Property");
            library.SetDocumentDate(originalId, date);
            library.SetReviewed(originalId, true);
            library.SaveSearch("Renewals", LetterKeyword, DocumentSort.Title, "Property");
        }

        using var database = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = Path.Combine(temporary.Path, ".plico", "catalog.sqlite3"), Pooling = false
        }.ToString());
        database.Open();
        using var command = database.CreateCommand();
        command.CommandText = "DROP INDEX IF EXISTS documents_text_presence; PRAGMA user_version = 9;";
        command.ExecuteNonQuery();
        database.Close();

        using var reopened = InboxLibrary.Open(temporary.Path);
        var record = Assert.Single(reopened.Find(new DocumentFilter(Query: "text:present tag:property"), token).Items);
        Assert.Equal(originalId, record.Id);
        Assert.Equal(date, record.DocumentDate);
        Assert.True(record.Reviewed);
        Assert.Equal("Renewals", Assert.Single(reopened.GetSavedSearches()).Name);
        Assert.Equal(File.ReadAllBytes(Fixture(Letter)), File.ReadAllBytes(reopened.ResolvePdf(record)));
        database.Open();
        command.CommandText = "PRAGMA user_version;";
        Assert.Equal(10L, command.ExecuteScalar());
        command.CommandText = "SELECT count(*) FROM sqlite_schema WHERE type = 'index' AND name = 'documents_text_presence';";
        Assert.Equal(1L, command.ExecuteScalar());
    }
}
