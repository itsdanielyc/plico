using Sdcb.SimdCodecs.Heif;
using SkiaSharp;

namespace Plico.Core;

internal static class HeifPreparation
{
    /// <summary>Decodes the display-oriented primary still image for the usual image OCR pipeline.</summary>
    internal static string ToPng(string source, string stage, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            using var decoded = HeifDecoder.Decode(File.ReadAllBytes(source));
            cancellationToken.ThrowIfCancellationRequested();
            var info = new SKImageInfo(decoded.Width, decoded.Height, SKColorType.Rgba8888, SKAlphaType.Opaque);
            using var pixels = SKData.CreateCopy(decoded.Rgba);
            using var image = SKImage.FromPixels(info, pixels, info.RowBytes)
                ?? throw new InvalidDataException("The HEIC image has no usable pixels.");
            using var png = image.Encode(SKEncodedImageFormat.Png, quality: 100)
                ?? throw new InvalidDataException("Plico could not prepare the HEIC image.");
            string output = Path.Combine(stage, "phone-image.png");
            using var file = File.Create(output);
            png.SaveTo(file);
            return output;
        }
        catch (Exception error) when (error is InvalidDataException or NotSupportedException or ArgumentException)
        {
            throw new InvalidDataException("Plico could not read this HEIC still image. Export a JPEG or PDF and try again.", error);
        }
    }
}
