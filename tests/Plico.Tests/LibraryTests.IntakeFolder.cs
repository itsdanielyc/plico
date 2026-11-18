using NAPS2.Images;
using NAPS2.Scan;
using Plico.Core;
using Xunit;

namespace Plico.Tests;

public sealed partial class LibraryTests
{
    [Fact]
    public async Task ImportIntoAFolderAvoidsLateFilenameCollisionsAndPreservesDuplicateDetection()
    {
        var token = TestContext.Current.CancellationToken;
        using var temporary = new TemporaryLibrary();
        string? occupiedPath = null;
        using var library = InboxLibrary.Open(temporary.Path, (source, destination, allowHardLink) =>
        {
            if (destination == occupiedPath) File.Copy(Fixture(Mixed), destination);
            return PdfFilePublication.TryClaim(source, destination, allowHardLink);
        });
        string filed = Directory.CreateDirectory(Path.Combine(library.PdfDirectory, "Filed", "Receipts")).FullName;
        string other = Directory.CreateDirectory(Path.Combine(library.PdfDirectory, "Other")).FullName;
        occupiedPath = Path.Combine(filed, Letter);

        var imported = await library.ImportFileAsync(Fixture(Letter), token, OcrLanguage.English, filed);
        var duplicate = await library.ImportFileAsync(Fixture(Letter), token, OcrLanguage.English, other);

        Assert.True(imported.Added);
        Assert.Equal("Filed/Receipts/Lease Renewal (2).pdf", imported.Document.RelativePath);
        Assert.Equal(File.ReadAllBytes(Fixture(Mixed)), File.ReadAllBytes(occupiedPath));
        Assert.Equal(File.ReadAllBytes(Fixture(Letter)), File.ReadAllBytes(library.ResolvePdf(imported.Document)));
        Assert.False(duplicate.Added);
        Assert.Equal(imported.Document.Id, duplicate.Document.Id);
        Assert.Empty(Directory.EnumerateFiles(other));
        Assert.Equal(imported.Document.Id,
            Assert.Single(library.Find(new DocumentFilter(Query: $"{LetterKeyword} folder:Filed"), token).Items).Id);
        await Assert.ThrowsAsync<ArgumentException>(
            () => library.ImportFileAsync(Fixture(Mixed), token, OcrLanguage.English, temporary.Path));
    }

    [Fact]
    public async Task ARecoveredScanUsesTheIntakeFolder()
    {
        var token = TestContext.Current.CancellationToken;
        using var temporary = new TemporaryLibrary();
        using var library = InboxLibrary.Open(temporary.Path);
        string target = Directory.CreateDirectory(Path.Combine(library.PdfDirectory, "Scan intake")).FullName;
        var outcome = await library.StoreScanWithRecoveryAsync("Feeder job",
            (context, cancellation) => CaptureUntilJamAsync(context, capturePages: true, cancellation),
            (_, _) => Task.FromResult(true), token, destinationFolder: target);

        var saved = Assert.IsType<DocumentSummary>(outcome.Document);
        Assert.Equal("Scan intake/Feeder job (partial).pdf", saved.RelativePath);
        Assert.Equal(2, saved.PageCount);
        Assert.True(saved.HasText);
        Assert.Equal(saved.Id, Assert.Single(library.Find(new DocumentFilter(Query: $"{ReceiptKeyword} folder:\"Scan intake\""), token).Items).Id);
        Assert.Empty(Directory.EnumerateFiles(library.PdfDirectory));
    }

    [Fact]
    public async Task ScanChecksItsIntakeFolderBeforeCaptureAndWhenPublishing()
    {
        var token = TestContext.Current.CancellationToken;
        using var temporary = new TemporaryLibrary();
        using var library = InboxLibrary.Open(temporary.Path);
        string destination = Path.Combine(library.PdfDirectory, "Scan intake");
        bool captureStarted = false;
        IAsyncEnumerable<ProcessedImage> Capture(ScanningContext context, CancellationToken cancellation)
        {
            captureStarted = true;
            return CaptureUntilJamAsync(context, capturePages: true, cancellation);
        }

        var missing = await Assert.ThrowsAsync<DirectoryNotFoundException>(() => library.StoreScanWithRecoveryAsync(
            "Missing folder", Capture, (_, _) => Task.FromResult(true), token, destinationFolder: destination));
        Assert.Contains("The intake folder is unavailable", missing.Message, StringComparison.Ordinal);
        Assert.False(captureStarted);

        Directory.CreateDirectory(destination);
        await Assert.ThrowsAsync<DirectoryNotFoundException>(() => library.StoreScanWithRecoveryAsync(
            "Displaced folder", Capture, (_, _) =>
            {
                Directory.Delete(destination);
                return Task.FromResult(true);
            }, token, destinationFolder: destination));
        Assert.True(captureStarted);
        AssertNoScanPublished(library, temporary.Path, token);
    }
}
