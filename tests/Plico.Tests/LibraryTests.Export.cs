using Plico.Core;
using Xunit;

namespace Plico.Tests;

public sealed partial class LibraryTests
{
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
