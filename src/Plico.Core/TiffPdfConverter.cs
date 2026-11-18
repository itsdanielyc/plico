using System.Buffers;
using System.Runtime.InteropServices;
using BitMiracle.LibTiff.Classic;
using SkiaSharp;

namespace Plico.Core;

/// <summary>Normalizes TIFF directories into PDF pages that the existing OCR pipeline can import.</summary>
internal static class TiffPdfConverter
{
    private const float PointsPerInch = 72;
    private const float CentimetersPerInch = 2.54f;
    private const float DefaultDpi = 96;
    private const int MaximumRasterPixels = 160_000_000;

    /// <summary>Writes a complete image-only PDF and returns the number of TIFF pages it contains.</summary>
    internal static int Convert(string sourcePath, string outputPath, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var source = Tiff.Open(sourcePath, "r") ?? throw new InvalidDataException("Plico could not open the TIFF scan.");
        using var output = File.Open(outputPath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        using var pdf = SKDocument.CreatePdf(output) ?? throw new IOException("Plico could not create a PDF for the TIFF scan.");
        try
        {
            int pages = AppendPages(source, pdf, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            pdf.Close();
            return pages;
        }
        catch
        {
            // Staging can be removed immediately when the writer stops without finalizing a partial document.
            pdf.Abort();
            throw;
        }
    }

    // O(decoded pixels + pages) time and O(largest raster + page offsets) scratch space, plus the PDF writer.
    private static int AppendPages(Tiff source, SKDocument pdf, CancellationToken cancellationToken)
    {
        var directoryOffsets = new HashSet<long>();
        int pages = 0;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!directoryOffsets.Add(source.CurrentDirOffset()))
            {
                throw new InvalidDataException("The TIFF scan repeats a page directory.");
            }
            AppendPage(source, pdf, cancellationToken);
            pages++;
            if (source.LastDirectory()) break;
            if (!source.ReadDirectory()) throw new InvalidDataException("Plico could not read the next TIFF page.");
        }
        return pages;
    }

    private static void AppendPage(Tiff source, SKDocument pdf, CancellationToken cancellationToken)
    {
        int width = source.GetField(TiffTag.IMAGEWIDTH)?[0].ToInt() ?? 0;
        int height = source.GetField(TiffTag.IMAGELENGTH)?[0].ToInt() ?? 0;
        if (width <= 0 || height <= 0 || (long)width * height > MaximumRasterPixels)
        {
            throw new InvalidDataException("The TIFF page dimensions exceed the raster size that Plico can decode.");
        }

        var orientation = (Orientation)(source.GetFieldDefaulted(TiffTag.ORIENTATION)?[0].ToShort() ?? (short)Orientation.TOPLEFT);
        var unit = (ResUnit)(source.GetFieldDefaulted(TiffTag.RESOLUTIONUNIT)?[0].ToShort() ?? (short)ResUnit.INCH);
        float horizontalScale = PointsPerInch / ReadDpi(source, TiffTag.XRESOLUTION, unit);
        float verticalScale = PointsPerInch / ReadDpi(source, TiffTag.YRESOLUTION, unit);
        bool transpose = orientation is Orientation.LEFTTOP or Orientation.RIGHTTOP or Orientation.RIGHTBOT or Orientation.LEFTBOT;
        float pageWidth = transpose ? height * verticalScale : width * horizontalScale;
        float pageHeight = transpose ? width * horizontalScale : height * verticalScale;
        if (!float.IsFinite(pageWidth) || !float.IsFinite(pageHeight) || pageWidth <= 0 || pageHeight <= 0)
        {
            throw new InvalidDataException("The TIFF page resolution does not give a valid PDF page size.");
        }

        var info = new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul);
        using var bitmap = new SKBitmap();
        if (!bitmap.TryAllocPixels(info, info.RowBytes)) throw new InvalidDataException("Plico could not allocate the TIFF page raster.");
        DecodePixels(source, bitmap, orientation, cancellationToken);
        bitmap.SetImmutable();
        using var image = SKImage.FromBitmap(bitmap) ?? throw new InvalidDataException("Plico could not render the TIFF page.");

        var canvas = pdf.BeginPage(pageWidth, pageHeight) ?? throw new InvalidDataException("Plico could not size the TIFF page in the PDF.");
        var transform = transpose
            ? TransposePage(orientation, horizontalScale, verticalScale, pageWidth, pageHeight)
            : SKMatrix.CreateScale(horizontalScale, verticalScale);
        canvas.SetMatrix(in transform);
        canvas.DrawImage(image, 0, 0, default(SKSamplingOptions), null);
        pdf.EndPage();
    }

    private static void DecodePixels(Tiff source, SKBitmap bitmap, Orientation orientation, CancellationToken cancellationToken)
    {
        int count = bitmap.Width * bitmap.Height;
        var raster = ArrayPool<int>.Shared.Rent(count);
        try
        {
            // LibTiff applies flips for the four corners. Transposed pages keep their raster order for the PDF transform.
            var origin = orientation switch
            {
                Orientation.RIGHTTOP => Orientation.TOPRIGHT,
                Orientation.RIGHTBOT => Orientation.BOTRIGHT,
                Orientation.LEFTBOT => Orientation.BOTLEFT,
                _ => Orientation.TOPLEFT
            };
            if (!source.ReadRGBAImageOriented(bitmap.Width, bitmap.Height, raster, origin, stopOnError: true))
            {
                throw new InvalidDataException("Plico could not decode the TIFF page.");
            }
            cancellationToken.ThrowIfCancellationRequested();
            // LibTiff's packed ABGR integers have RGBA bytes on the supported little-endian runtimes.
            Marshal.Copy(raster, 0, bitmap.GetPixels(), count);
        }
        finally
        {
            ArrayPool<int>.Shared.Return(raster, clearArray: true);
        }
    }

    private static SKMatrix TransposePage(Orientation orientation, float horizontalScale, float verticalScale,
        float pageWidth, float pageHeight)
    {
        bool reverseX = orientation is Orientation.RIGHTTOP or Orientation.RIGHTBOT;
        bool reverseY = orientation is Orientation.RIGHTBOT or Orientation.LEFTBOT;
        return new SKMatrix(0, reverseX ? -verticalScale : verticalScale, reverseX ? pageWidth : 0,
            reverseY ? -horizontalScale : horizontalScale, 0, reverseY ? pageHeight : 0, 0, 0, 1);
    }

    private static float ReadDpi(Tiff source, TiffTag resolutionTag, ResUnit unit)
    {
        if (unit is not (ResUnit.INCH or ResUnit.CENTIMETER)) return DefaultDpi;
        float value = source.GetField(resolutionTag)?[0].ToFloat() ?? 0;
        float dpi = unit == ResUnit.CENTIMETER ? value * CentimetersPerInch : value;
        return float.IsFinite(dpi) && dpi > 0 ? dpi : DefaultDpi;
    }
}
