using Microsoft.Data.Sqlite;
using Plico.Core;
using Xunit;

namespace Plico.Tests;

public sealed partial class LibraryTests
{
    [Fact]
    public async Task RestoreRecreatesTheOriginalFolderAndKeepsCatalogMetadata()
    {
        var token = TestContext.Current.CancellationToken;
        using var temporary = new TemporaryLibrary();
        using var library = InboxLibrary.Open(temporary.Path);
        string filed = Directory.CreateDirectory(Path.Combine(library.PdfDirectory, "Filed", "Property")).FullName;
        string destination = Path.Combine(filed, Letter);
        File.Copy(Fixture(Letter), destination);
        Assert.Equal(1, library.Refresh(token).Indexed);
        var original = Assert.Single(library.Find(new DocumentFilter(), token).Items);
        var date = new DateOnly(2024, 2, 29);
        library.SetTitle(original.Id, "Original lease");
        library.SetDocumentDate(original.Id, date);
        library.SetReviewed(original.Id, true);
        library.AddTag(original.Id, "Property");
        string backup = Path.Combine(temporary.Path, "Named backup.pdf");
        File.Move(destination, backup);
        Directory.Delete(filed);
        Directory.Delete(Path.GetDirectoryName(filed)!);
        Assert.Equal(original.RelativePath, Assert.Single(library.Refresh(token).UnavailablePaths));

        var restored = await library.RestoreUnavailablePdfAsync(original.RelativePath, backup, token);

        Assert.Equal(original.Id, restored.Id);
        Assert.Equal(original.RelativePath, restored.RelativePath);
        Assert.Equal(original.AddedAt, restored.AddedAt);
        Assert.Equal(date, restored.DocumentDate);
        Assert.Equal("Original lease", restored.Title);
        Assert.True(restored.Reviewed);
        Assert.Contains("Property", restored.Tags);
        Assert.Equal(File.ReadAllBytes(backup), File.ReadAllBytes(destination));
        Assert.Empty(library.ReadUnavailablePaths(token));
        Assert.Equal(original.Id, Assert.Single(library.Find(new DocumentFilter(Query: $"{LetterKeyword} tag:property"), token).Items).Id);
    }

    [Fact]
    public async Task RestoreRejectsChangedBytesCancellationAndOccupiedPaths()
    {
        var token = TestContext.Current.CancellationToken;
        using var temporary = new TemporaryLibrary();
        using var library = InboxLibrary.Open(temporary.Path);
        var original = await library.ImportAsync(Fixture(Letter), token);
        string destination = library.ResolvePdf(original);
        File.Delete(destination);
        library.Refresh(token);
        string changed = Path.Combine(temporary.Path, "Changed copy.pdf");
        byte[] bytes = File.ReadAllBytes(Fixture(Letter));
        bytes[^1] ^= 1;
        File.WriteAllBytes(changed, bytes);

        var mismatch = await Assert.ThrowsAsync<InvalidDataException>(
            () => library.RestoreUnavailablePdfAsync(original.RelativePath, changed, token));
        Assert.Contains("does not match", mismatch.Message, StringComparison.Ordinal);
        Assert.False(File.Exists(destination));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => library.RestoreUnavailablePdfAsync(original.RelativePath, Fixture(Letter), new CancellationToken(canceled: true)));
        Assert.False(File.Exists(destination));

        const string occupant = "This file already occupies the original path.";
        File.WriteAllText(destination, occupant);
        var occupied = await Assert.ThrowsAsync<IOException>(
            () => library.RestoreUnavailablePdfAsync(original.RelativePath, Fixture(Letter), token));
        Assert.Contains("already occupies", occupied.Message, StringComparison.Ordinal);
        Assert.Equal(occupant, File.ReadAllText(destination));
        Assert.Equal(original.RelativePath, Assert.Single(library.ReadUnavailablePaths(token)));
    }

    [Fact]
    public async Task RestoreKeepsAPdfThatArrivesAtTheOriginalPathDuringPublication()
    {
        var token = TestContext.Current.CancellationToken;
        using var temporary = new TemporaryLibrary();
        bool createCollision = false;
        using var library = InboxLibrary.Open(temporary.Path, (source, destination, allowHardLink) =>
        {
            if (createCollision)
            {
                File.Copy(Fixture(Mixed), destination);
                createCollision = false;
            }
            return PdfFilePublication.TryClaim(source, destination, allowHardLink);
        });
        var original = await library.ImportAsync(Fixture(Letter), token);
        string restorePath = library.ResolvePdf(original);
        File.Delete(restorePath);
        library.Refresh(token);
        createCollision = true;

        var error = await Assert.ThrowsAsync<IOException>(() => library.RestoreUnavailablePdfAsync(original.RelativePath, Fixture(Letter), token));

        Assert.Contains("already occupies", error.Message, StringComparison.Ordinal);
        Assert.Equal(File.ReadAllBytes(Fixture(Mixed)), File.ReadAllBytes(restorePath));
        Assert.Equal(original.RelativePath, Assert.Single(library.ReadUnavailablePaths(token)));

        File.Delete(restorePath);
        var restored = await library.RestoreUnavailablePdfAsync(original.RelativePath, Fixture(Letter), token);
        Assert.Equal(original.Id, restored.Id);
        Assert.Equal(File.ReadAllBytes(Fixture(Letter)), File.ReadAllBytes(restorePath));
    }

    [Fact]
    public async Task RestorePreservesTheVerifiedPdfWhenTheCatalogUpdateFails()
    {
        var token = TestContext.Current.CancellationToken;
        using var temporary = new TemporaryLibrary();
        using var library = InboxLibrary.Open(temporary.Path);
        var original = await library.ImportAsync(Fixture(Letter), token);
        library.AddTag(original.Id, "Housing");
        string destination = library.ResolvePdf(original);
        File.Delete(destination);
        library.Refresh(token);
        using var database = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = Path.Combine(temporary.Path, ".plico", "catalog.sqlite3"),
            Pooling = false
        }.ToString());
        database.Open();
        using var trigger = database.CreateCommand();
        trigger.CommandText = """
            CREATE TRIGGER fail_restore BEFORE UPDATE OF is_present ON documents
            WHEN OLD.is_present = 0 AND NEW.is_present = 1
            BEGIN SELECT RAISE(ABORT, 'test restore catalog failure'); END;
            """;
        trigger.ExecuteNonQuery();

        var failure = await Assert.ThrowsAsync<SqliteException>(
            () => library.RestoreUnavailablePdfAsync(original.RelativePath, Fixture(Letter), token));

        Assert.Contains("test restore catalog failure", failure.Message, StringComparison.Ordinal);
        Assert.Equal(File.ReadAllBytes(Fixture(Letter)), File.ReadAllBytes(destination));
        Assert.Equal(original.RelativePath, Assert.Single(library.ReadUnavailablePaths(token)));
        trigger.CommandText = "DROP TRIGGER fail_restore;";
        trigger.ExecuteNonQuery();
        library.Refresh(token);
        Assert.Empty(library.ReadUnavailablePaths(token));
        Assert.Equal(original.Id, Assert.Single(library.Find(new DocumentFilter(Tag: "Housing"), token).Items).Id);
    }

    [Fact]
    public async Task RestoreRejectsALinkedDestinationFolder()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "This fixture uses a Unix directory symlink.");
        var token = TestContext.Current.CancellationToken;
        using var temporary = new TemporaryLibrary();
        using var library = InboxLibrary.Open(temporary.Path);
        string folder = Directory.CreateDirectory(Path.Combine(library.PdfDirectory, "Filed")).FullName;
        string destination = Path.Combine(folder, Letter);
        File.Copy(Fixture(Letter), destination);
        library.Refresh(token);
        var original = Assert.Single(library.Find(new DocumentFilter(), token).Items);
        File.Delete(destination);
        Directory.Delete(folder);
        library.Refresh(token);
        string elsewhere = Directory.CreateDirectory(Path.Combine(temporary.Path, "Elsewhere")).FullName;
        Directory.CreateSymbolicLink(folder, elsewhere);
        try
        {
            var error = await Assert.ThrowsAsync<IOException>(
                () => library.RestoreUnavailablePdfAsync(original.RelativePath, Fixture(Letter), token));
            Assert.Contains("ordinary folder", error.Message, StringComparison.Ordinal);
            Assert.Empty(Directory.EnumerateFileSystemEntries(elsewhere));
            Assert.Equal(original.RelativePath, Assert.Single(library.ReadUnavailablePaths(token)));
        }
        finally
        {
            Directory.Delete(folder);
        }
    }
}
