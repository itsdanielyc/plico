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
        var selected = library.Find(inbox).Items.Select(document => document.Id).ToArray();

        Assert.Throws<InvalidOperationException>(() => library.SetReviewedDocuments([selected[0], long.MaxValue], true));
        Assert.Equal(2, library.Find(inbox).Items.Count);

        library.SetReviewedDocuments(selected, true);
        Assert.Empty(library.Find(inbox).Items);
        Assert.All(library.Find(new DocumentFilter()).Items, document => Assert.True(document.Reviewed));

        library.SetReviewedDocuments(selected, false);
        Assert.Equal(2, library.Find(inbox).Items.Count);
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
