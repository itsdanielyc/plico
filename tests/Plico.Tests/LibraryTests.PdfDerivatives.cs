using System.Text;
using Plico.Core;
using SkiaSharp;
using UglyToad.PdfPig;
using UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor;
using Xunit;

namespace Plico.Tests;

public sealed partial class LibraryTests
{
    [Fact]
    public async Task PdfDerivativesRemoveRedactedPixelsAndHiddenSourceData()
    {
        const string secret = "PRIVATE731";
        const string publicText = "PUBLIC RECORD";
        using var temporary = new TemporaryLibrary();
        using var library = InboxLibrary.Open(Directory.CreateDirectory(Path.Combine(temporary.Path, "Library")).FullName);
        string target = Directory.CreateDirectory(Path.Combine(temporary.Path, "Export")).FullName;
        string source = Path.Combine(temporary.Path, "Synthetic record.pdf");
        using (var writer = SKDocument.CreatePdf(source, new SKDocumentPdfMetadata { Subject = secret }))
        using (var font = new SKFont(SKTypeface.Default, 32))
        using (var paint = new SKPaint { Color = SKColors.Black, IsAntialias = true })
        {
            var canvas = writer!.BeginPage(612, 792);
            canvas.DrawText(secret, 80, 140, SKTextAlign.Left, font, paint);
            canvas.DrawText(publicText, 80, 360, SKTextAlign.Left, font, paint);
            writer.EndPage();
            writer.Close();
        }
        var token = TestContext.Current.CancellationToken;
        var document = await library.ImportAsync(source, token);
        var original = File.ReadAllBytes(library.ResolvePdf(document));

        string exported = await library.ExportRedactedPdfAsync(document.Id,
            [new PdfRedactionRegion(1, 0.10, 0.10, 0.57, 0.22)], target, OcrLanguage.English, token);

        using var redacted = PdfDocument.Open(exported);
        string text = ContentOrderTextExtractor.GetText(redacted.GetPage(1));
        Assert.DoesNotContain(secret, text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(secret, redacted.Information.Subject ?? "", StringComparison.OrdinalIgnoreCase);
        Assert.Contains(publicText, text, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(original, File.ReadAllBytes(library.ResolvePdf(document)));
        Assert.Equal(document.Id, Assert.Single(library.Find(new DocumentFilter(Query: secret), token).Items).Id);
        await Assert.ThrowsAsync<ArgumentException>(() => library.ExportRedactedPdfAsync(document.Id,
            [new PdfRedactionRegion(1, double.NaN, 0, 0.8, 1)], target, OcrLanguage.English, token));
    }

    [Fact]
    public async Task PdfDerivativesAddAVisibleSearchableStampWithoutEditingTheSource()
    {
        using var temporary = new TemporaryLibrary();
        using var library = InboxLibrary.Open(Directory.CreateDirectory(Path.Combine(temporary.Path, "Library")).FullName);
        string target = Directory.CreateDirectory(Path.Combine(temporary.Path, "Export")).FullName;
        var token = TestContext.Current.CancellationToken;
        var document = await library.ImportAsync(Fixture(Mixed), token);
        byte[] original = File.ReadAllBytes(library.ResolvePdf(document));

        string exported = await library.ExportStampedPdfAsync(document.Id, PdfPageSelection.Parse("2", 2),
            new PdfStamp("PAID", PdfStampPosition.BottomRight), target, token);

        using var stamped = PdfDocument.Open(exported);
        Assert.Equal(2, stamped.NumberOfPages);
        Assert.DoesNotContain("PAID", ContentOrderTextExtractor.GetText(stamped.GetPage(1)));
        Assert.Contains("PAID", ContentOrderTextExtractor.GetText(stamped.GetPage(2)));
        Assert.Contains(ReceiptKeyword, ContentOrderTextExtractor.GetText(stamped.GetPage(2)), StringComparison.OrdinalIgnoreCase);
        Assert.Equal(original, File.ReadAllBytes(library.ResolvePdf(document)));
    }

    [Fact]
    public async Task PdfDerivativesCompressUnfilteredStreamsAndKeepTheText()
    {
        using var temporary = new TemporaryLibrary();
        using var library = InboxLibrary.Open(Directory.CreateDirectory(Path.Combine(temporary.Path, "Library")).FullName);
        string target = Directory.CreateDirectory(Path.Combine(temporary.Path, "Export")).FullName;
        string source = Path.Combine(temporary.Path, "Uncompressed lease.pdf");
        using (var document = PdfSharpCore.Pdf.IO.PdfReader.Open(Fixture(Letter), PdfSharpCore.Pdf.IO.PdfDocumentOpenMode.Modify))
        {
            var padding = document.Pages[0].Contents.AppendContent();
            padding.CreateStream(Encoding.ASCII.GetBytes("\n%" + new string('x', 80_000) + "\n"));
            document.Options.NoCompression = true;
            document.Options.CompressContentStreams = false;
            document.Save(source);
        }
        Assert.True(new FileInfo(source).Length > 80_000);
        var token = TestContext.Current.CancellationToken;
        var imported = await library.ImportAsync(source, token);

        var compressed = await library.ExportCompressedPdfAsync(imported.Id, PdfCompressionMode.Lossless, target, token);

        Assert.True(compressed.OutputBytes < compressed.OriginalBytes,
            $"Trial output {compressed.OutputBytes} bytes, original {compressed.OriginalBytes} bytes.");
        Assert.NotNull(compressed.Location);
        using var pdf = PdfDocument.Open(compressed.Location);
        Assert.Contains(LetterKeyword, ContentOrderTextExtractor.GetText(pdf.GetPage(1)), StringComparison.OrdinalIgnoreCase);
        Assert.Equal(File.ReadAllBytes(source), File.ReadAllBytes(library.ResolvePdf(imported)));
    }
}
