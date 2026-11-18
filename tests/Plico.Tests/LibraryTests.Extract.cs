using Plico.Core;
using UglyToad.PdfPig;
using UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor;
using Xunit;

namespace Plico.Tests;

public sealed partial class LibraryTests
{
    [Fact]
    public void PageSelectionMergesOverlapsAndValidatesBoundsWithoutExpandingRanges()
    {
        var selection = PdfPageSelection.Parse("7, 3-5, 1, 5-6, 3", 9);
        Assert.Equal(new[] { new PdfPageRange(1, 1), new PdfPageRange(3, 7) }, selection.Ranges);
        Assert.Equal(6, selection.SelectedPageCount);
        var large = PdfPageSelection.Parse("2147483647,1-2147483646", int.MaxValue);
        Assert.Equal(int.MaxValue, large.SelectedPageCount);
        Assert.Equal(new PdfPageRange(1, int.MaxValue), Assert.Single(large.Ranges));
        foreach (string expression in new[] { "", "0", "6", "3-2", "1,", "1,,2", "2-", "1-2-3", "+1", "1\0", "1;2" })
        {
            Assert.Throws<ArgumentException>(() => PdfPageSelection.Parse(expression, 5));
        }
    }

    [Fact]
    public async Task ExtractedPagesKeepOnlySelectedTextAndCarrySourceTags()
    {
        var token = TestContext.Current.CancellationToken;
        using var temporary = new TemporaryLibrary();
        using var library = InboxLibrary.Open(temporary.Path);
        var mixed = await library.ImportAsync(Fixture(Mixed), token);
        var spanish = await library.ImportAsync(Fixture("Spanish Receipt.png"), token, OcrLanguage.Spanish);
        var source = await library.CombinePdfsAsync([spanish.Id, mixed.Id], "Source packet", cancellationToken: token);
        library.AddTag(source.Id, "Archive");
        library.SetReviewed(source.Id, reviewed: true);
        var sourceDate = new DateOnly(2024, 2, 29);
        library.SetDocumentDate(source.Id, sourceDate);
        string sourcePath = library.ResolvePdf(source);
        byte[] originalBytes = File.ReadAllBytes(sourcePath);
        var selection = PdfPageSelection.Parse("3,1,3", source.PageCount);

        var excerpt = await library.ExtractPdfPagesAsync(source.Id, selection, "Receipt pages", token);

        Assert.NotEqual(source.Id, excerpt.Id);
        Assert.Equal("Receipt pages", excerpt.Title);
        Assert.False(excerpt.Reviewed);
        Assert.Equal("Archive", Assert.Single(excerpt.Tags));
        Assert.Equal(sourceDate, excerpt.DocumentDate);
        Assert.Equal(2, excerpt.PageCount);
        using (var pdf = PdfDocument.Open(library.ResolvePdf(excerpt)))
        {
            Assert.Equal(2, pdf.NumberOfPages);
            Assert.Contains("pequeño", ContentOrderTextExtractor.GetText(pdf.GetPage(1)), StringComparison.OrdinalIgnoreCase);
            Assert.Contains(ReceiptKeyword, ContentOrderTextExtractor.GetText(pdf.GetPage(2)), StringComparison.OrdinalIgnoreCase);
        }
        Assert.DoesNotContain(LetterKeyword, library.ReadIndexedText(excerpt.Id), StringComparison.OrdinalIgnoreCase);
        Assert.Equal(2, library.FindFirstMatchingPage(excerpt, ReceiptKeyword, token));
        Assert.Contains(library.Find(new DocumentFilter(Query: "pequeño wrench tag:Archive dated:2024-02"), token).Items, item => item.Id == excerpt.Id);
        Assert.Equal(originalBytes, File.ReadAllBytes(sourcePath));
        Assert.True(library.Find(new DocumentFilter(Tag: "Archive"), token).Items.Single(item => item.Id == source.Id).Reviewed);
    }

    [Fact]
    public async Task ExtractPagesRejectsCancellationAndStaleSourcesBeforePublishing()
    {
        var token = TestContext.Current.CancellationToken;
        using var temporary = new TemporaryLibrary();
        using var library = InboxLibrary.Open(temporary.Path);
        var source = await library.ImportAsync(Fixture("Match Ledger.pdf"), token);
        var selection = PdfPageSelection.Parse("1-2", source.PageCount);
        var stalePageCount = PdfPageSelection.Parse("1-2", source.PageCount - 1);
        string path = library.ResolvePdf(source);
        byte[] before = File.ReadAllBytes(path);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            library.ExtractPdfPagesAsync(source.Id, selection, "Cancelled excerpt", new CancellationToken(true)));
        var changedPageCount = await Assert.ThrowsAsync<InvalidDataException>(() =>
            library.ExtractPdfPagesAsync(source.Id, stalePageCount, "Wrong page count", token));
        Assert.Contains("page count changed", changedPageCount.Message, StringComparison.OrdinalIgnoreCase);
        File.SetLastWriteTimeUtc(path, File.GetLastWriteTimeUtc(path).AddMinutes(5));
        await Assert.ThrowsAsync<IOException>(() => library.ExtractPdfPagesAsync(source.Id, selection, "Changed source", token));

        Assert.Equal(path, Assert.Single(Directory.EnumerateFiles(library.PdfDirectory, "*.pdf")));
        Assert.Equal(before, File.ReadAllBytes(path));
        Assert.Single(library.Find(new DocumentFilter(), token).Items);
        Assert.Empty(Directory.EnumerateDirectories(Path.Combine(temporary.Path, ".plico"), "import-*"));
    }
}
