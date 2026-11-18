using Plico.Core;
using Xunit;

namespace Plico.Tests;

public sealed class LibrarySortTests
{
    [Fact]
    public void TitleOrderUsesEditedTitlesAndStableCaseInsensitiveTiesAcrossPages()
    {
        using var directory = new TemporaryLibrary();
        var catalog = new LibraryCatalog(Path.Combine(directory.Path, "catalog.sqlite3"));
        var earlier = AddDocument(catalog, "Z-last-file", "Report");
        var later = AddDocument(catalog, "A-first-file", "Report");
        catalog.SetTitle(earlier, "ÉCLAIR", "Z-last-file");
        catalog.SetTitle(later, "éclair", "A-first-file");
        for (var index = 0; index < LibraryCatalog.PageSize; index++)
        {
            AddDocument(catalog, $"Apricot {index:D2}", "Report");
        }

        var titleOrder = new DocumentFilter(Sort: DocumentSort.Title);
        var firstPage = catalog.Find(titleOrder, TestContext.Current.CancellationToken);
        var secondPage = catalog.Find(titleOrder with { Offset = firstPage.Items.Count }, TestContext.Current.CancellationToken);

        Assert.True(firstPage.HasMore);
        Assert.Equal("Apricot 00", firstPage.Items[0].Title);
        Assert.Equal(new[] { later, earlier }, secondPage.Items.Select(document => document.Id));
        Assert.False(secondPage.HasMore);
    }

    [Fact]
    public void NewestOverridesRelevanceWithinAnInboxAndTagSearch()
    {
        using var directory = new TemporaryLibrary();
        var catalog = new LibraryCatalog(Path.Combine(directory.Path, "catalog.sqlite3"));
        const string tag = "Expenses";
        var relevant = AddDocument(catalog, "Bills on file", "Bills bills bills");
        var newest = AddDocument(catalog, "Newer report", "Bills");
        AddDocument(catalog, "Untagged bills", "Bills");
        var reviewed = AddDocument(catalog, "Reviewed bills", "Bills");
        foreach (long documentId in new[] { relevant, newest, reviewed }) catalog.AddTag(documentId, tag);
        catalog.SetReviewed(reviewed, reviewed: true);

        var search = new DocumentFilter(Query: "bills", Tag: tag, InboxOnly: true);

        Assert.Equal(new[] { relevant, newest }, catalog.Find(search, TestContext.Current.CancellationToken).Items.Select(document => document.Id));
        Assert.Equal(new[] { newest, relevant },
            catalog.Find(search with { Sort = DocumentSort.Newest }, TestContext.Current.CancellationToken).Items.Select(document => document.Id));
    }

    [Fact]
    public void DocumentDateSortPaginatesWithStableTiesAndUndatedRecordsLast()
    {
        using var directory = new TemporaryLibrary();
        var catalog = new LibraryCatalog(Path.Combine(directory.Path, "catalog.sqlite3"));
        var firstDay = new DateOnly(2024, 1, 1);
        long olderTie = AddDocument(catalog, "First with date", "Report");
        long olderUndated = AddDocument(catalog, "First undated", "Report");
        catalog.SetDocumentDate(olderTie, firstDay);

        var dated = new List<long>();
        for (int index = 1; index < LibraryCatalog.PageSize; index++)
        {
            long id = AddDocument(catalog, $"Dated {index:D2}", "Report");
            catalog.SetDocumentDate(id, firstDay.AddDays(index));
            dated.Add(id);
        }
        long newerTie = AddDocument(catalog, "Second with date", "Report");
        catalog.SetDocumentDate(newerTie, firstDay);
        long newerUndated = AddDocument(catalog, "Second undated", "Report");

        var sort = new DocumentFilter(Sort: DocumentSort.DocumentDate);
        var first = catalog.Find(sort, TestContext.Current.CancellationToken);
        var next = catalog.Find(sort with { Offset = first.Items.Count }, TestContext.Current.CancellationToken);
        Assert.True(first.HasMore);
        Assert.Equal(dated.AsEnumerable().Reverse().Append(newerTie), first.Items.Select(document => document.Id));
        Assert.Equal(new[] { olderTie, newerUndated, olderUndated }, next.Items.Select(document => document.Id));
        Assert.False(next.HasMore);
    }

    private static long AddDocument(LibraryCatalog catalog, string name, string body) =>
        catalog.Upsert($"{name}.pdf", name, new PdfText(body, PageCount: 1, HasTextOnEveryPage: true),
            new FileVersion(LastModifiedTicks: 0, Length: 0, Present: true), pdfFingerprint: []);
}
