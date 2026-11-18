using Plico.Core;
using Xunit;

namespace Plico.Tests;

public sealed partial class LibraryTests
{
    [Fact]
    public void TypedTagSearchRequiresEveryTagAndTheTextTerms()
    {
        using var temporary = new TemporaryLibrary();
        using var library = InboxLibrary.Open(temporary.Path);
        var token = TestContext.Current.CancellationToken;
        foreach (string filename in new[] { "Current lease.pdf", "Earlier lease.pdf", "Office lease.pdf" })
        {
            File.Copy(Fixture(Letter), Path.Combine(library.PdfDirectory, filename));
        }
        Assert.Equal(3, library.Refresh(token).Indexed);
        var documents = library.Find(new DocumentFilter(), token).Items;
        var current = documents.Single(document => document.Title == "Current lease");
        var earlier = documents.Single(document => document.Title == "Earlier lease");
        var office = documents.Single(document => document.Title == "Office lease");
        library.AddTag(current.Id, "Réntal");
        library.AddTag(current.Id, "Year 2026");
        library.AddTag(earlier.Id, "Réntal");
        library.AddTag(earlier.Id, "Year 2025");
        library.AddTag(office.Id, "Office");
        library.AddTag(office.Id, "Year 2026");

        const string query = "norths tag:RE\u0301NTAL tag:\"Year 2026\" TAG:réntal";
        var match = Assert.Single(library.Find(new DocumentFilter(Query: query), token).Items);
        Assert.Equal(current.Id, match.Id);
        Assert.Contains(LetterKeyword, match.Match, StringComparison.OrdinalIgnoreCase);
        var scoped = new DocumentFilter(Query: "tag:“Year 2026”", Tag: "réntal");
        Assert.Equal(current.Id, Assert.Single(library.Find(scoped, token).Items).Id);
        Assert.Equal(2, library.Find(new DocumentFilter(Query: "tag:réntal"), token).Items.Count);
        Assert.Empty(library.Find(new DocumentFilter(Query: "tag:rén"), token).Items);
        Assert.Empty(library.Find(new DocumentFilter(Query: "unmatchedword tag:réntal"), token).Items);
    }

    [Fact]
    public async Task TypedTagSearchHandlesLiteralPhrasesEscapesAndIncompleteInput()
    {
        using var temporary = new TemporaryLibrary();
        using var library = InboxLibrary.Open(temporary.Path);
        var token = TestContext.Current.CancellationToken;
        var document = await library.ImportAsync(Fixture(Letter), token);
        library.SetTitle(document.Id, "Tag archive");
        library.AddTag(document.Id, """Quotes "A" and “B” \ 2026""");

        const string escaped = """tag:"Quotes \"A\" and “B” \\ 2026" """;
        Assert.Equal(document.Id, Assert.Single(library.Find(new DocumentFilter(Query: escaped), token).Items).Id);
        const string literal = @"""tag:archive""";
        Assert.Equal(document.Id, Assert.Single(library.Find(new DocumentFilter(Query: literal), token).Items).Id);

        string[] incomplete = ["tag:", "tag:\"Quotes", "tag:\"\"", "tag:\"Quotes\"suffix"];
        foreach (string query in incomplete)
        {
            Assert.Empty(library.Find(new DocumentFilter(Query: query), token).Items);
        }
    }
}
