using Plico.Core;
using System.Runtime.CompilerServices;
using NAPS2.Images;
using NAPS2.ImportExport;
using NAPS2.Scan;
using UglyToad.PdfPig;
using UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor;
using Xunit;

namespace Plico.Tests;

public sealed class LibraryTests
{
    private const string Receipt = "Hardware Store Receipt.png";
    private const string Letter = "Lease Renewal.pdf";
    private const string Mixed = "Mixed Pages.pdf";
    private const string ReceiptKeyword = "WRENCH470";
    private const string LetterKeyword = "NORTHSTAR";

    [Fact]
    public async Task DigitalPdfStaysIntactAndSearchable()
    {
        using var temporary = new TemporaryLibrary();
        using var library = InboxLibrary.Open(temporary.Path);
        var imported = await library.ImportAsync(Fixture(Letter), TestContext.Current.CancellationToken);

        Assert.Equal(File.ReadAllBytes(Fixture(Letter)), File.ReadAllBytes(library.ResolvePdf(imported)));
        Assert.Equal(imported.Id, Assert.Single(library.Find(new DocumentFilter(Query: LetterKeyword)).Items).Id);
    }

    [Fact]
    public async Task ImageImportEmbedsTextInAnOrdinaryPdf()
    {
        using var temporary = new TemporaryLibrary();
        using var library = InboxLibrary.Open(temporary.Path);
        var imported = await library.ImportAsync(Fixture(Receipt), TestContext.Current.CancellationToken);
        using var pdf = PdfDocument.Open(library.ResolvePdf(imported));

        Assert.Contains(ReceiptKeyword, ContentOrderTextExtractor.GetText(pdf.GetPage(1)), StringComparison.OrdinalIgnoreCase);
        Assert.Equal(imported.Id, Assert.Single(library.Find(new DocumentFilter(Query: ReceiptKeyword)).Items).Id);
        Assert.True(File.Exists(Fixture(Receipt)));
    }

    [Fact]
    public async Task MixedPdfRetainsDigitalTextAndAddsOcrOnImagePage()
    {
        using var temporary = new TemporaryLibrary();
        using var library = InboxLibrary.Open(temporary.Path);
        var imported = await library.ImportAsync(Fixture(Mixed), TestContext.Current.CancellationToken);
        using var pdf = PdfDocument.Open(library.ResolvePdf(imported));

        Assert.Equal(2, pdf.NumberOfPages);
        Assert.Contains(LetterKeyword, ContentOrderTextExtractor.GetText(pdf.GetPage(1)), StringComparison.OrdinalIgnoreCase);
        Assert.Contains(ReceiptKeyword, ContentOrderTextExtractor.GetText(pdf.GetPage(2)), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task MalformedPdfDoesNotPublishAFileOrRow()
    {
        using var temporary = new TemporaryLibrary();
        string source = System.IO.Path.Combine(temporary.Path, "Corrupt.pdf");
        await File.WriteAllTextAsync(source, "A synthetic invalid PDF.", TestContext.Current.CancellationToken);
        using var library = InboxLibrary.Open(temporary.Path);

        await Assert.ThrowsAsync<InvalidDataException>(() => library.ImportAsync(source, TestContext.Current.CancellationToken));
        Assert.Empty(library.Find(new DocumentFilter()).Items);
        Assert.Empty(Directory.EnumerateFiles(library.PdfDirectory));
        Assert.True(File.Exists(source));
    }

    [Fact]
    public async Task TagsAndReviewStateSurviveMovingTheLibrary()
    {
        using var temporary = new TemporaryLibrary();
        string original = Directory.CreateDirectory(Path.Combine(temporary.Path, "Original")).FullName;
        long documentId;
        using (var library = InboxLibrary.Open(original))
        {
            var document = await library.ImportAsync(Fixture(Letter), TestContext.Current.CancellationToken);
            documentId = document.Id;
            library.AddTag(documentId, "  Réntal  ");
            library.AddTag(documentId, "RÉNTAL");
            library.SetReviewed(documentId, true);
        }

        string moved = Path.Combine(temporary.Path, "Moved");
        Directory.Move(original, moved);
        using var reopened = InboxLibrary.Open(moved);
        Assert.Empty(reopened.Find(new DocumentFilter(InboxOnly: true)).Items);
        var matching = Assert.Single(reopened.Find(new DocumentFilter(Query: "norths*", Tag: "réntal")).Items);
        Assert.Equal(documentId, matching.Id);
        Assert.Equal("Réntal", Assert.Single(matching.Tags));
        Assert.True(File.Exists(reopened.ResolvePdf(matching)));
    }

    [Fact]
    public void RefreshRecoversAnUnindexedFile()
    {
        using var temporary = new TemporaryLibrary();
        using var library = InboxLibrary.Open(temporary.Path);
        string nested = Directory.CreateDirectory(Path.Combine(library.PdfDirectory, "Property")).FullName;
        File.Copy(Fixture(Letter), Path.Combine(nested, "A copy.pdf"));

        var refreshed = library.Refresh(TestContext.Current.CancellationToken);

        Assert.Equal(1, refreshed.Indexed);
        Assert.Empty(refreshed.Warnings);
        Assert.Single(library.Find(new DocumentFilter(Query: LetterKeyword)).Items);
        Assert.Equal(0, library.Refresh(TestContext.Current.CancellationToken).Indexed);
    }

    [Fact]
    public async Task MultipageScanUsesTheSameSearchableOutputAsImport()
    {
        using var temporary = new TemporaryLibrary();
        using var library = InboxLibrary.Open(temporary.Path);
        var document = await library.StoreScanAsync("Feeder job", ReadCapturedPages, TestContext.Current.CancellationToken);
        using var pdf = PdfDocument.Open(library.ResolvePdf(document));

        Assert.Equal(2, document.PageCount);
        Assert.All(pdf.GetPages(), page =>
            Assert.Contains(ReceiptKeyword, ContentOrderTextExtractor.GetText(page), StringComparison.OrdinalIgnoreCase));
        Assert.Equal(document.Id, Assert.Single(library.Find(new DocumentFilter(Query: ReceiptKeyword)).Items).Id);
    }

    private static async IAsyncEnumerable<ProcessedImage> ReadCapturedPages(
        ScanningContext context, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var capturedFiles = new[] { Fixture(Receipt), Fixture(Receipt) };
        var importer = new ImageImporter(context);
        foreach (var capturedFile in capturedFiles)
        {
            await foreach (var image in importer.Import(capturedFile, progress: cancellationToken))
            {
                yield return image;
            }
        }
    }

    private static string Fixture(string name) => Path.Combine(AppContext.BaseDirectory, "fixtures", name);
}

internal sealed class TemporaryLibrary : IDisposable
{
    private readonly DirectoryInfo _directory = Directory.CreateTempSubdirectory("plico-test-");
    public string Path => _directory.FullName;
    public void Dispose() => _directory.Delete(recursive: true);
}
