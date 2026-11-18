using Plico.Core;
using Xunit;

namespace Plico.Tests;

public sealed class LibraryBatchTagTests
{
    [Fact]
    public void BatchTagReusesAnExistingTagAndRejectsAPartialSelection()
    {
        using var directory = new TemporaryLibrary();
        using var library = InboxLibrary.Open(directory.Path);
        BatchFixtures.AddTwoDocuments(library, TestContext.Current.CancellationToken);
        var documents = library.Find(new DocumentFilter(), TestContext.Current.CancellationToken).Items;
        var ids = documents.Select(document => document.Id).ToArray();
        library.AddTag(ids[0], "Records");

        Assert.Throws<InvalidOperationException>(() => library.AddTagToDocuments([ids[0], long.MaxValue], "Do not add"));
        Assert.DoesNotContain("Do not add", library.GetTags());

        library.AddTagToDocuments(ids, " records ");

        var tagged = library.Find(new DocumentFilter(Tag: "RECORDS"), TestContext.Current.CancellationToken).Items;
        Assert.Equal(2, tagged.Count);
        Assert.All(tagged, document => Assert.Equal("Records", Assert.Single(document.Tags)));
    }

    [Fact]
    public void BatchTagRemovalChangesOnlyMatchingAssignmentsInTheSelection()
    {
        const string sharedTag = "Café";
        const string otherTag = "Keep";
        var token = TestContext.Current.CancellationToken;
        using var directory = new TemporaryLibrary();
        using var library = InboxLibrary.Open(directory.Path);
        BatchFixtures.AddTwoDocuments(library, token);
        var selected = library.Find(new DocumentFilter(), token).Items;
        string outsidePath = Path.Combine(library.PdfDirectory, "June lease.pdf");
        File.Copy(library.ResolvePdf(selected[0]), outsidePath);
        library.Refresh(token);
        long outside = library.Find(new DocumentFilter(), token).Items.Single(document => document.RelativePath == "June lease.pdf").Id;
        library.AddTagToDocuments([selected[0].Id, outside], sharedTag);
        library.AddTag(selected[1].Id, otherTag);

        long[] selection = [selected[0].Id, selected[1].Id, selected[0].Id];
        Assert.Equal(1, library.RemoveTagFromDocuments(selection, " cafe\u0301 ", token));
        Assert.Equal(0, library.RemoveTagFromDocuments(selection, sharedTag, token));
        Assert.Equal(outside, Assert.Single(library.Find(new DocumentFilter(Tag: sharedTag), token).Items).Id);
        Assert.Equal(selected[1].Id, Assert.Single(library.Find(new DocumentFilter(Tag: otherTag), token).Items).Id);
    }

    [Fact]
    public void BatchTagRemovalRejectsAnUnavailableSelectionBeforeChangingAnyLinks()
    {
        const string tag = "Records";
        var token = TestContext.Current.CancellationToken;
        using var directory = new TemporaryLibrary();
        using var library = InboxLibrary.Open(directory.Path);
        BatchFixtures.AddTwoDocuments(library, token);
        var selected = library.Find(new DocumentFilter(), token).Items;
        var ids = selected.Select(document => document.Id).ToArray();
        library.AddTagToDocuments(ids, tag);

        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        Assert.Throws<OperationCanceledException>(() => library.RemoveTagFromDocuments(ids, tag, cancelled.Token));
        Assert.Equal(ids.Length, library.Find(new DocumentFilter(Tag: tag), token).Items.Count);

        File.Delete(library.ResolvePdf(selected[1]));
        library.Refresh(token);
        Assert.Throws<InvalidOperationException>(() => library.RemoveTagFromDocuments(ids, tag, token));
        Assert.Equal(selected[0].Id, Assert.Single(library.Find(new DocumentFilter(Tag: tag), token).Items).Id);
    }
}
