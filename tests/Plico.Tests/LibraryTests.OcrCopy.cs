using Plico.Core;
using UglyToad.PdfPig;
using UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor;
using UglyToad.PdfPig.Writer;
using Xunit;

namespace Plico.Tests;

public sealed partial class LibraryTests
{
    [Fact]
    public async Task SearchableCopyPreservesTheIndexedScanAndReusesTheFirstCopy()
    {
        using var temporary = new TemporaryLibrary();
        using var library = InboxLibrary.Open(temporary.Path);
        var token = TestContext.Current.CancellationToken;
        string folder = Directory.CreateDirectory(Path.Combine(library.PdfDirectory, "Filed")).FullName;
        string path = Path.Combine(folder, "Paper receipt.pdf");
        File.Copy(Fixture("Scanned Receipt.pdf"), path);
        byte[] originalBytes = File.ReadAllBytes(path);
        Assert.Equal(1, library.Refresh(token).Indexed);
        var original = Assert.Single(library.Find(new DocumentFilter(), token).Items);
        Assert.False(original.HasText);
        var legacyRepeat = await library.ImportFileAsync(path, token);
        Assert.False(legacyRepeat.Added);
        Assert.Equal(original.Id, legacyRepeat.Document.Id);
        library.AddTag(original.Id, "Workshop");
        library.SetTitle(original.Id, "October receipt");
        library.SetReviewed(original.Id, reviewed: true);

        var copy = await library.CreateSearchableCopyAsync(original.Id, token);

        Assert.True(copy.Added);
        Assert.NotEqual(original.Id, copy.Document.Id);
        Assert.Equal("October receipt (searchable)", copy.Document.Title);
        Assert.True(copy.Document.HasText);
        Assert.False(copy.Document.Reviewed);
        Assert.Equal("Workshop", Assert.Single(copy.Document.Tags));
        using (var pdf = PdfDocument.Open(library.ResolvePdf(copy.Document)))
        {
            Assert.Equal(original.PageCount, pdf.NumberOfPages);
            Assert.Contains(ReceiptKeyword, ContentOrderTextExtractor.GetText(pdf.GetPage(1)), StringComparison.OrdinalIgnoreCase);
        }
        var found = Assert.Single(library.Find(new DocumentFilter(Query: ReceiptKeyword, Tag: "Workshop"), token).Items);
        Assert.Equal(copy.Document.Id, found.Id);

        var repeat = await library.CreateSearchableCopyAsync(original.Id, token);

        Assert.False(repeat.Added);
        Assert.Equal(copy.Document.Id, repeat.Document.Id);
        Assert.Equal(2, Directory.EnumerateFiles(library.PdfDirectory, "*.pdf", SearchOption.AllDirectories).Count());
        Assert.Equal(originalBytes, File.ReadAllBytes(path));
        Assert.True(library.Find(new DocumentFilter(Tag: "Workshop"), token).Items.Single(item => item.Id == original.Id).Reviewed);
    }

    [Fact]
    public async Task SearchableCopyPublishesNothingForABlankChangedOrCancelledScan()
    {
        using var temporary = new TemporaryLibrary();
        using var library = InboxLibrary.Open(temporary.Path);
        var token = TestContext.Current.CancellationToken;
        using var blank = new PdfDocumentBuilder();
        blank.AddPage(UglyToad.PdfPig.Content.PageSize.A4);
        string path = Path.Combine(library.PdfDirectory, "Blank scan.pdf");
        byte[] contents = blank.Build();
        File.WriteAllBytes(path, contents);
        Assert.Equal(1, library.Refresh(token).Indexed);
        var scan = Assert.Single(library.Find(new DocumentFilter(), token).Items);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => library.CreateSearchableCopyAsync(scan.Id, new CancellationToken(true)));
        var empty = await Assert.ThrowsAsync<InvalidDataException>(() => library.CreateSearchableCopyAsync(scan.Id, token));
        Assert.Contains("no searchable text", empty.Message, StringComparison.OrdinalIgnoreCase);
        File.SetLastWriteTimeUtc(path, File.GetLastWriteTimeUtc(path).AddMinutes(5));
        var changed = await Assert.ThrowsAsync<IOException>(() => library.CreateSearchableCopyAsync(scan.Id, token));
        Assert.Contains("refresh", changed.Message, StringComparison.OrdinalIgnoreCase);

        Assert.Equal(path, Assert.Single(Directory.EnumerateFiles(library.PdfDirectory)));
        Assert.Equal(contents, File.ReadAllBytes(path));
        Assert.Equal(scan.Id, Assert.Single(library.Find(new DocumentFilter(), token).Items).Id);
        Assert.Empty(Directory.EnumerateDirectories(Path.Combine(temporary.Path, ".plico"), "import-*"));
    }
}
