using Plico.Core;
using Xunit;

namespace Plico.Tests;

public sealed partial class LibraryTests
{
    [Fact]
    public void FolderScopePreservesQuotedTextAndTheOtherFilters()
    {
        const string original = """renewal folder:Old tag:"Property tax" folder:"Old/2024 files" "folder:newsletter" dated:2024-02 status:inbox""";
        const string folder = """Filed/Tax "notes" \ purchase""";
        var previous = SearchTerms.Compile(original);

        string updated = SearchTerms.WithFolderScope(original, folder);
        var search = SearchTerms.Compile(updated);

        Assert.True(search.IsValid);
        Assert.Equal(folder, search.FolderPath);
        Assert.Equal(previous.FullTextExpression, search.FullTextExpression);
        Assert.Equal(previous.TagKeys, search.TagKeys);
        Assert.Equal(previous.DocumentPeriod, search.DocumentPeriod);
        Assert.Equal(previous.Reviewed, search.Reviewed);
        Assert.Contains("\"folder:newsletter\"", updated, StringComparison.Ordinal);
        Assert.DoesNotContain("folder:Old", updated, StringComparison.Ordinal);
        Assert.Equal("renewal folder:\"Filed\"",
            SearchTerms.WithFolderScope("renewal folder:Old folder:Other", "Filed"));
    }

    [Fact]
    public void FolderScopeRejectsAnUnfinishedOrOverlengthQuery()
    {
        Assert.Throws<ArgumentException>(() => SearchTerms.WithFolderScope("renewal folder:\"unfinished", "Filed"));
        Assert.Throws<ArgumentException>(() => SearchTerms.WithFolderScope("\"unfinished phrase", "Filed"));
        string longQuery = new('x', InboxLibrary.MaximumSearchQueryLength - 2);
        var overlength = Assert.Throws<ArgumentException>(() => SearchTerms.WithFolderScope(longQuery, "Filed"));
        Assert.Contains("Shorten the search", overlength.Message, StringComparison.Ordinal);
        string longFolder = string.Join('/', Enumerable.Repeat("Archive folder", 20));
        Assert.Throws<ArgumentException>(() => SearchTerms.WithFolderScope("", longFolder));
    }

    [Fact]
    public void FolderPickerUsesTheRelativePathAndRejectsFoldersOutsideTheLibrary()
    {
        var token = TestContext.Current.CancellationToken;
        using var temporary = new TemporaryLibrary();
        using var library = InboxLibrary.Open(temporary.Path);
        string filed = Directory.CreateDirectory(Path.Combine(library.PdfDirectory, "Filed", "2024")).FullName;
        File.Copy(Fixture(Letter), Path.Combine(library.PdfDirectory, "Root lease.pdf"));
        File.Copy(Fixture(Letter), Path.Combine(filed, "Filed lease.pdf"));
        Assert.Equal(2, library.Refresh(token).Indexed);

        string root = library.CreateFolderSearchQuery(library.PdfDirectory, "");
        Assert.Equal("folder:.", root);
        Assert.Equal("Root lease", Assert.Single(library.Find(new DocumentFilter(Query: root), token).Items).Title);
        string scoped = library.CreateFolderSearchQuery(filed, "renewal");
        Assert.Equal("renewal folder:\"Filed/2024\"", scoped);
        Assert.Equal("Filed lease", Assert.Single(library.Find(new DocumentFilter(Query: scoped), token).Items).Title);
        Assert.Throws<ArgumentException>(() => library.CreateFolderSearchQuery(temporary.Path, ""));

        if (OperatingSystem.IsWindows()) return;
        string alias = Path.Combine(library.PdfDirectory, "Outside");
        Directory.CreateSymbolicLink(alias, temporary.Path);
        try
        {
            Assert.Throws<ArgumentException>(() => library.CreateFolderSearchQuery(alias, ""));
        }
        finally
        {
            Directory.Delete(alias);
        }
    }
}
