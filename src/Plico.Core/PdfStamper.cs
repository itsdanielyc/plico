using PdfSharpCore.Drawing;
using PdfSharpCore.Pdf;
using PdfSharpCore.Pdf.IO;
using SkiaSharp;

namespace Plico.Core;

/// <summary>Embeds vector stamp artwork while keeping the original page content and its text.</summary>
internal static class PdfStamper
{
    private const float StampWidth = 172;
    private const float StampHeight = 32;
    private const float StampFontSize = 17;
    private const float StampInset = 8;
    private const double MaximumMargin = 18;
    private const double MarginFraction = 0.06;
    private const double MaximumPageWidthFraction = 0.7;

    internal static void Write(string source, string output, PdfPageSelection selection, string label,
        PdfStampPosition position, CancellationToken token)
    {
        using var pdf = PdfReader.Open(source, PdfDocumentOpenMode.Modify);
        if (selection.SourcePageCount != pdf.PageCount) throw new InvalidDataException("The PDF page count changed.");
        using var stamp = CreateStamp(label);
        using var artwork = XPdfForm.FromStream(stamp);
        foreach (var range in selection.Ranges)
        {
            for (int index = range.FirstPage - 1; index < range.LastPage; index++)
            {
                token.ThrowIfCancellationRequested();
                DrawStamp(pdf.Pages[index], artwork, position);
            }
        }
        token.ThrowIfCancellationRequested();
        pdf.Save(output);
    }

    private static MemoryStream CreateStamp(string label)
    {
        var stream = new MemoryStream();
        try
        {
            using var pdf = SKDocument.CreatePdf(stream) ?? throw new IOException("Could not create the PDF stamp.");
            using var font = new SKFont(SKTypeface.Default, StampFontSize);
            using var ink = new SKPaint { IsAntialias = true, Color = new SKColor(25, 78, 120), StrokeWidth = 1.75f };
            var canvas = pdf.BeginPage(StampWidth, StampHeight);
            ink.Style = SKPaintStyle.Stroke;
            canvas.DrawRoundRect(new SKRect(1, 1, StampWidth - 1, StampHeight - 1), 4, 4, ink);
            ink.Style = SKPaintStyle.Fill;
            float textWidth = font.MeasureText(label);
            if (textWidth > StampWidth - 2 * StampInset) font.Size *= (StampWidth - 2 * StampInset) / textWidth;
            var metrics = font.Metrics;
            float baseline = (StampHeight - metrics.Ascent - metrics.Descent) / 2;
            canvas.DrawText(label, StampWidth / 2, baseline, SKTextAlign.Center, font, ink);
            pdf.EndPage();
            pdf.Close();
            stream.Position = 0;
            return stream;
        }
        catch
        {
            stream.Dispose();
            throw;
        }
    }

    private static void DrawStamp(PdfPage page, XPdfForm artwork, PdfStampPosition position)
    {
        using var graphics = XGraphics.FromPdfPage(page, XGraphicsPdfPageOptions.Append);
        double width = page.Width.Point;
        double height = page.Height.Point;
        int rotation = (page.Rotate % 360 + 360) % 360;
        switch (rotation)
        {
            case 90:
                graphics.TranslateTransform(0, height);
                graphics.RotateTransform(-90);
                (width, height) = (height, width);
                break;
            case 180:
                graphics.TranslateTransform(width, height);
                graphics.RotateTransform(180);
                break;
            case 270:
                graphics.TranslateTransform(width, 0);
                graphics.RotateTransform(90);
                (width, height) = (height, width);
                break;
        }
        double margin = Math.Min(MaximumMargin, Math.Min(width, height) * MarginFraction);
        double artworkWidth = Math.Min(StampWidth, width * MaximumPageWidthFraction);
        double artworkHeight = Math.Min(artworkWidth * StampHeight / StampWidth, height - 2 * margin);
        bool left = position is PdfStampPosition.TopLeft or PdfStampPosition.BottomLeft;
        bool top = position is PdfStampPosition.TopLeft or PdfStampPosition.TopRight;
        graphics.DrawImage(artwork, left ? margin : width - artworkWidth - margin,
            top ? margin : height - artworkHeight - margin, artworkWidth, artworkHeight);
    }
}
