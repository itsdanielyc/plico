using Plico.Core;
using Xunit;

namespace Plico.Tests;

public sealed partial class LibraryTests
{
    [Fact]
    public async Task IndependentSingleExportsReserveDistinctFilenames()
    {
        const string name = "Receipt.pdf";
        var token = TestContext.Current.CancellationToken;
        using var firstRoot = new TemporaryLibrary();
        using var secondRoot = new TemporaryLibrary();
        using var destination = new TemporaryLibrary();
        using var first = InboxLibrary.Open(firstRoot.Path);
        using var second = InboxLibrary.Open(secondRoot.Path);
        File.Copy(Fixture(Letter), Path.Combine(first.PdfDirectory, name));
        File.Copy(Fixture(Mixed), Path.Combine(second.PdfDirectory, name));
        first.Refresh(token);
        second.Refresh(token);
        long firstId = Assert.Single(first.Find(new DocumentFilter(), token).Items).Id;
        long secondId = Assert.Single(second.Find(new DocumentFilter(), token).Items).Id;
        string occupied = Path.Combine(destination.Path, name);
        File.WriteAllText(occupied, "Existing file");
        string directoryConflict = Directory.CreateDirectory(Path.Combine(destination.Path, "Receipt (2).pdf")).FullName;

        string[] exports = await Task.WhenAll(
            first.ExportPdfAsync(firstId, destination.Path, token),
            second.ExportPdfAsync(secondId, destination.Path, token));

        Assert.Equal(["Receipt (3).pdf", "Receipt (4).pdf"], exports.Select(Path.GetFileName).Order(StringComparer.Ordinal));
        Assert.Equal(File.ReadAllBytes(Fixture(Letter)), File.ReadAllBytes(exports[0]));
        Assert.Equal(File.ReadAllBytes(Fixture(Mixed)), File.ReadAllBytes(exports[1]));
        Assert.Equal("Existing file", File.ReadAllText(occupied));
        Assert.Equal(Path.GetFileName(directoryConflict), Path.GetFileName(Assert.Single(Directory.EnumerateDirectories(destination.Path))));
    }

    [Fact]
    public async Task CancelledOrChangedSingleExportLeavesNoCopy()
    {
        var token = TestContext.Current.CancellationToken;
        using var source = new TemporaryLibrary();
        using var destination = new TemporaryLibrary();
        using var library = InboxLibrary.Open(source.Path);
        string original = Path.Combine(library.PdfDirectory, "Lease.pdf");
        File.Copy(Fixture(Letter), original);
        library.Refresh(token);
        var document = Assert.Single(library.Find(new DocumentFilter(), token).Items);

        await Assert.ThrowsAsync<ArgumentException>(() => library.ExportPdfAsync(document.Id, library.PdfDirectory, token));
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => library.ExportPdfAsync(document.Id, destination.Path, cancelled.Token));
        Assert.Empty(Directory.EnumerateFileSystemEntries(destination.Path));

        File.AppendAllText(original, "\n");
        await Assert.ThrowsAsync<IOException>(() => library.ExportPdfAsync(document.Id, destination.Path, token));
        Assert.Empty(Directory.EnumerateFileSystemEntries(destination.Path));
    }

    [Fact]
    public async Task ExportCopiesOnlySelectedPdfsAndGivesMatchingNamesDistinctPaths()
    {
        var token = TestContext.Current.CancellationToken;
        using var source = new TemporaryLibrary();
        using var destination = new TemporaryLibrary();
        using var library = InboxLibrary.Open(source.Path);
        string firstFolder = Directory.CreateDirectory(Path.Combine(library.PdfDirectory, "April")).FullName;
        string secondFolder = Directory.CreateDirectory(Path.Combine(library.PdfDirectory, "May")).FullName;
        string firstPath = Path.Combine(firstFolder, "Receipt.pdf");
        string secondPath = Path.Combine(secondFolder, "receipt.PDF");
        File.Copy(Fixture(Letter), firstPath);
        File.Copy(Fixture(Mixed), secondPath);
        File.Copy(Fixture(Letter), Path.Combine(library.PdfDirectory, "Unselected.pdf"));
        Assert.Equal(3, library.Refresh(token).Indexed);
        var documents = library.Find(new DocumentFilter(), token).Items;
        var first = documents.Single(document => document.RelativePath == "April/Receipt.pdf");
        var second = documents.Single(document => document.RelativePath == "May/receipt.PDF");
        string existing = Path.Combine(destination.Path, "Receipt.pdf");
        File.WriteAllText(existing, "Existing file");

        var exported = await library.ExportPdfsAsync([first.Id, second.Id, first.Id], destination.Path, cancellationToken: token);

        Assert.Equal(2, exported.PdfCount);
        Assert.True(Directory.Exists(Path.Combine(destination.Path, Path.GetFileName(exported.Location))));
        Assert.Equal(File.ReadAllBytes(firstPath), File.ReadAllBytes(Path.Combine(exported.Location, "Receipt.pdf")));
        Assert.Equal(File.ReadAllBytes(secondPath), File.ReadAllBytes(Path.Combine(exported.Location, "receipt (2).pdf")));
        Assert.Equal(2, Directory.EnumerateFileSystemEntries(exported.Location).Count());
        Assert.Equal("Existing file", File.ReadAllText(existing));
        Assert.Equal(3, library.Find(new DocumentFilter(), token).Items.Count);
    }

    [Fact]
    public async Task CancelledChangedOrMissingSelectionPublishesNoExportFolder()
    {
        var token = TestContext.Current.CancellationToken;
        using var source = new TemporaryLibrary();
        using var destination = new TemporaryLibrary();
        using var library = InboxLibrary.Open(source.Path);
        BatchFixtures.AddTwoDocuments(library, token);
        var documents = library.Find(new DocumentFilter(), token).Items;
        var selectedIds = documents.Select(document => document.Id).ToArray();
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(token);
        var progress = new InlinePdfProgress(copied =>
        {
            if (copied == 1) cancellation.Cancel();
        });

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            library.ExportPdfsAsync(selectedIds, destination.Path, progress, cancellation.Token));
        Assert.Empty(Directory.EnumerateFileSystemEntries(destination.Path));
        await Assert.ThrowsAsync<ArgumentException>(() =>
            library.ExportPdfsAsync(selectedIds, library.PdfDirectory, cancellationToken: token));

        string firstPath = library.ResolvePdf(documents[0]);
        DateTime firstTimestamp = File.GetLastWriteTimeUtc(firstPath);
        var editAfterCopy = new InlinePdfProgress(copied =>
        {
            if (copied == selectedIds.Length) File.SetLastWriteTimeUtc(firstPath, firstTimestamp.AddMinutes(5));
        });
        await Assert.ThrowsAsync<IOException>(() => library.ExportPdfsAsync(selectedIds, destination.Path, editAfterCopy, token));
        Assert.Empty(Directory.EnumerateFileSystemEntries(destination.Path));
        File.SetLastWriteTimeUtc(firstPath, firstTimestamp);

        File.Delete(library.ResolvePdf(documents[1]));
        await Assert.ThrowsAnyAsync<IOException>(() => library.ExportPdfsAsync(selectedIds, destination.Path, cancellationToken: token));

        Assert.Empty(Directory.EnumerateFileSystemEntries(destination.Path));
        Assert.True(File.Exists(firstPath));
    }

    private sealed record InlinePdfProgress(Action<int> Callback) : IProgress<int>
    {
        public void Report(int value) => Callback(value);
    }
}
