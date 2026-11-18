using System.Globalization;
using SkiaSharp;
using ZXing;
using ZXing.Common;
using ZXing.OneD;

namespace Plico.Core;

/// <summary>Writes printable barcode sheets for separating scan batches and identifying archived paper originals.</summary>
public static class ArchiveLabels
{
    private const string SerialPrefix = "ASN";
    private const string SeparatorCode = "PATCHT";
    private const float PointsPerInch = 72;
    private const float MillimetresPerInch = 25.4f;
    private const int LabelColumns = 3;
    private const float BarcodeInset = 8;
    private const float BarcodeHeight = 30;
    private const float IdentifierFontSize = 11;
    private const float SeparatorBarcodeHeight = 65;
    private const float SeparatorFontSize = 20;

    private sealed record Sheet(float Width, float Height, float LabelWidth, float LabelHeight, float ColumnGap, int Rows);

    internal static long? ReadBarcode(string? value) =>
        value is not null && value.StartsWith(SerialPrefix, StringComparison.Ordinal) &&
        long.TryParse(value.AsSpan(SerialPrefix.Length), NumberStyles.None, CultureInfo.InvariantCulture, out long serial) && serial > 0
            ? serial : null;

    /// <summary>Writes sequential ASN labels to a new PDF at their physical print dimensions.</summary>
    public static void WriteSerialSheet(string destination, ScanPaper paper, long startingNumber, int quantity,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(startingNumber);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(quantity);
        if (startingNumber > long.MaxValue - (quantity - 1L)) throw new ArgumentOutOfRangeException(nameof(quantity));
        var sheet = Layout(paper);
        WritePdf(destination, document => DrawLabels(document, sheet, startingNumber, quantity, cancellationToken), cancellationToken);
    }

    /// <summary>Writes a full-page PATCH-T separator for a scan feeder.</summary>
    public static void WriteSeparatorSheet(string destination, ScanPaper paper, CancellationToken cancellationToken = default)
    {
        var sheet = Layout(paper);
        WritePdf(destination, document =>
        {
            using var ink = new SKPaint { Color = SKColors.Black };
            using var font = new SKFont(SKTypeface.Default, SeparatorFontSize);
            var page = document.BeginPage(sheet.Width, sheet.Height);
            float barcodeWidth = sheet.Width / 2;
            float left = (sheet.Width - barcodeWidth) / 2;
            float top = sheet.Height / 3;
            var pattern = new Code39Writer().encode(SeparatorCode, BarcodeFormat.CODE_39, 1, 1);
            DrawBarcode(page, pattern, left, top, barcodeWidth, SeparatorBarcodeHeight, ink);
            page.DrawText("PATCH-T separator", sheet.Width / 2, top + SeparatorBarcodeHeight + 2 * SeparatorFontSize,
                SKTextAlign.Center, font, ink);
            document.EndPage();
        }, cancellationToken);
    }

    // O(labels × barcode modules) time. Each page is written before the next without holding label rasters.
    private static void DrawLabels(SKDocument document, Sheet sheet, long startingNumber, int quantity, CancellationToken cancellationToken)
    {
        using var font = new SKFont(SKTypeface.Default, IdentifierFontSize);
        using var ink = new SKPaint { Color = SKColors.Black };
        var encoder = new Code128Writer();
        int labelsPerPage = LabelColumns * sheet.Rows;
        float marginX = (sheet.Width - LabelColumns * sheet.LabelWidth - (LabelColumns - 1) * sheet.ColumnGap) / 2;
        float marginY = (sheet.Height - sheet.Rows * sheet.LabelHeight) / 2;
        SKCanvas? page = null;
        for (int index = 0; index < quantity; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            int slot = index % labelsPerPage;
            if (slot == 0)
            {
                if (page is not null) document.EndPage();
                page = document.BeginPage(sheet.Width, sheet.Height);
            }
            float left = marginX + slot % LabelColumns * (sheet.LabelWidth + sheet.ColumnGap);
            float top = marginY + slot / LabelColumns * sheet.LabelHeight;
            float barcodeTop = top + (sheet.LabelHeight - BarcodeHeight - IdentifierFontSize - BarcodeInset) / 2;
            string number = (startingNumber + index).ToString(CultureInfo.InvariantCulture);
            var pattern = encoder.encode(SerialPrefix + number, BarcodeFormat.CODE_128, 1, 1);
            DrawBarcode(page!, pattern, left + BarcodeInset, barcodeTop, sheet.LabelWidth - 2 * BarcodeInset, BarcodeHeight, ink);
            page!.DrawText($"{SerialPrefix} {number}", left + sheet.LabelWidth / 2, barcodeTop + BarcodeHeight + IdentifierFontSize + 3,
                SKTextAlign.Center, font, ink);
        }
        document.EndPage();
    }

    private static void DrawBarcode(SKCanvas page, BitMatrix pattern, float left, float top, float width, float height, SKPaint ink)
    {
        for (int column = 0; column < pattern.Width;)
        {
            int start = column;
            while (column < pattern.Width && pattern[column, 0]) column++;
            if (column == start)
            {
                column++;
                continue;
            }
            float barLeft = left + start * width / pattern.Width;
            float barRight = left + column * width / pattern.Width;
            page.DrawRect(barLeft, top, barRight - barLeft, height, ink);
        }
    }

    private static Sheet Layout(ScanPaper paper) => paper switch
    {
        ScanPaper.Letter => new Sheet(8.5f * PointsPerInch, 11 * PointsPerInch, 2.625f * PointsPerInch,
            PointsPerInch, 0.125f * PointsPerInch, 10),
        ScanPaper.A4 => new Sheet(ToPoints(210), ToPoints(297), ToPoints(63.5f), ToPoints(38.1f), ToPoints(2.5f), 7),
        _ => throw new ArgumentOutOfRangeException(nameof(paper))
    };

    private static float ToPoints(float millimetres) => millimetres * PointsPerInch / MillimetresPerInch;

    private static void WritePdf(string destination, Action<SKDocument> draw, CancellationToken cancellationToken)
    {
        string directory = Path.GetDirectoryName(Path.GetFullPath(destination))!;
        string staged = Path.Combine(directory, Path.GetRandomFileName());
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            using (var output = File.Open(staged, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            using (var document = SKDocument.CreatePdf(output) ?? throw new IOException("Plico could not create the label sheet."))
            {
                draw(document);
                document.Close();
            }
            cancellationToken.ThrowIfCancellationRequested();
            File.Move(staged, destination);
        }
        finally
        {
            File.Delete(staged);
        }
    }
}
