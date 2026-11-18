using Microsoft.Data.Sqlite;
using Plico.Core;
using Xunit;

namespace Plico.Tests;

public sealed class LibraryMoveTests
{
    private static string Fixture => Path.Combine(AppContext.BaseDirectory, "fixtures", "Lease Renewal.pdf");

    [Fact]
    public async Task UniqueMoveKeepsTitleTagsReviewAndRepeatDetection()
    {
        var token = TestContext.Current.CancellationToken;
        using var temporary = new TemporaryLibrary();
        using var library = InboxLibrary.Open(temporary.Path);
        var imported = await library.ImportFileAsync(Fixture, token);
        long id = imported.Document.Id;
        library.AddTag(id, "Housing");
        library.SetReviewed(id, true);
        library.SetTitle(id, "Next lease");

        var subfolder = Directory.CreateDirectory(Path.Combine(library.PdfDirectory, "Property")).FullName;
        var movedPath = Path.Combine(subfolder, "Renewal55.pdf");
        File.Move(library.ResolvePdf(imported.Document), movedPath);
        var refreshed = library.Refresh(token);
        var moved = Assert.Single(library.Find(new DocumentFilter(Query: "Renewal55", Tag: "housing")).Items);
        var repeated = await library.ImportFileAsync(Fixture, token);

        Assert.Equal(1, refreshed.Relocated);
        Assert.Equal(0, refreshed.Indexed);
        Assert.Equal(id, moved.Id);
        Assert.Equal("Next lease", moved.Title);
        Assert.True(moved.Reviewed);
        Assert.Equal(movedPath, library.ResolvePdf(moved));
        Assert.False(repeated.Added);
        Assert.Equal(id, repeated.Document.Id);
    }

    [Fact]
    public async Task AmbiguousCopiesDoNotInheritAnUnrelatedCatalogRecord()
    {
        var token = TestContext.Current.CancellationToken;
        using var temporary = new TemporaryLibrary();
        using var library = InboxLibrary.Open(temporary.Path);
        var original = await library.ImportAsync(Fixture, token);
        library.AddTag(original.Id, "Keep");
        var originalPath = library.ResolvePdf(original);
        File.Copy(originalPath, Path.Combine(library.PdfDirectory, "Copy 1.pdf"));
        File.Move(originalPath, Path.Combine(library.PdfDirectory, "Copy 2.pdf"));

        var refreshed = library.Refresh(token);
        var copies = library.Find(new DocumentFilter(Query: "NORTHSTAR")).Items;

        Assert.Equal(0, refreshed.Relocated);
        Assert.Equal(2, refreshed.Indexed);
        Assert.Equal(2, copies.Count);
        Assert.All(copies, copy =>
        {
            Assert.NotEqual(original.Id, copy.Id);
            Assert.Empty(copy.Tags);
        });
    }

    [Fact]
    public async Task RefreshPreparesAnOlderCatalogForFutureMoves()
    {
        var token = TestContext.Current.CancellationToken;
        using var temporary = new TemporaryLibrary();
        long id;
        string before;
        using (var library = InboxLibrary.Open(temporary.Path))
        {
            var saved = await library.ImportAsync(Fixture, token);
            id = saved.Id;
            before = library.ResolvePdf(saved);
            library.AddTag(id, "Property");
        }
        var connection = new SqliteConnectionStringBuilder
        {
            DataSource = Path.Combine(temporary.Path, ".plico", "catalog.sqlite3"),
            Pooling = false
        };
        using (var database = new SqliteConnection(connection.ToString()))
        {
            database.Open();
            using var command = database.CreateCommand();
            command.CommandText = """
                ALTER TABLE documents DROP COLUMN pdf_sha256;
                PRAGMA user_version = 3;
                """;
            command.ExecuteNonQuery();
        }

        using var upgraded = InboxLibrary.Open(temporary.Path);
        Assert.Equal(0, upgraded.Refresh(token).Indexed);
        File.Move(before, Path.Combine(upgraded.PdfDirectory, "Moved lease.pdf"));
        Assert.Equal(1, upgraded.Refresh(token).Relocated);
        Assert.Equal(id, Assert.Single(upgraded.Find(new DocumentFilter(Tag: "Property")).Items).Id);
    }
}
