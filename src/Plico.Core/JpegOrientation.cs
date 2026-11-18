using NAPS2.Images;
using NAPS2.Images.Skia;
using NAPS2.ImportExport;
using SkiaSharp;

namespace Plico.Core;

/// <summary>Applies a JPEG's EXIF orientation before the image is embedded or sent to OCR.</summary>
internal static class JpegOrientation
{
    private const byte JpegMarkerPrefix = 0xff;
    private const byte JpegStartOfImage = 0xd8;
    private const int NormalizedJpegQuality = 95;
    private const string NormalizedFilename = "upright-scan.jpg";

    /// <summary>Returns the staged image path, with corrected pixels and resolution when orientation requires it.</summary>
    internal static string NormalizeIfNeeded(string sourcePath, string stagingDirectory, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var orientation = ReadOrientation(sourcePath);
        if (orientation is < SKEncodedOrigin.TopRight or > SKEncodedOrigin.LeftBottom) return sourcePath;

        cancellationToken.ThrowIfCancellationRequested();
        using var source = (SkiaImage)new SkiaImageContext().Load(sourcePath);
        cancellationToken.ThrowIfCancellationRequested();
        using var upright = DrawUpright(source, orientation);
        cancellationToken.ThrowIfCancellationRequested();

        string normalizedPath = Path.Combine(stagingDirectory, NormalizedFilename);
        using var output = File.Open(normalizedPath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        upright.Save(output, ImageFileFormat.Jpeg, new ImageSaveOptions { Quality = NormalizedJpegQuality });
        cancellationToken.ThrowIfCancellationRequested();
        return normalizedPath;
    }

    private static SKEncodedOrigin ReadOrientation(string path)
    {
        using var input = File.OpenRead(path);
        if (input.ReadByte() != JpegMarkerPrefix || input.ReadByte() != JpegStartOfImage) return SKEncodedOrigin.TopLeft;
        input.Position = 0;
        using var codec = SKCodec.Create(input);
        return codec?.EncodedOrigin ?? SKEncodedOrigin.TopLeft;
    }

    // O(pixels) time and O(pixels) space. Swap the DPI axes with the raster to preserve physical page size.
    private static SkiaImage DrawUpright(SkiaImage source, SKEncodedOrigin orientation)
    {
        bool transposed = orientation is SKEncodedOrigin.LeftTop or SKEncodedOrigin.RightTop or
            SKEncodedOrigin.RightBottom or SKEncodedOrigin.LeftBottom;
        int width = transposed ? source.Height : source.Width;
        int height = transposed ? source.Width : source.Height;
        var info = new SKImageInfo(width, height, SKColorType.Rgb888x, SKAlphaType.Opaque);
        var bitmap = new SKBitmap();
        try
        {
            if (!bitmap.TryAllocPixels(info)) throw new InvalidDataException("Plico could not allocate the upright photo.");
            source.Bitmap.SetImmutable();
            using var image = SKImage.FromBitmap(source.Bitmap) ?? throw new InvalidDataException("Plico could not read the photo pixels.");
            using (var canvas = new SKCanvas(bitmap))
            {
                var transform = UprightTransform(orientation, source.Width, source.Height);
                canvas.SetMatrix(in transform);
                canvas.DrawImage(image, 0, 0, default(SKSamplingOptions), null);
            }
            var upright = new SkiaImage(bitmap);
            upright.SetResolution(transposed ? source.VerticalResolution : source.HorizontalResolution,
                transposed ? source.HorizontalResolution : source.VerticalResolution);
            return upright;
        }
        catch
        {
            bitmap.Dispose();
            throw;
        }
    }

    // Map encoded pixel edges to upright edges with integer translations so the draw needs no interpolation.
    private static SKMatrix UprightTransform(SKEncodedOrigin orientation, int width, int height) => orientation switch
    {
        SKEncodedOrigin.TopRight => new(-1, 0, width, 0, 1, 0, 0, 0, 1),
        SKEncodedOrigin.BottomRight => new(-1, 0, width, 0, -1, height, 0, 0, 1),
        SKEncodedOrigin.BottomLeft => new(1, 0, 0, 0, -1, height, 0, 0, 1),
        SKEncodedOrigin.LeftTop => new(0, 1, 0, 1, 0, 0, 0, 0, 1),
        SKEncodedOrigin.RightTop => new(0, -1, height, 1, 0, 0, 0, 0, 1),
        SKEncodedOrigin.RightBottom => new(0, -1, height, -1, 0, width, 0, 0, 1),
        SKEncodedOrigin.LeftBottom => new(0, 1, 0, -1, 0, width, 0, 0, 1),
        _ => SKMatrix.CreateIdentity()
    };
}
