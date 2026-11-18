using Plico.Core;
using UglyToad.PdfPig;
using UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor;
using Xunit;

namespace Plico.Tests;

public sealed partial class LibraryTests
{
    private const string TiffScan = "Scanner pages.TIFF";

    [Fact]
    public async Task MultiPageTiffImportsEveryPageInOrderAndMakesItSearchable()
    {
        var token = TestContext.Current.CancellationToken;
        using var temporary = new TemporaryLibrary();
        using var library = InboxLibrary.Open(temporary.Path);
        string sourcePath = Fixture(TiffScan);
        byte[] source = File.ReadAllBytes(sourcePath);
        Assert.True(InboxLibrary.SupportsImport(sourcePath));
        Assert.True(InboxLibrary.SupportsImport("Scanner output.tif"));

        var imported = await library.ImportFileAsync(sourcePath, token);

        Assert.True(imported.Added);
        Assert.Equal(2, imported.Document.PageCount);
        using (var pdf = PdfDocument.Open(library.ResolvePdf(imported.Document)))
        {
            Assert.Equal(2, pdf.NumberOfPages);
            Assert.Contains(LetterKeyword, ContentOrderTextExtractor.GetText(pdf.GetPage(1)), StringComparison.OrdinalIgnoreCase);
            var receipt = pdf.GetPage(2);
            Assert.Contains(ReceiptKeyword, ContentOrderTextExtractor.GetText(receipt), StringComparison.OrdinalIgnoreCase);
            Assert.InRange((double)receipt.Width, 345, 347);
            Assert.InRange((double)receipt.Height, 633, 635);
        }
        Assert.Equal(2, library.FindFirstMatchingPage(imported.Document, ReceiptKeyword, token));
        Assert.Equal(source, File.ReadAllBytes(sourcePath));
        var repeat = await library.ImportFileAsync(sourcePath, token);
        Assert.False(repeat.Added);
        Assert.Equal(imported.Document.Id, repeat.Document.Id);
        Assert.Single(Directory.EnumerateFiles(library.PdfDirectory, "*.pdf"));
    }

    [Fact]
    public async Task TruncatedMultiPageTiffPublishesNoPartialPdf()
    {
        const int classicTiffDirectoryPointer = 4;
        const int classicTiffDirectoryEntryBytes = 12;
        var token = TestContext.Current.CancellationToken;
        using var temporary = new TemporaryLibrary();
        using var library = InboxLibrary.Open(temporary.Path);
        string source = Path.Combine(temporary.Path, "Incomplete scan.tif");
        File.Copy(Fixture(TiffScan), source);
        using (var stream = File.Open(source, FileMode.Open, FileAccess.ReadWrite))
        {
            // Keep the first page intact and truncate the little-endian fixture at its second directory.
            using var reader = new BinaryReader(stream);
            stream.Position = classicTiffDirectoryPointer;
            stream.Position = reader.ReadUInt32();
            long entryBytes = reader.ReadUInt16() * classicTiffDirectoryEntryBytes;
            stream.Seek(entryBytes, SeekOrigin.Current);
            long secondDirectory = reader.ReadUInt32();
            Assert.InRange(secondDirectory, stream.Position, stream.Length - 1);
            stream.SetLength(secondDirectory);
        }

        var error = await Assert.ThrowsAsync<InvalidDataException>(() => library.ImportFileAsync(source, token));

        Assert.Contains("next TIFF page", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(library.Find(new DocumentFilter(), token).Items);
        Assert.Empty(Directory.EnumerateFiles(library.PdfDirectory));
        Assert.Empty(Directory.EnumerateDirectories(Path.Combine(temporary.Path, ".plico"), "import-*"));
    }
}
