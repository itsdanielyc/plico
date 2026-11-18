using Plico.Core;
using UglyToad.PdfPig;
using UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor;
using Xunit;

namespace Plico.Tests;

public sealed partial class LibraryTests
{
    [Theory]
    [InlineData("Sideways receipt.JPG")]
    [InlineData("Mirrored receipt.jpeg")]
    public async Task JpegOrientationIsAppliedBeforeOcrAndPreservesPhysicalSize(string filename)
    {
        var token = TestContext.Current.CancellationToken;
        using var temporary = new TemporaryLibrary();
        using var library = InboxLibrary.Open(temporary.Path);
        string sourcePath = Fixture(filename);
        byte[] original = File.ReadAllBytes(sourcePath);

        var imported = await library.ImportFileAsync(sourcePath, token);

        Assert.True(imported.Added);
        Assert.Equal(1, imported.Document.PageCount);
        using (var pdf = PdfDocument.Open(library.ResolvePdf(imported.Document)))
        {
            var page = pdf.GetPage(1);
            Assert.InRange((double)page.Width, 431, 433);
            Assert.InRange((double)page.Height, 632, 635);
            Assert.Contains(ReceiptKeyword, ContentOrderTextExtractor.GetText(page), StringComparison.OrdinalIgnoreCase);
        }
        Assert.Equal(imported.Document.Id, Assert.Single(library.Find(new DocumentFilter(Query: ReceiptKeyword), token).Items).Id);
        Assert.Equal(original, File.ReadAllBytes(sourcePath));
        var repeated = await library.ImportFileAsync(sourcePath, token);
        Assert.False(repeated.Added);
        Assert.Equal(imported.Document.Id, repeated.Document.Id);
        Assert.Single(Directory.EnumerateFiles(library.PdfDirectory, "*.pdf"));
    }
}
