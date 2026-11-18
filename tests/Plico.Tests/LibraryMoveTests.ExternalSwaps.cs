using Microsoft.Data.Sqlite;
using Plico.Core;
using Xunit;

namespace Plico.Tests;

public sealed partial class LibraryMoveTests
{
    private const string FirstSwapName = "Alpha.pdf";
    private const string SecondSwapName = "Beta.pdf";
    private static string ScannedReceiptFixture => Path.Combine(AppContext.BaseDirectory, "fixtures", "Scanned Receipt.pdf");

    [Fact]
    public void RefreshKeepsMetadataWithSwappedAndChainedPdfs()
    {
        var token = TestContext.Current.CancellationToken;
        using var temporary = new TemporaryLibrary();
        using var library = InboxLibrary.Open(temporary.Path);
        var (first, second) = IndexDistinctPdfs(library, token);
        var date = new DateOnly(2024, 2, 29);
        library.SetTitle(first.Id, "Annual lease");
        library.SetDocumentDate(first.Id, date);
        library.AddTag(first.Id, "Housing");
        library.SetReviewed(first.Id, true);
        library.AddTag(second.Id, "Workshop");
        string firstPath = library.ResolvePdf(first);
        string secondPath = library.ResolvePdf(second);
        ExchangeFiles(firstPath, secondPath, temporary.Path);

        var swap = library.Refresh(token);

        Assert.Equal(2, swap.Relocated);
        Assert.Equal(0, swap.Indexed);
        var lease = Assert.Single(library.Find(new DocumentFilter(Query: "Beta NORTHSTAR tag:housing"), token).Items);
        Assert.Equal(first.Id, lease.Id);
        Assert.Equal(SecondSwapName, lease.RelativePath);
        Assert.Equal("Annual lease", lease.Title);
        Assert.Equal(first.AddedAt, lease.AddedAt);
        Assert.Equal(date, lease.DocumentDate);
        Assert.True(lease.Reviewed);
        Assert.Equal(File.ReadAllBytes(Fixture), File.ReadAllBytes(library.ResolvePdf(lease)));
        var receipt = Assert.Single(library.Find(new DocumentFilter(Query: "Alpha text:missing tag:workshop"), token).Items);
        Assert.Equal(second.Id, receipt.Id);
        Assert.Equal(FirstSwapName, receipt.RelativePath);

        const string newPath = "Gamma.pdf";
        File.Move(secondPath, Path.Combine(library.PdfDirectory, newPath));
        File.Move(firstPath, secondPath);
        File.Copy(Path.Combine(AppContext.BaseDirectory, "fixtures", "Mixed Pages.pdf"), firstPath);
        var chain = library.Refresh(token);

        Assert.Equal(2, chain.Relocated);
        Assert.Equal(1, chain.Indexed);
        var after = library.Find(new DocumentFilter(), token).Items;
        Assert.Equal(newPath, Assert.Single(after, document => document.Id == first.Id).RelativePath);
        Assert.Equal(SecondSwapName, Assert.Single(after, document => document.Id == second.Id).RelativePath);
        var fresh = Assert.Single(after, document => document.RelativePath == FirstSwapName);
        Assert.DoesNotContain(fresh.Id, new[] { first.Id, second.Id });
        Assert.Empty(fresh.Tags);
        Assert.Null(fresh.DocumentDate);
        Assert.False(fresh.Reviewed);
        Assert.Equal(0, library.Refresh(token).Relocated);
    }

    [Fact]
    public void AFailedSwapUpdateRollsBackBothPathsAndCanBeRetried()
    {
        var token = TestContext.Current.CancellationToken;
        using var temporary = new TemporaryLibrary();
        using var library = InboxLibrary.Open(temporary.Path);
        var (first, second) = IndexDistinctPdfs(library, token);
        ExchangeFiles(library.ResolvePdf(first), library.ResolvePdf(second), temporary.Path);
        using var database = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = Path.Combine(temporary.Path, ".plico", "catalog.sqlite3"),
            Pooling = false
        }.ToString());
        database.Open();
        using var fault = database.CreateCommand();
        fault.CommandText = """
            CREATE TRIGGER fail_second_relocation BEFORE UPDATE OF relative_path ON documents
            WHEN NEW.relative_path IN ('Alpha.pdf', 'Beta.pdf') AND EXISTS(
                SELECT 1 FROM documents WHERE relative_path IN ('Alpha.pdf', 'Beta.pdf') AND id <> NEW.id
            )
            BEGIN SELECT RAISE(ABORT, 'test relocation failure'); END;
            """;
        fault.ExecuteNonQuery();

        var error = Assert.Throws<SqliteException>(() => library.Refresh(token));

        Assert.Contains("test relocation failure", error.Message, StringComparison.Ordinal);
        var unchanged = library.Find(new DocumentFilter(), token).Items;
        Assert.Equal(FirstSwapName, Assert.Single(unchanged, document => document.Id == first.Id).RelativePath);
        Assert.Equal(SecondSwapName, Assert.Single(unchanged, document => document.Id == second.Id).RelativePath);

        fault.CommandText = "DROP TRIGGER fail_second_relocation;";
        fault.ExecuteNonQuery();
        Assert.Equal(2, library.Refresh(token).Relocated);
        var recovered = library.Find(new DocumentFilter(), token).Items;
        Assert.Equal(SecondSwapName, Assert.Single(recovered, document => document.Id == first.Id).RelativePath);
        Assert.Equal(FirstSwapName, Assert.Single(recovered, document => document.Id == second.Id).RelativePath);
    }

    [Fact]
    public void AChainWithAnUnmatchedOccupantKeepsTheExistingReplacementBehavior()
    {
        const string occupiedName = "Gamma.pdf";
        var token = TestContext.Current.CancellationToken;
        using var temporary = new TemporaryLibrary();
        using var library = InboxLibrary.Open(temporary.Path);
        var (first, second) = IndexDistinctPdfs(library, token);
        string occupiedPath = Path.Combine(library.PdfDirectory, occupiedName);
        File.Copy(Path.Combine(AppContext.BaseDirectory, "fixtures", "Mixed Pages.pdf"), occupiedPath);
        Assert.Equal(1, library.Refresh(token).Indexed);
        var occupied = Assert.Single(library.Find(new DocumentFilter(), token).Items, document => document.RelativePath == occupiedName);

        File.Move(library.ResolvePdf(second), occupiedPath, overwrite: true);
        File.Move(library.ResolvePdf(first), Path.Combine(library.PdfDirectory, SecondSwapName));
        var refreshed = library.Refresh(token);

        Assert.Equal(0, refreshed.Relocated);
        Assert.Equal(2, refreshed.Indexed);
        var remaining = library.Find(new DocumentFilter(), token).Items;
        Assert.Equal(2, remaining.Count);
        Assert.DoesNotContain(remaining, document => document.Id == first.Id);
        Assert.Equal(SecondSwapName, Assert.Single(remaining, document => document.Id == second.Id).RelativePath);
        Assert.Equal(occupiedName, Assert.Single(remaining, document => document.Id == occupied.Id).RelativePath);
    }

    private static (DocumentSummary First, DocumentSummary Second) IndexDistinctPdfs(InboxLibrary library, CancellationToken token)
    {
        File.Copy(Fixture, Path.Combine(library.PdfDirectory, FirstSwapName));
        File.Copy(ScannedReceiptFixture, Path.Combine(library.PdfDirectory, SecondSwapName));
        Assert.Equal(2, library.Refresh(token).Indexed);
        var documents = library.Find(new DocumentFilter(), token).Items;
        return (Assert.Single(documents, document => document.RelativePath == FirstSwapName),
            Assert.Single(documents, document => document.RelativePath == SecondSwapName));
    }

    private static void ExchangeFiles(string first, string second, string temporaryRoot)
    {
        string holdingPath = Path.Combine(temporaryRoot, "held-for-exchange.pdf");
        File.Move(first, holdingPath);
        File.Move(second, first);
        File.Move(holdingPath, second);
    }
}
