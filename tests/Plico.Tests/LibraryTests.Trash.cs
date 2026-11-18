using Plico.Core;
using Xunit;

namespace Plico.Tests;

public sealed partial class LibraryTests
{
    [Fact]
    public async Task TrashIsHiddenFromSearchAndKeepsItsMetadataInAPortableBackup()
    {
        var token = TestContext.Current.CancellationToken;
        using var temporary = new TemporaryLibrary();
        using var backups = new TemporaryLibrary();
        using var library = InboxLibrary.Open(temporary.Path);
        var original = await library.ImportAsync(Fixture(Letter), token);
        library.AddTag(original.Id, "Property");
        var date = new DateOnly(2026, 10, 1);
        library.SetDocumentDate(original.Id, date);
        library.SetReviewed(original.Id, true);
        string originalPath = library.ResolvePdf(original);
        byte[] bytes = File.ReadAllBytes(originalPath);

        await library.TrashPdfAsync(original.Id, token);

        Assert.False(File.Exists(originalPath));
        Assert.Empty(library.Find(new DocumentFilter(Query: LetterKeyword), token).Items);
        Assert.Empty(library.Refresh(token).UnavailablePaths);
        Assert.Equal(0, library.CountUnavailableDocuments());
        Assert.Equal(original.RelativePath, Assert.Single(library.GetTrash()).OriginalPath);

        var backup = await library.BackupAsync(backups.Path, cancellationToken: token);
        Assert.Equal(1, backup.PdfCount);
        using var reopened = InboxLibrary.Open(backup.Location);
        long restoredId = Assert.Single(reopened.GetTrash()).DocumentId;
        var restored = await reopened.RestoreTrashedPdfAsync(restoredId, token);
        Assert.Equal(original.Id, restored.Id);
        Assert.Equal(date, restored.DocumentDate);
        Assert.Equal("Property", Assert.Single(restored.Tags));
        Assert.True(restored.Reviewed);
        Assert.Equal(bytes, File.ReadAllBytes(reopened.ResolvePdf(restored)));
    }

    [Fact]
    public async Task ReopeningCompletesATrashMoveAndRestoreKeepsAReplacementSafe()
    {
        var token = TestContext.Current.CancellationToken;
        using var temporary = new TemporaryLibrary();
        long documentId;
        string path;
        using (var interrupted = InboxLibrary.Open(temporary.Path, (source, destination, allowHardLink) =>
               {
                   bool claimed = PdfFilePublication.TryClaim(source, destination, allowHardLink);
                   if (claimed && Path.GetFileName(Path.GetDirectoryName(destination)) == "trash")
                       throw new IOException("Interrupted after moving to Trash.");
                   return claimed;
               }))
        {
            var document = await interrupted.ImportAsync(Fixture(Letter), token);
            documentId = document.Id;
            path = interrupted.ResolvePdf(document);
            await Assert.ThrowsAsync<IOException>(() => interrupted.TrashPdfAsync(document.Id, token));
        }

        byte[] replacement = File.ReadAllBytes(Fixture("Match Ledger.pdf"));
        File.WriteAllBytes(path, replacement);
        using var library = InboxLibrary.Open(temporary.Path);
        Assert.Equal(documentId, Assert.Single(library.GetTrash()).DocumentId);
        library.Refresh(token);
        await Assert.ThrowsAsync<IOException>(() => library.RestoreTrashedPdfAsync(documentId, token));
        Assert.Equal(replacement, File.ReadAllBytes(path));
        Assert.Equal(documentId, Assert.Single(library.GetTrash()).DocumentId);

        var newest = await library.ImportAsync(Fixture(Letter), token);
        await library.TrashPdfAsync(newest.Id, token);
        await library.PurgeTrashedPdfAsync(newest.Id, token);
        Assert.Equal(documentId, Assert.Single(library.GetTrash()).DocumentId);
        var reimport = await library.ImportAsync(Fixture(Letter), token);
        Assert.True(reimport.Id > newest.Id);
    }
}
