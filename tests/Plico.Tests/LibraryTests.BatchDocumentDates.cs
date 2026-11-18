using Plico.Core;
using Xunit;

namespace Plico.Tests;

public sealed partial class LibraryTests
{
    [Fact]
    public void BatchDocumentDatesApplyAndClearOnlySelectedRecords()
    {
        const string outsidePath = "June lease.pdf";
        const string datedQuery = "dated:2024-02-29";
        DateOnly sharedDate = new(2024, 2, 29);
        DateOnly outsideDate = new(2024, 3, 1);
        var token = TestContext.Current.CancellationToken;
        using var temporary = new TemporaryLibrary();
        using var library = InboxLibrary.Open(temporary.Path);
        BatchFixtures.AddTwoDocuments(library, token);
        var selected = library.Find(new DocumentFilter(), token).Items;
        File.Copy(library.ResolvePdf(selected[0]), Path.Combine(library.PdfDirectory, outsidePath));
        library.Refresh(token);
        long outsideId = library.Find(new DocumentFilter(), token).Items.Single(document => document.RelativePath == outsidePath).Id;
        library.SetDocumentDate(outsideId, outsideDate);

        long[] selectedIds = [selected[0].Id, selected[1].Id, selected[0].Id];
        library.SetDocumentDates(selectedIds, sharedDate, token);
        var dated = library.Find(new DocumentFilter(Query: datedQuery), token).Items;
        Assert.Equal(selectedIds.Distinct().Order(), dated.Select(document => document.Id).Order());
        library.SetDocumentDates(selectedIds, null, token);
        Assert.Empty(library.Find(new DocumentFilter(Query: datedQuery), token).Items);
        var after = library.Find(new DocumentFilter(), token).Items;
        Assert.All(after.Where(document => document.Id != outsideId), document => Assert.Null(document.DocumentDate));
        Assert.Equal(outsideDate, after.Single(document => document.Id == outsideId).DocumentDate);
    }

    [Fact]
    public void BatchDocumentDateChangeRollsBackWhenASelectionMemberIsUnavailable()
    {
        DateOnly original = new(2024, 1, 7);
        DateOnly replacement = new(2024, 2, 29);
        var token = TestContext.Current.CancellationToken;
        using var temporary = new TemporaryLibrary();
        using var library = InboxLibrary.Open(temporary.Path);
        BatchFixtures.AddTwoDocuments(library, token);
        var selected = library.Find(new DocumentFilter(), token).Items;
        var ids = selected.Select(document => document.Id).ToArray();
        foreach (long id in ids) library.SetDocumentDate(id, original);

        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        Assert.Throws<OperationCanceledException>(() => library.SetDocumentDates(ids, replacement, cancelled.Token));
        Assert.All(library.Find(new DocumentFilter(), token).Items, document => Assert.Equal(original, document.DocumentDate));

        string unavailablePath = library.ResolvePdf(selected[1]);
        File.Delete(unavailablePath);
        library.Refresh(token);
        Assert.Throws<InvalidOperationException>(() => library.SetDocumentDates(ids, replacement, token));
        Assert.Equal(original, Assert.Single(library.Find(new DocumentFilter(), token).Items).DocumentDate);

        File.Copy(library.ResolvePdf(selected[0]), unavailablePath);
        library.Refresh(token);
        var restored = library.Find(new DocumentFilter(), token).Items;
        Assert.Equal(ids.Order(), restored.Select(document => document.Id).Order());
        Assert.All(restored, document => Assert.Equal(original, document.DocumentDate));
    }
}
