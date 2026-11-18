using Plico.Core;
using Xunit;

namespace Plico.Tests;

public sealed partial class LibraryTests
{
    [Fact]
    public async Task PdfVersionsKeepIdentityAndReindexReorderedAndDeletedPages()
    {
        using var temporary = new TemporaryLibrary();
        using var library = InboxLibrary.Open(temporary.Path);
        var token = TestContext.Current.CancellationToken;
        var original = await library.ImportAsync(Fixture(Mixed), token);
        string path = library.ResolvePdf(original);
        byte[] originalBytes = File.ReadAllBytes(path);
        var date = new DateOnly(2024, 2, 29);
        library.SetDocumentDate(original.Id, date);
        library.AddTag(original.Id, "household");
        library.SetReviewed(original.Id, reviewed: true);
        library.SetTitle(original.Id, "House records");

        var reordered = await library.ReorderPdfPagesAsync(original.Id, [2, 1], token);
        Assert.Equal(path, library.ResolvePdf(reordered));
        Assert.Equal(original.Id, reordered.Id);
        Assert.Equal("House records", reordered.Title);
        Assert.Equal(date, reordered.DocumentDate);
        Assert.Contains("household", reordered.Tags);
        Assert.True(reordered.Reviewed);
        Assert.Equal(1, library.FindFirstMatchingPage(reordered, ReceiptKeyword, token));
        var beforeReorder = Assert.Single(library.GetPdfVersions(original.Id));
        Assert.Equal(PdfRevisionKind.Reorder, beforeReorder.Change);

        var deleted = await library.DeletePdfPagesAsync(original.Id, PdfPageSelection.Parse("1", 2), token);
        Assert.Equal(1, deleted.PageCount);
        Assert.Empty(library.Find(new DocumentFilter(Query: ReceiptKeyword), token).Items);
        await Assert.ThrowsAsync<ArgumentException>(() =>
            library.DeletePdfPagesAsync(original.Id, PdfPageSelection.Parse("1", 1), token));

        var restored = await library.RestorePdfVersionAsync(original.Id, beforeReorder.Key, token);
        Assert.Equal(2, restored.PageCount);
        Assert.Equal(originalBytes, File.ReadAllBytes(path));
        Assert.Equal(2, library.FindFirstMatchingPage(restored, ReceiptKeyword, token));
        Assert.Equal(3, library.GetPdfVersions(original.Id).Count);
    }

    [Fact]
    public async Task InterruptedPdfVersionRollsBackWithoutReassigningTheActivePath()
    {
        using var temporary = new TemporaryLibrary();
        var token = TestContext.Current.CancellationToken;
        string? activePath = null;
        long documentId;
        byte[] originalBytes;
        using (var library = InboxLibrary.Open(temporary.Path, (source, destination, allowHardLink) =>
        {
            if (IsActiveVersionClaim(destination, activePath, allowHardLink)) throw new InterruptedVersionTestException();
            return PdfFilePublication.TryClaim(source, destination, allowHardLink);
        }))
        {
            var original = await library.ImportAsync(Fixture(Mixed), token);
            documentId = original.Id;
            activePath = library.ResolvePdf(original);
            originalBytes = File.ReadAllBytes(activePath);
            await Assert.ThrowsAsync<InterruptedVersionTestException>(() =>
                library.DeletePdfPagesAsync(documentId, PdfPageSelection.Parse("1", 2), token));
            Assert.False(File.Exists(activePath));
        }

        using var reopened = InboxLibrary.Open(temporary.Path);
        Assert.Empty(reopened.GetPdfVersions(documentId));
        var restored = Assert.Single(reopened.Find(new DocumentFilter(), token).Items);
        Assert.Equal(documentId, restored.Id);
        Assert.Equal(originalBytes, File.ReadAllBytes(activePath));
        Assert.Equal(1, reopened.FindFirstMatchingPage(restored, LetterKeyword, token));
    }

    [Fact]
    public async Task PdfVersionPreservesAnExternalFileThatClaimsTheActivePath()
    {
        using var temporary = new TemporaryLibrary();
        var token = TestContext.Current.CancellationToken;
        byte[] competing = [17, 42, 91];
        string? activePath = null;
        byte[] originalBytes;
        long documentId;
        using (var library = InboxLibrary.Open(temporary.Path, (source, destination, allowHardLink) =>
        {
            if (IsActiveVersionClaim(destination, activePath, allowHardLink))
            {
                File.WriteAllBytes(destination, competing);
                return false;
            }
            return PdfFilePublication.TryClaim(source, destination, allowHardLink);
        }))
        {
            var original = await library.ImportAsync(Fixture(Mixed), token);
            documentId = original.Id;
            activePath = library.ResolvePdf(original);
            originalBytes = File.ReadAllBytes(activePath);
            await Assert.ThrowsAsync<IOException>(() =>
                library.DeletePdfPagesAsync(documentId, PdfPageSelection.Parse("1", 2), token));
            Assert.Equal(competing, File.ReadAllBytes(activePath));
        }
        Assert.Throws<IOException>(() => InboxLibrary.Open(temporary.Path));
        string movedAside = Path.Combine(temporary.Path, "External file.pdf");
        File.Move(activePath, movedAside);
        using var recovered = InboxLibrary.Open(temporary.Path);
        Assert.Empty(recovered.GetPdfVersions(documentId));
        Assert.Equal(originalBytes, File.ReadAllBytes(activePath));
        Assert.Equal(competing, File.ReadAllBytes(movedAside));
    }

    [Fact]
    public async Task PdfVersionCommitsTheInstalledPdfWhenTheProcessStoppedBeforeReindexing()
    {
        using var temporary = new TemporaryLibrary();
        var token = TestContext.Current.CancellationToken;
        string? active = null;
        long documentId;
        using (var library = InboxLibrary.Open(temporary.Path, (source, destination, allowHardLink) =>
        {
            bool claimed = PdfFilePublication.TryClaim(source, destination, allowHardLink);
            if (claimed && IsActiveVersionClaim(destination, active, allowHardLink)) throw new InterruptedVersionTestException();
            return claimed;
        }))
        {
            var original = await library.ImportAsync(Fixture(Mixed), token);
            documentId = original.Id;
            active = library.ResolvePdf(original);
            await Assert.ThrowsAsync<InterruptedVersionTestException>(() =>
                library.DeletePdfPagesAsync(documentId, PdfPageSelection.Parse("1", 2), token));
            Assert.True(File.Exists(active));
        }

        using var reopened = InboxLibrary.Open(temporary.Path);
        var current = Assert.Single(reopened.Find(new DocumentFilter(Query: ReceiptKeyword), token).Items);
        Assert.Equal(documentId, current.Id);
        Assert.Equal(1, current.PageCount);
        Assert.Empty(reopened.Find(new DocumentFilter(Query: LetterKeyword), token).Items);
        Assert.Equal(2, Assert.Single(reopened.GetPdfVersions(documentId)).PageCount);
    }

    [Fact]
    public async Task PdfVersionBackupAndTrashKeepHistoryUntilPurge()
    {
        using var temporary = new TemporaryLibrary();
        using var library = InboxLibrary.Open(Directory.CreateDirectory(Path.Combine(temporary.Path, "Library")).FullName);
        string target = Directory.CreateDirectory(Path.Combine(temporary.Path, "Backups")).FullName;
        var token = TestContext.Current.CancellationToken;
        var entry = await library.ImportAsync(Fixture(Mixed), token);
        var parts = await library.SplitPdfAsync(entry.Id, [2], token);
        Assert.Equal(2, parts.Count);
        Assert.Equal(1, library.FindFirstMatchingPage(parts[0], LetterKeyword, token));
        Assert.Equal(1, library.FindFirstMatchingPage(parts[1], ReceiptKeyword, token));
        await library.RotatePdfVersionAsync(entry.Id, PdfPageSelection.Parse("1", 2), PdfPageRotation.Clockwise, token);
        var earlier = Assert.Single(library.GetPdfVersions(entry.Id));
        await library.TrashPdfAsync(entry.Id, token);

        var backup = await library.BackupAsync(target, cancellationToken: token);
        using (var copy = InboxLibrary.Open(backup.Location))
        {
            Assert.Equal(earlier, Assert.Single(copy.GetPdfVersions(entry.Id)));
            await copy.RestoreTrashedPdfAsync(entry.Id, token);
            string exported = await copy.ExportPdfVersionAsync(entry.Id, earlier.Key, target, token);
            using var archivedPdf = UglyToad.PdfPig.PdfDocument.Open(exported);
            Assert.Equal(2, archivedPdf.NumberOfPages);
        }
        await library.PurgeTrashedPdfAsync(entry.Id, token);
        Assert.Empty(library.GetPdfVersions(entry.Id));
        Assert.Empty(Directory.EnumerateFiles(Path.Combine(library.RootPath, ".plico", "versions"), "*.pdf", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task PdfVersionUnlockKeepsTheEncryptedOriginalAndRejectsTheWrongPassword()
    {
        const string password = "synthetic buyer password";
        using var temporary = new TemporaryLibrary();
        using var library = InboxLibrary.Open(Directory.CreateDirectory(Path.Combine(temporary.Path, "Library")).FullName);
        string export = Directory.CreateDirectory(Path.Combine(temporary.Path, "Export")).FullName;
        string locked = Path.Combine(temporary.Path, "Protected lease.pdf");
        using (var document = PdfSharp.Pdf.IO.PdfReader.Open(Fixture(Letter), PdfSharp.Pdf.IO.PdfDocumentOpenMode.Modify))
        {
            document.Version = 20;
            document.SecurityHandler.SetEncryptionToV5();
            document.SecuritySettings.UserPassword = password;
            document.SecuritySettings.OwnerPassword = "synthetic owner password";
            document.Save(locked);
        }
        byte[] expectedOriginal = File.ReadAllBytes(locked);
        var token = TestContext.Current.CancellationToken;
        await Assert.ThrowsAsync<InvalidDataException>(() =>
            library.UnlockAndImportPdfAsync(locked, "wrong password", cancellationToken: token));
        Assert.Empty(library.Find(new DocumentFilter(), token).Items);

        var unlocked = await library.UnlockAndImportPdfAsync(locked, password, cancellationToken: token);
        Assert.True(unlocked.Document.HasText);
        Assert.Equal(1, library.FindFirstMatchingPage(unlocked.Document, LetterKeyword, token));
        var original = Assert.Single(library.GetPdfVersions(unlocked.Document.Id));
        Assert.True(original.IsEncrypted);
        string archived = await library.ExportPdfVersionAsync(unlocked.Document.Id, original.Key, export, token);
        Assert.Equal(expectedOriginal, File.ReadAllBytes(archived));
        Assert.Equal(expectedOriginal, File.ReadAllBytes(locked));
        Assert.False((await library.UnlockAndImportPdfAsync(locked, password, cancellationToken: token)).Added);
        Assert.Single(library.GetPdfVersions(unlocked.Document.Id));
    }

    private static bool IsActiveVersionClaim(string destination, string? active, bool publishing) =>
        // A fixture's temporary directory can be addressed through /var or /private/var on macOS.
        publishing && active is not null && Path.GetFileName(destination) == Path.GetFileName(active);

    private sealed class InterruptedVersionTestException : Exception;
}
