using System.Globalization;
using System.Text;
using PdfSharpCore.Pdf;
using PdfSharpCore.Pdf.IO;
using Plico.Core;
using SkiaSharp;
using UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor;
using Xunit;
using ReadablePdf = UglyToad.PdfPig.PdfDocument;

namespace Plico.Tests;

public sealed partial class LibraryTests
{
    [Fact]
    public async Task RotatedPdfKeepsItsPagesTextAndTagsWithRelativeTurns()
    {
        var token = TestContext.Current.CancellationToken;
        using var temporary = new TemporaryLibrary();
        using var library = InboxLibrary.Open(temporary.Path);
        var source = await library.ImportAsync(Fixture(Mixed), token);
        library.AddTag(source.Id, "Workshop");
        library.SetReviewed(source.Id, reviewed: true);
        var sourceDate = new DateOnly(2024, 2, 29);
        library.SetDocumentDate(source.Id, sourceDate);
        string path = library.ResolvePdf(source);
        byte[] originalBytes = File.ReadAllBytes(path);
        using var original = ReadablePdf.Open(originalBytes);
        var pages = PdfPageSelection.Parse("2", source.PageCount);

        var rotated = await library.RotatePdfPagesAsync(source.Id, pages, PdfPageRotation.Clockwise, "Turned receipt", token);

        Assert.NotEqual(source.Id, rotated.Id);
        Assert.Equal("Turned receipt", rotated.Title);
        Assert.Equal(source.PageCount, rotated.PageCount);
        Assert.Equal("Workshop", Assert.Single(rotated.Tags));
        Assert.Equal(sourceDate, rotated.DocumentDate);
        Assert.False(rotated.Reviewed);
        using (var turned = ReadablePdf.Open(library.ResolvePdf(rotated)))
        {
            Assert.Equal(original.GetPage(1).Rotation.Value, turned.GetPage(1).Rotation.Value);
            Assert.Equal((original.GetPage(2).Rotation.Value + (int)PdfPageRotation.Clockwise) % 360, turned.GetPage(2).Rotation.Value);
            Assert.Contains(LetterKeyword, ContentOrderTextExtractor.GetText(turned.GetPage(1)), StringComparison.OrdinalIgnoreCase);
            Assert.Contains(ReceiptKeyword, ContentOrderTextExtractor.GetText(turned.GetPage(2)), StringComparison.OrdinalIgnoreCase);
        }
        Assert.Equal(2, library.FindFirstMatchingPage(rotated, ReceiptKeyword, token));
        Assert.Contains(library.Find(new DocumentFilter(Query: "wrench tag:Workshop dated:2024-02"), token).Items, document => document.Id == rotated.Id);

        var restored = await library.RotatePdfPagesAsync(rotated.Id, pages, PdfPageRotation.Counterclockwise, "Restored receipt", token);
        Assert.Equal(sourceDate, restored.DocumentDate);
        using var upright = ReadablePdf.Open(library.ResolvePdf(restored));
        Assert.Equal(original.GetPage(2).Rotation.Value, upright.GetPage(2).Rotation.Value);
        Assert.Equal(originalBytes, File.ReadAllBytes(path));
        Assert.True(library.Find(new DocumentFilter(Tag: "Workshop"), token).Items.Single(document => document.Id == source.Id).Reviewed);
    }

    [Fact]
    public async Task InvalidRotationCancellationAndStalePageCountStopPublication()
    {
        var token = TestContext.Current.CancellationToken;
        using var temporary = new TemporaryLibrary();
        using var library = InboxLibrary.Open(temporary.Path);
        var source = await library.ImportAsync(Fixture(Letter), token);
        var pages = PdfPageSelection.Parse("1", source.PageCount);
        var stalePages = PdfPageSelection.Parse("1", source.PageCount + 1);
        string path = library.ResolvePdf(source);
        byte[] originalBytes = File.ReadAllBytes(path);

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            library.RotatePdfPagesAsync(source.Id, pages, (PdfPageRotation)45, "Invalid turn", token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            library.RotatePdfPagesAsync(source.Id, pages, PdfPageRotation.HalfTurn, "Cancelled turn", new CancellationToken(true)));
        var stale = await Assert.ThrowsAsync<InvalidDataException>(() =>
            library.RotatePdfPagesAsync(source.Id, stalePages, PdfPageRotation.Clockwise, "Outdated selection", token));
        Assert.Contains("page count changed", stale.Message, StringComparison.OrdinalIgnoreCase);

        Assert.Equal(path, Assert.Single(Directory.EnumerateFiles(library.PdfDirectory, "*.pdf")));
        Assert.Equal(originalBytes, File.ReadAllBytes(path));
        Assert.Single(library.Find(new DocumentFilter(), token).Items);
        Assert.Empty(Directory.EnumerateDirectories(Path.Combine(temporary.Path, ".plico"), "import-*"));
    }

    [Theory]
    [InlineData(120, 180, PdfPageRotation.HalfTurn, false)]
    [InlineData(120, 120, PdfPageRotation.Clockwise, true)]
    public async Task RotatedScanPreviewHonorsTurnsThatKeepTheSameDimensions(int width, int height, PdfPageRotation rotation,
        bool withOcrText)
    {
        const string orientationMarker = "ORIENTATION";
        const string scanSubject = "Preview orientation fixture";
        var token = TestContext.Current.CancellationToken;
        using var temporary = new TemporaryLibrary();
        using var library = InboxLibrary.Open(temporary.Path);
        WriteAsymmetricScan(Path.Combine(library.PdfDirectory, "Orientation mark.pdf"), width, height,
            scanSubject, withOcrText ? orientationMarker : null);
        library.Refresh(token);
        var source = Assert.Single(library.Find(new DocumentFilter(), token).Items);
        var rotated = await library.RotatePdfPagesAsync(source.Id, PdfPageSelection.Parse("1", 1), rotation, "Turned scan", token);
        Assert.Equal(withOcrText, rotated.HasText);
        using (var copy = PdfReader.Open(library.ResolvePdf(rotated), PdfDocumentOpenMode.Import))
        {
            Assert.Equal(scanSubject, copy.Info.Subject);
            Assert.Equal(scanSubject, Assert.Single(copy.Outlines).Title);
        }
        if (withOcrText) Assert.Equal(1, library.FindFirstMatchingPage(rotated, orientationMarker, token));

        using var originalPreview = await library.RenderPageAsync(source, 1, token);
        using var rotatedPreview = await library.RenderPageAsync(rotated, 1, token);
        using var original = SKBitmap.Decode(originalPreview);
        using var turned = SKBitmap.Decode(rotatedPreview);
        Assert.Equal(original.Width, turned.Width);
        Assert.Equal(original.Height, turned.Height);
        Assert.True(IsRed(original.GetPixel(original.Width / 4, original.Height / 4)));

        int expectedMarkerHeight = rotation == PdfPageRotation.HalfTurn ? turned.Height * 3 / 4 : turned.Height / 4;
        Assert.True(IsRed(turned.GetPixel(turned.Width * 3 / 4, expectedMarkerHeight)), "The red orientation mark stayed on the original corner.");
        Assert.False(IsRed(turned.GetPixel(turned.Width / 4, turned.Height / 4)));
    }

    private static bool IsRed(SKColor color) => color.Red > 200 && color.Green < 40 && color.Blue < 40;

    private static void WriteAsymmetricScan(string path, int width, int height, string subject, string? hiddenText)
    {
        using var raster = new SKBitmap(width, height);
        raster.Erase(SKColors.White);
        using var canvas = new SKCanvas(raster);
        using var marker = new SKPaint { Color = SKColors.Red };
        canvas.DrawRect(0, 0, width / 2f, height / 2f, marker);
        using var encoded = raster.Encode(SKEncodedImageFormat.Jpeg, 95);

        using var pdf = new PdfDocument();
        var page = pdf.AddPage();
        page.Width = width;
        page.Height = height;
        pdf.Info.Subject = subject;
        pdf.Outlines.Add(subject, page, opened: true);
        var image = new PdfDictionary(pdf);
        image.Elements.SetName("/Type", "/XObject");
        image.Elements.SetName("/Subtype", "/Image");
        image.Elements.SetInteger("/Width", width);
        image.Elements.SetInteger("/Height", height);
        image.Elements.SetInteger("/BitsPerComponent", 8);
        image.Elements.SetName("/ColorSpace", "/DeviceRGB");
        image.Elements.SetName("/Filter", "/DCTDecode");
        image.CreateStream(encoded.ToArray());
        pdf.Internals.AddObject(image);
        var resources = new PdfDictionary(pdf);
        var images = new PdfDictionary(pdf);
        images.Elements["/Scan"] = image.Reference;
        resources.Elements["/XObject"] = images;
        page.Elements["/Resources"] = resources;
        string paintPage = string.Create(CultureInfo.InvariantCulture, $"q {width} 0 0 {height} 0 0 cm /Scan Do Q");
        string textLayer = hiddenText is null ? "" : "\n" + AddInvisibleText(pdf, resources, hiddenText);
        page.Contents.AppendContent().CreateStream(Encoding.ASCII.GetBytes(paintPage + textLayer));
        pdf.Save(path);
    }

    private static string AddInvisibleText(PdfDocument pdf, PdfDictionary resources, string text)
    {
        var font = new PdfDictionary(pdf);
        font.Elements.SetName("/Type", "/Font");
        font.Elements.SetName("/Subtype", "/Type1");
        font.Elements.SetName("/BaseFont", "/Helvetica");
        pdf.Internals.AddObject(font);
        var fonts = new PdfDictionary(pdf);
        fonts.Elements["/TextLayer"] = font.Reference;
        resources.Elements["/Font"] = fonts;
        return $"BT /TextLayer 10 Tf 3 Tr 12 12 Td ({text}) Tj ET";
    }
}
