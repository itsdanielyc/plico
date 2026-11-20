using Microsoft.Data.Sqlite;
using Plico.Core;
using Xunit;

namespace Plico.Tests;

/// <summary>
/// Catalog writes while another connection reads. The desktop reads suggestions and search results in the
/// background, so a write must wait for a reader instead of failing with "database is locked".
/// </summary>
public sealed class LibraryCatalogConcurrencyTests
{
    private static readonly TimeSpan ReaderHold = TimeSpan.FromMilliseconds(400);

    [Fact]
    public async Task IndexingADocumentWaitsForAReaderOnAnotherConnection()
    {
        using var temporary = new TemporaryLibrary();
        string path = Path.Combine(temporary.Path, "catalog.sqlite3");
        var catalog = new LibraryCatalog(path);
        Index(catalog, "Earlier letter");

        Task<long> write;
        using (var reader = HoldReadLock(path))
        {
            write = Task.Run(() => Index(catalog, "Lease renewal"), TestContext.Current.CancellationToken);
            await Task.Delay(ReaderHold, TestContext.Current.CancellationToken);
            Assert.False(write.IsCompleted, write.Exception?.GetBaseException().Message ?? "The write did not wait for the reader.");
        }

        long indexed = await write;
        var found = Assert.Single(catalog.Find(new DocumentFilter(Query: "\"Lease renewal\""), TestContext.Current.CancellationToken).Items);
        Assert.Equal(indexed, found.Id);
    }

    [Fact]
    public async Task SavingATagRuleWaitsForAReaderOnAnotherConnection()
    {
        using var temporary = new TemporaryLibrary();
        string path = Path.Combine(temporary.Path, "catalog.sqlite3");
        var catalog = new LibraryCatalog(path);

        Task<FilingRule> write;
        using (var reader = HoldReadLock(path))
        {
            write = Task.Run(() => catalog.SaveFilingRule(new FilingRule(null, "Housing", "deposit", FilingRuleMatch.AnyWord)),
                TestContext.Current.CancellationToken);
            await Task.Delay(ReaderHold, TestContext.Current.CancellationToken);
            Assert.False(write.IsCompleted, write.Exception?.GetBaseException().Message ?? "The write did not wait for the reader.");
        }

        var saved = await write;
        Assert.Equal(saved, Assert.Single(catalog.ReadFilingRules()));
    }

    private static long Index(LibraryCatalog catalog, string title) =>
        catalog.Upsert(title + ".pdf", title, new PdfText(title, 1, true), new FileVersion(1, 1024, true), new byte[32]);

    /// <summary>Opens a second connection and keeps its shared lock until the returned transaction is disposed.</summary>
    private static ReadLock HoldReadLock(string path)
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Pooling = false }.ToString());
        connection.Open();
        var transaction = connection.BeginTransaction(deferred: true);
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT COUNT(*) FROM documents;";
        command.ExecuteScalar();
        return new ReadLock(connection, transaction);
    }

    private sealed class ReadLock(SqliteConnection connection, SqliteTransaction transaction) : IDisposable
    {
        public void Dispose()
        {
            transaction.Dispose();
            connection.Dispose();
        }
    }
}
