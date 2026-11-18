using Plico.Core;
using Xunit;

namespace Plico.Tests;

public sealed class LibraryBooleanSearchTests
{
    [Fact]
    public void GroupingAndNegationApplyAcrossTextTagsAndDates()
    {
        using var temporary = new TemporaryLibrary();
        var catalog = new LibraryCatalog(Path.Combine(temporary.Path, "catalog.sqlite3"));
        long renewal = Add(catalog, "Renewal notice", "renewal ahead");
        long estimate = Add(catalog, "Estimate", "electrical work");
        long excluded = Add(catalog, "Old renewal", "archive this renewal");
        catalog.AddTag(estimate, "Home work");
        catalog.AddTag(excluded, "Archive");
        catalog.SetDocumentDate(estimate, new DateOnly(2026, 5, 1));
        catalog.SetReviewed(renewal, true);

        var token = TestContext.Current.CancellationToken;
        var result = catalog.Find(new DocumentFilter(Query: "(renewal OR (tag:\"Home work\" dated:2026)) -tag:archive"), token);
        Assert.Equal([renewal, estimate], result.Items.Select(document => document.Id));
        Assert.Equal(renewal, Assert.Single(catalog.Find(new DocumentFilter(Query: "renewal NOT archive"), token).Items).Id);
        Assert.Equal(estimate, Assert.Single(catalog.Find(new DocumentFilter(Query: "NOT (status:reviewed OR tag:archive)"), token).Items).Id);
    }

    [Fact]
    public void TitleWeightingAndDiacriticMatchesProduceHighlightOffsets()
    {
        using var temporary = new TemporaryLibrary();
        var catalog = new LibraryCatalog(Path.Combine(temporary.Path, "catalog.sqlite3"));
        long titled = Add(catalog, "Renouvelé", "This has a different subject.");
        long body = Add(catalog, "Report", "Le contrat renouvelé inclut des amendements.");
        var result = catalog.Find(new DocumentFilter(Query: "renouvele OR invoice"), TestContext.Current.CancellationToken);
        Assert.Equal([titled, body], result.Items.Select(document => document.Id));
        var excerpt = result.Items[1];
        var match = Assert.Single(excerpt.MatchHighlights);
        Assert.Equal("renouvelé", excerpt.Match.Substring(match.Start, match.Length));
    }

    [Theory]
    [InlineData("(receipt OR invoice")]
    [InlineData("receipt OR")]
    [InlineData("NOT ()")]
    public void BrokenBooleanSyntaxHasATypedError(string query)
    {
        Assert.Equal(SearchQueryIssue.BooleanSyntax, SearchTerms.Compile(query).QueryIssue);
    }

    [Fact]
    public void FolderPickerReplacesTheOuterFolderAndKeepsBranchFilters()
    {
        const string query = "(invoice OR folder:\"Filed/Tax\") folder:Filed";
        string scoped = SearchTerms.WithFolderScope(query, "Archive");
        Assert.Equal("(invoice OR folder:\"Filed/Tax\") folder:\"Archive\"", scoped);
        Assert.True(SearchTerms.Compile(scoped).IsValid);
    }

    private static long Add(LibraryCatalog catalog, string title, string body) =>
        catalog.Upsert(title + ".pdf", title, new PdfText(body, 1, true), new FileVersion(1, 1024, true), new byte[32]);
}
