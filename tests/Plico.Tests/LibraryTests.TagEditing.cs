using Microsoft.Data.Sqlite;
using Plico.Core;
using Xunit;

namespace Plico.Tests;

public sealed partial class LibraryTests
{
    [Fact]
    public void TagRenameUpdatesCapitalizationAndReachesTemporarilyMissingPdfs()
    {
        using var temporary = new TemporaryLibrary();
        using var library = InboxLibrary.Open(temporary.Path);
        var token = TestContext.Current.CancellationToken;
        BatchFixtures.AddTwoDocuments(library, token);
        var documents = library.Find(new DocumentFilter(), token).Items;
        var missing = documents[0];
        foreach (var document in documents) library.AddTag(document.Id, "property");
        library.AddTag(missing.Id, "Priority");
        library.SetTitle(missing.Id, "Filed lease");
        library.SetReviewed(missing.Id, true);
        string originalPath = library.ResolvePdf(missing);
        string heldFile = Path.Combine(temporary.Path, "held.pdf");
        File.Move(originalPath, heldFile);
        library.Refresh(token);

        Assert.Equal("Property", library.RenameTag("property", "Property", cancellationToken: token));
        Assert.Equal("Property", Assert.Single(library.GetTags()));
        Assert.Equal("Housing", library.RenameTag("Property", "Housing", cancellationToken: token));
        File.Move(heldFile, originalPath);
        library.Refresh(token);

        var renamed = library.Find(new DocumentFilter(Query: "tag:housing"), token).Items;
        Assert.Equal(2, renamed.Count);
        var returned = renamed.Single(item => item.Id == missing.Id);
        Assert.Equal("Filed lease", returned.Title);
        Assert.True(returned.Reviewed);
        Assert.Contains("Priority", returned.Tags);
        Assert.Empty(library.Find(new DocumentFilter(Tag: "property"), token).Items);
        Assert.Equal(File.ReadAllBytes(Fixture(Letter)), File.ReadAllBytes(originalPath));
    }

    [Fact]
    public void TagRenameRequiresAnExplicitMergeAndDeduplicatesExistingLinks()
    {
        using var temporary = new TemporaryLibrary();
        using var library = InboxLibrary.Open(temporary.Path);
        var token = TestContext.Current.CancellationToken;
        BatchFixtures.AddTwoDocuments(library, token);
        var documents = library.Find(new DocumentFilter(), token).Items;
        foreach (var document in documents) library.AddTag(document.Id, "Vendor");
        library.AddTag(documents[1].Id, "Vendors");
        library.AddTag(documents[1].Id, "Retired");
        library.RemoveTag(documents[1].Id, "Retired");

        Assert.Throws<InvalidOperationException>(() => library.RenameTag("Vendor", "vendors", cancellationToken: token));
        Assert.Equal(2, library.Find(new DocumentFilter(Tag: "Vendor"), token).Items.Count);
        Assert.Single(library.Find(new DocumentFilter(Tag: "Vendors"), token).Items);
        Assert.Contains("Retired", library.GetCatalogTagNames());

        Assert.Equal("Vendors", library.RenameTag("Vendor", "vendors", mergeIntoExisting: true, cancellationToken: token));

        Assert.Equal("Vendors", Assert.Single(library.GetTags()));
        Assert.All(library.Find(new DocumentFilter(), token).Items,
            document => Assert.Equal("Vendors", Assert.Single(document.Tags)));
        Assert.Empty(library.Find(new DocumentFilter(Tag: "Vendor"), token).Items);
    }

    [Fact]
    public void FailedTagMergeRollsBackBothSetsOfLinks()
    {
        using var temporary = new TemporaryLibrary();
        using var library = InboxLibrary.Open(temporary.Path);
        var token = TestContext.Current.CancellationToken;
        BatchFixtures.AddTwoDocuments(library, token);
        var documents = library.Find(new DocumentFilter(), token).Items;
        library.AddTag(documents[0].Id, "Source");
        library.AddTag(documents[1].Id, "Target");
        string catalogPath = Path.Combine(temporary.Path, ".plico", "catalog.sqlite3");
        using (var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = catalogPath, Pooling = false }.ToString()))
        {
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = """
                CREATE TRIGGER reject_fixture_tag_delete BEFORE DELETE ON tags WHEN old.tag_key = 'SOURCE' BEGIN
                    SELECT RAISE(ABORT, 'Synthetic tag merge failure');
                END;
                """;
            command.ExecuteNonQuery();
        }

        Assert.Throws<SqliteException>(() => library.RenameTag("Source", "Target", mergeIntoExisting: true, cancellationToken: token));

        Assert.Equal(documents[0].Id, Assert.Single(library.Find(new DocumentFilter(Tag: "Source"), token).Items).Id);
        Assert.Equal(documents[1].Id, Assert.Single(library.Find(new DocumentFilter(Tag: "Target"), token).Items).Id);
    }
}
