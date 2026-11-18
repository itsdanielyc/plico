using System.Globalization;
using Plico.Core;
using Xunit;

namespace Plico.Tests;

public sealed partial class LibraryTests
{
    [Fact]
    public void FolderSearchUsesTheStoredPathAndCombinesWithOtherClauses()
    {
        using var temporary = new TemporaryLibrary();
        using var library = InboxLibrary.Open(temporary.Path);
        var token = TestContext.Current.CancellationToken;
        string[] paths =
        [
            "Filed/Tax Forms/Current lease.pdf",
            "Filed/Tax Forms/2026/Next lease.pdf",
            "Filed/Tax Forms Archive/Previous lease.pdf",
            "Filed/Tax %_/Literal lease.pdf",
            "Filed/Tax AB/Similar lease.pdf",
            "Root lease.pdf"
        ];
        foreach (string path in paths)
        {
            string destination = Path.Combine(library.PdfDirectory, path);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(Fixture(Letter), destination);
        }
        Assert.Equal(paths.Length, library.Refresh(token).Indexed);
        var documents = library.Find(new DocumentFilter(), token).Items.ToDictionary(document => document.RelativePath);
        var nested = documents[paths[1]];
        library.AddTag(nested.Id, "Rental");
        library.AddTag(documents[paths[2]].Id, "Rental");

        var branch = library.Find(new DocumentFilter(Query: "FOLDER:“Filed/Tax Forms”"), token).Items;
        Assert.Equal(paths.Take(2).Order(StringComparer.Ordinal), branch.Select(document => document.RelativePath).Order(StringComparer.Ordinal));
        var nestedOnly = library.Find(new DocumentFilter(Query: "folder:Filed folder:\"Filed/Tax Forms/2026\""), token).Items;
        Assert.Equal(nested.Id, Assert.Single(nestedOnly).Id);
        string year = nested.AddedAt.LocalDateTime.ToString("yyyy", CultureInfo.InvariantCulture);
        var combined = Assert.Single(library.Find(new DocumentFilter(Query: $"norths folder:\"Filed/Tax Forms\" tag:rental added:{year}"), token).Items);
        Assert.Equal(nested.Id, combined.Id);
        Assert.Contains(LetterKeyword, combined.Match, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(paths[3], Assert.Single(library.Find(new DocumentFilter(Query: "folder:\"Filed/Tax %_\""), token).Items).RelativePath);
        Assert.Equal(paths[5], Assert.Single(library.Find(new DocumentFilter(Query: "folder:."), token).Items).RelativePath);

        library.SetTitle(documents[paths[5]].Id, "Folder Filed Tax Forms notes");
        Assert.Equal(paths[5], Assert.Single(library.Find(new DocumentFilter(Query: "\"folder:Filed/Tax Forms\""), token).Items).RelativePath);
        foreach (string query in new[]
        {
            "folder:", "folder:../Filed", "folder:/Filed", "folder:Filed/", "folder:\"Filed/Tax Forms",
            "folder:\"filed/Tax Forms\"", "folder:. folder:Filed", "folder:\"Filed/Tax Forms\" folder:\"Filed/Tax AB\""
        })
        {
            Assert.Empty(library.Find(new DocumentFilter(Query: query), token).Items);
        }
    }
}
