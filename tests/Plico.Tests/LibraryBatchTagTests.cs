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
        var documents = library.Find(new DocumentFilter()).Items;
        var ids = documents.Select(document => document.Id).ToArray();
        library.AddTag(ids[0], "Records");

        Assert.Throws<InvalidOperationException>(() => library.AddTagToDocuments([ids[0], long.MaxValue], "Do not add"));
        Assert.DoesNotContain("Do not add", library.GetTags());

        library.AddTagToDocuments(ids, " records ");

        var tagged = library.Find(new DocumentFilter(Tag: "RECORDS")).Items;
        Assert.Equal(2, tagged.Count);
        Assert.All(tagged, document => Assert.Equal("Records", Assert.Single(document.Tags)));
    }
}
