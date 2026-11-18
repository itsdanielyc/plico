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

    [Fact]
    public void ReviewStatusSearchCombinesWithTextTagsDatesAndFolders()
    {
        var token = TestContext.Current.CancellationToken;
        using var directory = new TemporaryLibrary();
        using var library = InboxLibrary.Open(directory.Path);
        BatchFixtures.AddTwoDocuments(library, token);
        var documents = library.Find(new DocumentFilter(), token).Items;
        var reviewed = documents[0];
        var pending = documents[1];
        library.SetReviewed(reviewed.Id, true);
        foreach (var document in documents)
        {
            library.AddTag(document.Id, "Property");
            library.SetDocumentDate(document.Id, new DateOnly(2024, 2, 29));
        }

        const string common = "northstar tag:property dated:2024-02 folder:.";
        var inbox = Assert.Single(library.Find(new DocumentFilter(Query: $"{common} STATUS:Inbox"), token).Items);
        var archive = Assert.Single(library.Find(new DocumentFilter(Query: $"{common} status:reviewed STATUS:REVIEWED"), token).Items);
        Assert.Equal(pending.Id, inbox.Id);
        Assert.Equal(reviewed.Id, archive.Id);
        Assert.Contains("NORTHSTAR", archive.Match, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(reviewed.Id, Assert.Single(library.Find(new DocumentFilter(Query: "status:reviewed"), token).Items).Id);
        Assert.Empty(library.Find(new DocumentFilter(Query: "status:reviewed", InboxOnly: true), token).Items);
        Assert.Equal(pending.Id, Assert.Single(library.Find(new DocumentFilter(Query: "status:inbox", InboxOnly: true), token).Items).Id);

        foreach (string invalid in new[] { "status:", "status:unknown", "status:inbox status:reviewed", "status:\"inbox\"" })
        {
            Assert.Empty(library.Find(new DocumentFilter(Query: invalid), token).Items);
        }

        library.SetTitle(pending.Id, "Status reviewed notice");
        Assert.Equal(pending.Id, Assert.Single(library.Find(new DocumentFilter(Query: "\"status:reviewed\""), token).Items).Id);
        library.SetReviewed(pending.Id, true);
        Assert.Empty(library.Find(new DocumentFilter(Query: "status:inbox"), token).Items);
        Assert.Equal(2, library.Find(new DocumentFilter(Query: "status:reviewed"), token).Items.Count);
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
