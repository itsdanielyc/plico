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
        var firstPage = catalog.Find(titleOrder);
        var secondPage = catalog.Find(titleOrder with { Offset = firstPage.Items.Count });

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

        Assert.Equal(new[] { relevant, newest }, catalog.Find(search).Items.Select(document => document.Id));
        Assert.Equal(new[] { newest, relevant },
            catalog.Find(search with { Sort = DocumentSort.Newest }).Items.Select(document => document.Id));
    }

    private static long AddDocument(LibraryCatalog catalog, string name, string body) =>
        catalog.Upsert($"{name}.pdf", name, new PdfText(body, PageCount: 1, HasTextOnEveryPage: true),
            new FileVersion(LastModifiedTicks: 0, Length: 0, Present: true), pdfFingerprint: []);
}
