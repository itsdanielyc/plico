using Plico.Core;
using Xunit;

namespace Plico.Tests;

public sealed class LibraryBackupTests
{
    [Fact]
    public async Task BackupReopensWithPDFsAndTheirCatalogMetadata()
    {
        var token = TestContext.Current.CancellationToken;
        using var source = new TemporaryLibrary();
        using var destination = new TemporaryLibrary();
        using var library = InboxLibrary.Open(source.Path);
        var fixture = Path.Combine(AppContext.BaseDirectory, "fixtures", "Lease Renewal.pdf");
        var document = await library.ImportAsync(fixture, token);
        library.AddTag(document.Id, "Property");
        library.SetReviewed(document.Id, true);
        library.SetTitle(document.Id, "Lease correspondence");

        var backup = await library.BackupAsync(destination.Path, cancellationToken: token);
        using var reopened = InboxLibrary.Open(backup.Location);
        var found = Assert.Single(reopened.Find(new DocumentFilter(Query: "NORTHSTAR", Tag: "Property"), TestContext.Current.CancellationToken).Items);

        Assert.Equal(1, backup.PdfCount);
        Assert.Equal(document.Id, found.Id);
        Assert.Equal("Lease correspondence", found.Title);
        Assert.True(found.Reviewed);
        Assert.Equal(File.ReadAllBytes(fixture), File.ReadAllBytes(reopened.ResolvePdf(found)));
        Assert.True(File.Exists(library.ResolvePdf(document)));
    }

    [Fact]
    public async Task CancellationAfterTheFirstPdfClearsTheStagingFolder()
    {
        var token = TestContext.Current.CancellationToken;
        using var source = new TemporaryLibrary();
        using var destination = new TemporaryLibrary();
        using var library = InboxLibrary.Open(source.Path);
        BatchFixtures.AddTwoDocuments(library, token);
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(token);
        var progress = new InlineProgress(update =>
        {
            if (update.Copied == 1) cancellation.Cancel();
        });

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            library.BackupAsync(destination.Path, progress, cancellation.Token));
        Assert.Empty(Directory.EnumerateFileSystemEntries(destination.Path));
        Assert.Equal(2, library.Find(new DocumentFilter(), TestContext.Current.CancellationToken).Items.Count);
    }

    [Fact]
    public async Task BackupDestinationMustBeOutsideTheLibraryTree()
    {
        using var directory = new TemporaryLibrary();
        using var library = InboxLibrary.Open(directory.Path);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            library.BackupAsync(library.PdfDirectory, cancellationToken: TestContext.Current.CancellationToken));
        Assert.Empty(Directory.EnumerateFileSystemEntries(library.PdfDirectory));
    }

    private sealed record InlineProgress(Action<BackupProgress> Callback) : IProgress<BackupProgress>
    {
        public void Report(BackupProgress value) => Callback(value);
    }
}
