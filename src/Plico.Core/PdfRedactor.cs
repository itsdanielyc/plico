using NAPS2.Images;
using NAPS2.Images.Skia;
using NAPS2.ImportExport;
using NAPS2.Pdf;
using PdfSharpCore.Drawing;
using PdfSharpCore.Pdf;
using PdfSharpCore.Pdf.IO;
using SkiaSharp;

namespace Plico.Core;

/// <summary>Builds a PDF from the visible page pixels after applying all redaction rectangles.</summary>
internal static class PdfRedactor
{
    private const float PointsPerInch = 72;
    private const int PreviewSize = 1050;
    private const int DefaultRasterDpi = 300;
    private const long MaximumRasterPixels = 48_000_000;

    // O(page pixels + OCR) time, with one decoded raster at a time and temp files for the PDF streams.
    internal static async Task WriteAsync(DocumentProcessor processor, string input, string output, int expectedPages,
        IReadOnlyList<PdfRedactionRegion> regions, OcrLanguage language, CancellationToken token)
    {
        string stage = Path.GetDirectoryName(output)!;
        string renderSource = Path.Combine(stage, "render-source.pdf");
        using (var visiblePdf = PdfReader.Open(input, PdfDocumentOpenMode.Modify))
        {
            if (visiblePdf.PageCount != expectedPages) throw new InvalidDataException("The PDF page count changed.");
            foreach (var page in visiblePdf.Pages)
            {
                token.ThrowIfCancellationRequested();
                DrawRasterBackground(page);
            }
            visiblePdf.Save(renderSource);
        }

        string pixelsOnly = Path.Combine(stage, "redacted-pixels.pdf");
        using var context = DocumentProcessor.CreateCaptureContext(stage);
        var marks = regions.ToLookup(region => region.PageNumber);
        using (var writer = SKDocument.CreatePdf(pixelsOnly) ?? throw new IOException("Could not create a redacted PDF."))
        using (var ink = new SKPaint { Color = SKColors.Black, IsAntialias = false })
        {
            int pageNumber = 0;
            await foreach (var page in new PdfImporter(context).Import(renderSource, progress: token)
                               .WithCancellation(token).ConfigureAwait(false))
            {
                using (page)
                using (var image = (SkiaImage)context.ImageContext.Render(page))
                {
                    ++pageNumber;
                    RedactPixels(image.Bitmap, marks[pageNumber], ink);
                    float width = image.Width * PointsPerInch / image.HorizontalResolution;
                    float height = image.Height * PointsPerInch / image.VerticalResolution;
                    var canvas = writer.BeginPage(width, height);
                    canvas.DrawBitmap(image.Bitmap, new SKRect(0, 0, width, height), new SKSamplingOptions(SKFilterMode.Nearest));
                    writer.EndPage();
                }
            }
            if (pageNumber != expectedPages) throw new InvalidDataException("The redacted PDF is missing pages.");
            writer.Close();
        }
        await processor.ReprocessPdfAsync(pixelsOnly, output, language, token).ConfigureAwait(false);
    }

    internal static async Task<MemoryStream> RenderPreviewAsync(string source, int pageNumber, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        using var sourcePdf = PdfReader.Open(source, PdfDocumentOpenMode.Import);
        using var pagePdf = new PdfDocument { Version = sourcePdf.Version };
        pagePdf.Pages.InsertRange(0, sourcePdf, pageNumber - 1, 1, AnnotationCopyingType.DeepCopy);
        DrawRasterBackground(pagePdf.Pages[0]);
        using var pageStream = new MemoryStream();
        pagePdf.Save(pageStream, closeStream: false);
        pageStream.Position = 0;
        using var context = new NAPS2.Scan.ScanningContext(new SkiaImageContext());
        MemoryStream? preview = null;
        try
        {
            await foreach (var page in new PdfImporter(context).Import(pageStream, new ImportParams { ThumbnailSize = PreviewSize }, token)
                               .WithCancellation(token).ConfigureAwait(false))
            {
                using (page) preview ??= page.PostProcessingData.Thumbnail?.SaveToMemoryStream(ImageFileFormat.Png);
            }
            return preview ?? throw new InvalidDataException("The PDF page could not be previewed for redaction.");
        }
        catch
        {
            preview?.Dispose();
            throw;
        }
    }

    private static void DrawRasterBackground(PdfPage page)
    {
        double width = page.Width.Point;
        double height = page.Height.Point;
        double pixels = width / PointsPerInch * height / PointsPerInch * DefaultRasterDpi * DefaultRasterDpi;
        if (!double.IsFinite(pixels) || width <= 0 || height <= 0 || pixels > MaximumRasterPixels)
            throw new InvalidDataException("This page is too large to rasterize for redaction.");
        // A vector backdrop prevents the PDF importer from substituting an embedded image for the visible composition.
        using var graphics = XGraphics.FromPdfPage(page, XGraphicsPdfPageOptions.Prepend);
        graphics.DrawRectangle(XBrushes.White, 0, 0, width, height);
    }

    private static void RedactPixels(SKBitmap bitmap, IEnumerable<PdfRedactionRegion> regions, SKPaint ink)
    {
        using var canvas = new SKCanvas(bitmap);
        foreach (var region in regions)
        {
            var box = new SKRect(
                (float)Math.Floor(region.Left * bitmap.Width),
                (float)Math.Floor(region.Top * bitmap.Height),
                (float)Math.Ceiling(region.Right * bitmap.Width),
                (float)Math.Ceiling(region.Bottom * bitmap.Height));
            canvas.DrawRect(box, ink);
        }
        canvas.Flush();
    }
}
