using Plico.Core;
using Xunit;

namespace Plico.Tests;

public sealed class LibrarySearchQueryIssuesTests
{
    [Theory]
    [InlineData("...?", SearchQueryIssue.NoSearchTerms)]
    [InlineData("notice tag:\"Property tax", SearchQueryIssue.Tag)]
    [InlineData("added:2024-13", SearchQueryIssue.AddedDate)]
    [InlineData("dated:2023-02-29", SearchQueryIssue.DocumentDate)]
    [InlineData("dated:2024-02 dated:2024-03", SearchQueryIssue.DocumentDate)]
    [InlineData("folder:Filed/../Tax", SearchQueryIssue.Folder)]
    [InlineData("folder:Receipts folder:Letters", SearchQueryIssue.Folder)]
    [InlineData("status:", SearchQueryIssue.ReviewStatus)]
    [InlineData("status:inbox status:reviewed", SearchQueryIssue.ReviewStatus)]
    public void SearchReportsWhichClauseNeedsRepair(string query, SearchQueryIssue issue)
    {
        using var directory = new TemporaryLibrary();
        using var library = InboxLibrary.Open(directory.Path);

        var page = library.Find(new DocumentFilter(Query: query), TestContext.Current.CancellationToken);

        Assert.Equal(issue, page.QueryIssue);
        Assert.Empty(page.Items);
        Assert.False(page.HasMore);
    }

    [Fact]
    public void ValidQueriesCanReturnNoResultsWithoutARepairHint()
    {
        var token = TestContext.Current.CancellationToken;
        using var directory = new TemporaryLibrary();
        using var library = InboxLibrary.Open(directory.Path);
        DocumentFilter[] filters =
        [
            new(),
            new(Query: "\"status:pending\""),
            new(Query: "tag:missing status:inbox"),
            new(Query: "status:reviewed", InboxOnly: true)
        ];

        foreach (var filter in filters)
        {
            var page = library.Find(filter, token);
            Assert.Null(page.QueryIssue);
            Assert.Empty(page.Items);
            Assert.False(page.HasMore);
        }
    }
}
