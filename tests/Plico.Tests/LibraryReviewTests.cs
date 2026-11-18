using Plico.Core;
using Xunit;

namespace Plico.Tests;

public sealed class LibraryReviewTests
{
    [Fact]
    public void BatchReviewUpdatesAllOrLeavesAllInTheirPreviousState()
    {
        using var directory = new TemporaryLibrary();
        using var library = InboxLibrary.Open(directory.Path);
        BatchFixtures.AddTwoDocuments(library, TestContext.Current.CancellationToken);
        var inbox = new DocumentFilter(InboxOnly: true);
        var selected = library.Find(inbox, TestContext.Current.CancellationToken).Items.Select(document => document.Id).ToArray();

        Assert.Throws<InvalidOperationException>(() => library.SetReviewedDocuments([selected[0], long.MaxValue], true));
        Assert.Equal(2, library.Find(inbox, TestContext.Current.CancellationToken).Items.Count);
        Assert.Equal(2, library.CountUnreviewedDocuments());

        library.SetReviewedDocuments(selected, true);
        Assert.Empty(library.Find(inbox, TestContext.Current.CancellationToken).Items);
        Assert.Equal(0, library.CountUnreviewedDocuments());
        Assert.All(library.Find(new DocumentFilter(), TestContext.Current.CancellationToken).Items, document => Assert.True(document.Reviewed));

        library.SetReviewedDocuments(selected, false);
        Assert.Equal(2, library.Find(inbox, TestContext.Current.CancellationToken).Items.Count);
        Assert.Equal(2, library.CountUnreviewedDocuments());
    }

    [Fact]
    public void InboxCountIgnoresReviewedAndMissingDocuments()
    {
        var token = TestContext.Current.CancellationToken;
        using var directory = new TemporaryLibrary();
        using var library = InboxLibrary.Open(directory.Path);
        BatchFixtures.AddTwoDocuments(library, token);
        var documents = library.Find(new DocumentFilter(), token).Items;
        library.SetReviewed(documents[0].Id, true);
        Assert.Equal(1, library.CountUnreviewedDocuments());

        string path = library.ResolvePdf(documents[1]);
        byte[] source = File.ReadAllBytes(path);
        File.Delete(path);
        library.Refresh(token);
        Assert.Equal(0, library.CountUnreviewedDocuments());

        File.WriteAllBytes(path, source);
        library.Refresh(token);
        Assert.Equal(1, library.CountUnreviewedDocuments());
    }
}

internal static class BatchFixtures
{
    internal static void AddTwoDocuments(InboxLibrary library, CancellationToken token)
    {
        var fixture = Path.Combine(AppContext.BaseDirectory, "fixtures", "Lease Renewal.pdf");
        File.Copy(fixture, Path.Combine(library.PdfDirectory, "April lease.pdf"));
        File.Copy(fixture, Path.Combine(library.PdfDirectory, "May lease.pdf"));
        Assert.Equal(2, library.Refresh(token).Indexed);
    }
}
