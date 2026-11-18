using Microsoft.Data.Sqlite;
using Plico.Core;
using Xunit;

namespace Plico.Tests;

public sealed partial class LibraryTests
{
    [Fact]
    public async Task SavedSearchFollowsTagRenameAndMergeAcrossBackup()
    {
        var token = TestContext.Current.CancellationToken;
        using var temporary = new TemporaryLibrary();
        using var backupFolder = new TemporaryLibrary();
        long searchId;
        long leaseId;
        long ledgerId;
        using (var library = InboxLibrary.Open(temporary.Path))
        {
            var lease = await library.ImportAsync(Fixture(Letter), token);
            var ledger = await library.ImportAsync(Fixture("Match Ledger.pdf"), token);
            leaseId = lease.Id;
            ledgerId = ledger.Id;
            library.AddTag(lease.Id, "Property");
            library.AddTag(ledger.Id, "Household");
            Assert.Equal(2, library.Find(new DocumentFilter(Query: "renewal"), token).Items.Count);
            var saved = library.SaveSearch("  Property renewals  ", "  renewal  ", DocumentSort.Title, "property");
            Assert.Equal("Property renewals", saved.Name);
            Assert.Equal("Property", saved.ScopeTag);
            Assert.Equal("renewal", saved.Query);
            searchId = saved.Id;
        }

        using var reopened = InboxLibrary.Open(temporary.Path);
        reopened.RenameTag("Property", "Housing", cancellationToken: token);
        var renamed = Assert.Single(reopened.GetSavedSearches());
        Assert.Equal(searchId, renamed.Id);
        Assert.Equal("Housing", renamed.ScopeTag);
        Assert.Equal(leaseId, Assert.Single(reopened.Find(new(renamed.Query, renamed.ScopeTag, Sort: renamed.Sort), token).Items).Id);

        reopened.RenameTag("Housing", "Household", mergeIntoExisting: true, cancellationToken: token);
        var backup = await reopened.BackupAsync(backupFolder.Path, cancellationToken: token);
        using var copied = InboxLibrary.Open(backup.Location);
        var savedCopy = Assert.Single(copied.GetSavedSearches());
        Assert.Equal(searchId, savedCopy.Id);
        Assert.Equal("Household", savedCopy.ScopeTag);
        Assert.Equal(DocumentSort.Title, savedCopy.Sort);
        Assert.Equal(new[] { leaseId, ledgerId }, copied.Find(new(savedCopy.Query, savedCopy.ScopeTag, Sort: savedCopy.Sort), token)
            .Items.Select(document => document.Id));
    }

    [Fact]
    public void SavedSearchEditsRejectDuplicateNamesAndInvalidQueries()
    {
        using var temporary = new TemporaryLibrary();
        using var library = InboxLibrary.Open(temporary.Path);
        var primary = library.SaveSearch("Café files", "renewal", DocumentSort.Title);
        var secondary = library.SaveSearch("Second search", "agreement", DocumentSort.Newest);

        Assert.Throws<InvalidOperationException>(() => library.SaveSearch("  CAFE\u0301 FILES ", "agreement", DocumentSort.Newest));
        Assert.Throws<InvalidOperationException>(() => library.UpdateSavedSearch(secondary.Id, "café files", "invoice", DocumentSort.Automatic));
        Assert.Throws<InvalidOperationException>(() => library.SaveSearch("Missing scope", "invoice", DocumentSort.Newest, "Unassigned"));
        Assert.Throws<ArgumentException>(() => library.UpdateSavedSearch(secondary.Id, "New name", "folder:", DocumentSort.Title));
        Assert.Throws<ArgumentException>(() => library.SaveSearch("Empty search", "  ", DocumentSort.Newest));
        Assert.Equal(new[] { primary, secondary }, library.GetSavedSearches());

        var updated = library.UpdateSavedSearch(secondary.Id, "Updated search", "added:2026-11", DocumentSort.Automatic);
        Assert.Equal(secondary.Id, updated.Id);
        Assert.Equal("added:2026-11", updated.Query);
        Assert.Equal(DocumentSort.Automatic, updated.Sort);
        library.RemoveSavedSearch(primary.Id);
        library.RemoveSavedSearch(primary.Id);
        Assert.Equal(updated, Assert.Single(library.GetSavedSearches()));
    }

    [Fact]
    public async Task VersionSevenCatalogUpgradesWithoutChangingTheIndexedPdf()
    {
        var token = TestContext.Current.CancellationToken;
        using var temporary = new TemporaryLibrary();
        long originalId;
        using (var library = InboxLibrary.Open(temporary.Path))
        {
            var document = await library.ImportAsync(Fixture(Letter), token);
            originalId = document.Id;
            library.AddTag(originalId, "Property");
            library.SetReviewed(originalId, true);
        }

        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = Path.Combine(temporary.Path, ".plico", "catalog.sqlite3"),
            Pooling = false
        }.ToString();
        using (var database = new SqliteConnection(connectionString))
        {
            database.Open();
            using var command = database.CreateCommand();
            command.CommandText = """
                DROP INDEX documents_text_presence;
                DROP INDEX documents_document_day;
                ALTER TABLE documents DROP COLUMN document_day;
                DROP TABLE saved_searches;
                PRAGMA user_version = 7;
                """;
            command.ExecuteNonQuery();
        }

        using var reopened = InboxLibrary.Open(temporary.Path);
        var match = Assert.Single(reopened.Find(new DocumentFilter(Query: LetterKeyword, Tag: "Property"), token).Items);
        Assert.Equal(originalId, match.Id);
        Assert.True(match.Reviewed);
        Assert.Equal(File.ReadAllBytes(Fixture(Letter)), File.ReadAllBytes(reopened.ResolvePdf(match)));
        Assert.Empty(reopened.GetSavedSearches());
        Assert.Equal("Property", reopened.SaveSearch("Household folder", LetterKeyword, DocumentSort.Newest, "Property").ScopeTag);

        using var migrated = new SqliteConnection(connectionString);
        migrated.Open();
        using var version = migrated.CreateCommand();
        version.CommandText = "PRAGMA user_version;";
        Assert.Equal(10L, version.ExecuteScalar());
    }
}
