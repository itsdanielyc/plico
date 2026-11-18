using System.Security.Cryptography;
using Microsoft.Data.Sqlite;
using Plico.Core;
using Xunit;

namespace Plico.Tests;

public sealed partial class LibraryMoveTests
{
    private static string Fixture => Path.Combine(AppContext.BaseDirectory, "fixtures", "Lease Renewal.pdf");

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FilingWaitsForThePreviewReaderToReleaseThePdf(bool batchMove)
    {
        var token = TestContext.Current.CancellationToken;
        using var temporary = new TemporaryLibrary();
        using var library = InboxLibrary.Open(temporary.Path);
        var document = await library.ImportAsync(Fixture, token);
        var other = await library.ImportAsync(Path.Combine(AppContext.BaseDirectory, "fixtures", "Match Ledger.pdf"), token);
        string original = library.ResolvePdf(document);
        string folder = Directory.CreateDirectory(Path.Combine(library.PdfDirectory, "Filed")).FullName;
        string occupied = Path.Combine(folder, Path.GetFileName(original));
        byte[] occupiedBytes = File.ReadAllBytes(library.ResolvePdf(other));
        var opened = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var previewTask = library.RenderPageAsync(document, 1, async (path, _, cancellationToken) =>
        {
            using var reader = File.OpenRead(path);
            opened.SetResult();
            await release.Task.WaitAsync(cancellationToken);
            return new MemoryStream();
        }, token);
        await opened.Task.WaitAsync(token);
        Task move = batchMove
            ? library.MoveSelectedPdfsAsync([document.Id, other.Id], folder, cancellationToken: token)
            : library.MovePdfAsync(document.Id, folder, token);
        bool waited = !move.IsCompleted;
        try
        {
            File.WriteAllBytes(occupied, occupiedBytes);
        }
        finally
        {
            release.SetResult();
        }
        using var preview = await previewTask;
        var error = await Record.ExceptionAsync(() => move);

        Assert.True(waited, "Filing reached the open PDF before the preview released its reader.");
        Assert.Null(error);
        Assert.False(File.Exists(original));
        var moved = library.Find(new DocumentFilter(), token).Items.Single(item => item.Id == document.Id);
        Assert.Equal("Filed/Lease Renewal (2).pdf", moved.RelativePath);
        Assert.True(File.Exists(library.ResolvePdf(moved)));
        Assert.Equal(occupiedBytes, File.ReadAllBytes(occupied));
    }

    [Fact]
    public async Task MovePdfUsesAnAvailableNameAndKeepsTheCatalogRecord()
    {
        var token = TestContext.Current.CancellationToken;
        using var temporary = new TemporaryLibrary();
        using var outside = new TemporaryLibrary();
        using var library = InboxLibrary.Open(temporary.Path);
        var imported = await library.ImportFileAsync(Fixture, token);
        var document = imported.Document;
        string originalPath = library.ResolvePdf(document);
        byte[] bytes = File.ReadAllBytes(originalPath);
        library.AddTag(document.Id, "Housing");
        library.SetReviewed(document.Id, true);
        library.SetTitle(document.Id, "Final lease");

        string destination = Directory.CreateDirectory(Path.Combine(library.PdfDirectory, "Filed", "Property")).FullName;
        string occupied = Path.Combine(destination, "Lease Renewal.pdf");
        string reserved = Path.Combine(destination, "Lease Renewal (2).pdf");
        File.Copy(Fixture, occupied);
        File.Copy(Fixture, reserved);
        Assert.Equal(2, library.Refresh(token).Indexed);
        File.Delete(reserved);
        library.Refresh(token);

        await Assert.ThrowsAsync<ArgumentException>(() => library.MovePdfAsync(document.Id, outside.Path, token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            library.MovePdfAsync(document.Id, destination, new CancellationToken(canceled: true)));
        Assert.Equal(bytes, File.ReadAllBytes(originalPath));

        var moved = await library.MovePdfAsync(document.Id, destination, token);

        Assert.Equal(document.Id, moved.Id);
        Assert.Equal(document.AddedAt, moved.AddedAt);
        Assert.Equal("Final lease", moved.Title);
        Assert.True(moved.Reviewed);
        Assert.Equal("Housing", Assert.Single(moved.Tags));
        Assert.Equal("Filed/Property/Lease Renewal (3).pdf", moved.RelativePath);
        Assert.Equal(bytes, File.ReadAllBytes(library.ResolvePdf(moved)));
        Assert.Equal(bytes, File.ReadAllBytes(occupied));
        Assert.False(File.Exists(originalPath));
        Assert.False(File.Exists(reserved));
        Assert.Equal(document.Id, Assert.Single(library.Find(new DocumentFilter(Query: "\"lease renewal 3\" tag:housing"), token).Items).Id);
        Assert.Equal(moved.RelativePath, (await library.MovePdfAsync(document.Id, destination, token)).RelativePath);
        var duplicate = await library.ImportFileAsync(Fixture, token);
        Assert.False(duplicate.Added);
        Assert.Equal(document.Id, duplicate.Document.Id);
        Assert.Equal(0, library.Refresh(token).Relocated);
    }

    [Fact]
    public async Task ReopeningCompletesTheRecordedMoveEvenWhenIdenticalPdfsExist()
    {
        var token = TestContext.Current.CancellationToken;
        using var temporary = new TemporaryLibrary();
        string? occupiedFilename = null;
        string occupiedFixture = Path.Combine(AppContext.BaseDirectory, "fixtures", "Match Ledger.pdf");
        const string movedRelativePath = "Filed/Lease Renewal (2).pdf";
        long sourceId;
        using (var library = InboxLibrary.Open(temporary.Path, (source, destination, allowHardLink) =>
        {
            if (Path.GetFileName(destination) == occupiedFilename) File.Copy(occupiedFixture, destination);
            return PdfFilePublication.TryClaim(source, destination, allowHardLink);
        }))
        {
            var original = await library.ImportAsync(Fixture, token);
            sourceId = original.Id;
            File.Copy(library.ResolvePdf(original), Path.Combine(library.PdfDirectory, "Duplicate lease.pdf"));
            Assert.Equal(1, library.Refresh(token).Indexed);
            var twin = library.Find(new DocumentFilter(), token).Items.Single(item => item.Id != original.Id);
            library.AddTag(twin.Id, "Other copy");
            library.AddTag(original.Id, "Archive");
            library.SetReviewed(original.Id, true);
            string filing = Directory.CreateDirectory(Path.Combine(library.PdfDirectory, "Filed")).FullName;
            occupiedFilename = Path.GetFileName(original.RelativePath);
            using var database = new SqliteConnection(new SqliteConnectionStringBuilder
            {
                DataSource = Path.Combine(temporary.Path, ".plico", "catalog.sqlite3"),
                Pooling = false
            }.ToString());
            database.Open();
            using var command = database.CreateCommand();
            command.CommandText = """
                CREATE TRIGGER fail_test_move BEFORE UPDATE OF relative_path ON documents
                BEGIN SELECT RAISE(ABORT, 'Interrupted catalog update'); END;
                """;
            command.ExecuteNonQuery();
            var error = await Assert.ThrowsAsync<SqliteException>(() => library.MovePdfAsync(original.Id, filing, token));
            Assert.Contains("Interrupted catalog update", error.Message, StringComparison.Ordinal);
            Assert.Equal(File.ReadAllBytes(occupiedFixture), File.ReadAllBytes(Path.Combine(filing, occupiedFilename)));
            Assert.Equal(File.ReadAllBytes(Fixture), File.ReadAllBytes(Path.Combine(library.PdfDirectory, movedRelativePath)));
            Assert.False(File.Exists(library.ResolvePdf(original)));
            command.CommandText = "DROP TRIGGER fail_test_move;";
            command.ExecuteNonQuery();
        }

        using var recovered = InboxLibrary.Open(temporary.Path);
        var filed = Assert.Single(recovered.Find(new DocumentFilter(Query: "norths tag:Archive"), token).Items);
        Assert.Equal(sourceId, filed.Id);
        Assert.True(filed.Reviewed);
        Assert.Equal(movedRelativePath, filed.RelativePath);
        Assert.Equal("Finished the previous PDF move.", recovered.MoveRecoveryNotice);
        Assert.Equal("Duplicate lease.pdf", Assert.Single(recovered.Find(new DocumentFilter(Tag: "Other copy"), token).Items).RelativePath);
        var reopenedCatalog = new LibraryCatalog(Path.Combine(temporary.Path, ".plico", "catalog.sqlite3"));
        Assert.Null(reopenedCatalog.ReadPendingPdfMove());
    }

    [Fact]
    public async Task MoveRecoveryKeepsUnrelatedDestinationBytesSeparate()
    {
        var token = TestContext.Current.CancellationToken;
        using var temporary = new TemporaryLibrary();
        long originalId;
        using (var library = InboxLibrary.Open(temporary.Path))
        {
            var original = await library.ImportAsync(Fixture, token);
            originalId = original.Id;
            library.AddTag(originalId, "Archive");
            string path = library.ResolvePdf(original);
            var catalog = new LibraryCatalog(Path.Combine(temporary.Path, ".plico", "catalog.sqlite3"));
            catalog.RecordPdfMove(new PdfMoveIntent(original.Id, original.RelativePath, "Expected.pdf", SHA256.HashData(File.ReadAllBytes(path))));
            File.Move(path, Path.Combine(library.PdfDirectory, "Actually moved.pdf"));
            File.Copy(Path.Combine(AppContext.BaseDirectory, "fixtures", "Match Ledger.pdf"), Path.Combine(library.PdfDirectory, "Expected.pdf"));
        }

        using var reopened = InboxLibrary.Open(temporary.Path);
        Assert.Contains("could not match", reopened.MoveRecoveryNotice, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(1, reopened.Refresh(token).Relocated);
        var source = Assert.Single(reopened.Find(new DocumentFilter(Tag: "Archive"), token).Items);
        Assert.Equal(originalId, source.Id);
        Assert.Equal("Actually moved.pdf", source.RelativePath);
        var unrelated = reopened.Find(new DocumentFilter(), token).Items.Single(item => item.RelativePath == "Expected.pdf");
        Assert.Empty(unrelated.Tags);
    }

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
        var moved = Assert.Single(library.Find(new DocumentFilter(Query: "Renewal55", Tag: "housing"), TestContext.Current.CancellationToken).Items);
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
        var copies = library.Find(new DocumentFilter(Query: "NORTHSTAR"), TestContext.Current.CancellationToken).Items;

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
                DROP INDEX documents_text_presence;
                DROP INDEX documents_document_day;
                ALTER TABLE documents DROP COLUMN document_day;
                DROP TABLE saved_searches;
                DROP TABLE pending_pdf_move;
                DROP INDEX documents_added_at;
                DROP INDEX documents_ocr_copies;
                ALTER TABLE documents DROP COLUMN ocr_copy_language;
                ALTER TABLE documents DROP COLUMN ocr_copy_source_id;
                ALTER TABLE documents DROP COLUMN pdf_sha256;
                PRAGMA user_version = 3;
                """;
            command.ExecuteNonQuery();
        }

        using var upgraded = InboxLibrary.Open(temporary.Path);
        Assert.Equal(0, upgraded.Refresh(token).Indexed);
        File.Move(before, Path.Combine(upgraded.PdfDirectory, "Moved lease.pdf"));
        Assert.Equal(1, upgraded.Refresh(token).Relocated);
        Assert.Equal(id, Assert.Single(upgraded.Find(new DocumentFilter(Tag: "Property"), TestContext.Current.CancellationToken).Items).Id);
    }
}
