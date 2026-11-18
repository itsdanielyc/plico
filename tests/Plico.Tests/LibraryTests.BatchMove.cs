using Plico.Core;
using Xunit;

namespace Plico.Tests;

public sealed partial class LibraryTests
{
    [Fact]
    public async Task BatchMovePreservesOrderAndTagsAcrossFilenameCollisions()
    {
        var token = TestContext.Current.CancellationToken;
        using var temporary = new TemporaryLibrary();
        using var library = InboxLibrary.Open(temporary.Path);
        const string filename = "Lease.pdf";
        string april = Directory.CreateDirectory(Path.Combine(library.PdfDirectory, "April")).FullName;
        string june = Directory.CreateDirectory(Path.Combine(library.PdfDirectory, "June")).FullName;
        string destination = Directory.CreateDirectory(Path.Combine(library.PdfDirectory, "Filed")).FullName;
        string reservedPath = Path.Combine(destination, "Lease (2).pdf");
        foreach (string folder in new[] { april, june, destination }) File.Copy(Fixture(Letter), Path.Combine(folder, filename));
        File.Copy(Fixture(Letter), reservedPath);
        Assert.Equal(4, library.Refresh(token).Indexed);
        File.Delete(reservedPath);
        library.Refresh(token);
        var before = library.Find(new DocumentFilter(), token).Items.ToDictionary(item => item.RelativePath);
        long aprilId = before["April/Lease.pdf"].Id;
        long juneId = before["June/Lease.pdf"].Id;
        long filedId = before["Filed/Lease.pdf"].Id;
        library.AddTag(aprilId, "April");
        library.AddTag(juneId, "June");
        library.SetReviewed(juneId, true);
        long[] requested = [juneId, filedId, aprilId, juneId];

        var moved = await library.MoveSelectedPdfsAsync(requested, destination, cancellationToken: token);

        Assert.Equal(new BatchMoveResult(2, 1, 0), moved);
        var after = library.Find(new DocumentFilter(), token).Items.ToDictionary(item => item.Id);
        Assert.Equal("Filed/Lease (3).pdf", after[juneId].RelativePath);
        Assert.Equal("Filed/Lease (4).pdf", after[aprilId].RelativePath);
        Assert.Equal("Filed/Lease.pdf", after[filedId].RelativePath);
        Assert.Equal("April", Assert.Single(after[aprilId].Tags));
        Assert.Equal("June", Assert.Single(after[juneId].Tags));
        Assert.True(after[juneId].Reviewed);
        Assert.Empty(Directory.EnumerateFiles(april));
        Assert.Empty(Directory.EnumerateFiles(june));
        Assert.False(File.Exists(reservedPath));
        Assert.Equal(new BatchMoveResult(0, 3, 0), await library.MoveSelectedPdfsAsync(requested, destination, cancellationToken: token));
    }

    [Fact]
    public async Task BatchMoveValidatesTheWholeSelectionBeforeMovingAnyPdf()
    {
        var token = TestContext.Current.CancellationToken;
        using var temporary = new TemporaryLibrary();
        using var library = InboxLibrary.Open(temporary.Path);
        BatchFixtures.AddTwoDocuments(library, token);
        var documents = library.Find(new DocumentFilter(), token).Items;
        long[] ids = documents.Select(document => document.Id).ToArray();
        string available = library.ResolvePdf(documents[0]);
        File.Delete(library.ResolvePdf(documents[1]));
        string destination = Directory.CreateDirectory(Path.Combine(library.PdfDirectory, "Filed")).FullName;

        await Assert.ThrowsAnyAsync<IOException>(() => library.MoveSelectedPdfsAsync(ids, destination, cancellationToken: token));

        Assert.True(File.Exists(available));
        Assert.Empty(Directory.EnumerateFiles(destination));
        Assert.Equal(documents[0].RelativePath, library.Find(new DocumentFilter(), token).Items.Single(item => item.Id == ids[0]).RelativePath);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task BatchMovePausesAfterACompletedMoveAndReportsRemainingPdfs(bool cancel)
    {
        var token = TestContext.Current.CancellationToken;
        using var temporary = new TemporaryLibrary();
        using var library = InboxLibrary.Open(temporary.Path);
        BatchFixtures.AddTwoDocuments(library, token);
        var documents = library.Find(new DocumentFilter(), token).Items;
        long[] ids = documents.Select(document => document.Id).ToArray();
        string source = library.ResolvePdf(documents[1]);
        string destination = Directory.CreateDirectory(Path.Combine(library.PdfDirectory, "Filed")).FullName;
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(token);
        var progress = new InlinePdfProgress(processed =>
        {
            if (processed != 1) return;
            if (cancel) cancellation.Cancel();
            else File.SetLastWriteTimeUtc(source, File.GetLastWriteTimeUtc(source).AddMinutes(5));
        });

        var result = await library.MoveSelectedPdfsAsync(ids, destination, progress, cancellation.Token);

        Assert.Equal(cancel, result.Cancelled);
        Assert.Equal(1, result.Moved);
        Assert.Equal(1, result.Remaining);
        Assert.Equal(0, result.AlreadyInFolder);
        if (cancel) Assert.Null(result.Failure);
        else Assert.Equal(documents[1].RelativePath, Assert.IsType<BatchMoveFailure>(result.Failure).RelativePath);
        Assert.True(File.Exists(source));
        Assert.Equal(Path.GetFileName(documents[0].RelativePath), Path.GetFileName(Assert.Single(Directory.EnumerateFiles(destination))));
        var after = library.Find(new DocumentFilter(), token).Items.ToDictionary(item => item.Id);
        Assert.StartsWith("Filed/", after[ids[0]].RelativePath);
        Assert.Equal(documents[1].RelativePath, after[ids[1]].RelativePath);
    }
}
