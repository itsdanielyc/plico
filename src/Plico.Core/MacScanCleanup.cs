using NAPS2.Images;
using NAPS2.Images.Skia;
using NAPS2.Images.Transforms;
using NAPS2.Scan;
using SkiaSharp;

namespace Plico.Core;

/// <summary>Applies Plico's output-side image adjustments to pages delivered by Image Capture.</summary>
internal static class MacScanCleanup
{
    private const int BackSideRotation = 180;
    private const int SampleEdge = 256;
    private const int BytesPerPixel = 4;
    private const byte WhiteThreshold = 235;
    private const double MinimumInkFraction = 0.0008;
    private const int CropSafetyPixels = 5;
    private const int SkewSteps = 10;
    private const double SkewStepDegrees = 0.5;
    private const double MinimumSkewImprovement = 1.12;

    internal static ProcessedImage? Apply(ScanningContext context, ProcessedImage page, ScanOptions options, int pageNumber)
    {
        if (options.ExcludeBlankPages && Inspect(context, page).IsBlank)
        {
            page.Dispose();
            return null;
        }
        if (options.Brightness != 0) page = page.WithTransform(new BrightnessTransform(options.Brightness), disposeSelf: true);
        if (options.Contrast != 0) page = page.WithTransform(new TrueContrastTransform(options.Contrast), disposeSelf: true);
        if (options.FlipDuplexedPages && pageNumber % 2 == 0)
            page = page.WithTransform(new RotationTransform(BackSideRotation), disposeSelf: true);
        if (options.AutoDeskew)
        {
            double rotation = Inspect(context, page).DeskewAngle();
            if (rotation != 0) page = page.WithTransform(new RotationTransform(rotation), disposeSelf: true);
        }
        if (options.AutoCrop && Inspect(context, page).Crop() is { } crop)
            page = page.WithTransform(crop, disposeSelf: true);
        return page;
    }

    private static InkSample Inspect(ScanningContext context, ProcessedImage page)
    {
        using var rendered = (SkiaImage)context.ImageContext.Render(page);
        double scale = Math.Min(1, (double)SampleEdge / Math.Max(rendered.Width, rendered.Height));
        int width = Math.Max(1, (int)Math.Round(rendered.Width * scale));
        int height = Math.Max(1, (int)Math.Round(rendered.Height * scale));
        using var thumbnail = new SKBitmap(new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Opaque));
        using var source = SKImage.FromBitmap(rendered.Bitmap);
        using (var canvas = new SKCanvas(thumbnail))
        {
            canvas.DrawImage(source, SKRect.Create(width, height), new SKSamplingOptions(SKFilterMode.Linear));
        }
        return new InkSample(thumbnail, rendered.Width, rendered.Height);
    }

    private sealed class InkSample
    {
        private readonly SKSizeI _sample;
        private readonly SKSizeI _original;
        private readonly List<SKPointI> _ink = [];
        private readonly SKRectI _bounds;

        internal InkSample(SKBitmap thumbnail, int originalWidth, int originalHeight)
        {
            _sample = new(thumbnail.Width, thumbnail.Height);
            _original = new(originalWidth, originalHeight);
            int left = _sample.Width;
            int right = 0;
            int top = _sample.Height;
            int bottom = 0;
            ReadOnlySpan<byte> pixels = thumbnail.GetPixelSpan();
            for (int row = 0; row < _sample.Height; row++)
            {
                for (int column = 0; column < _sample.Width; column++)
                {
                    int pixel = row * thumbnail.RowBytes + column * BytesPerPixel;
                    if (pixels[pixel] >= WhiteThreshold && pixels[pixel + 1] >= WhiteThreshold &&
                        pixels[pixel + 2] >= WhiteThreshold) continue;
                    _ink.Add(new SKPointI(column, row));
                    left = Math.Min(left, column);
                    right = Math.Max(right, column + 1);
                    top = Math.Min(top, row);
                    bottom = Math.Max(bottom, row + 1);
                }
            }
            _bounds = new(left, top, right, bottom);
        }

        internal bool IsBlank => _ink.Count < Math.Max(1, _sample.Width * _sample.Height * MinimumInkFraction);

        internal CropTransform? Crop()
        {
            if (IsBlank) return null;
            int left = ScaleMargin(Math.Max(0, _bounds.Left - CropSafetyPixels), _original.Width, _sample.Width);
            int top = ScaleMargin(Math.Max(0, _bounds.Top - CropSafetyPixels), _original.Height, _sample.Height);
            int right = ScaleMargin(Math.Max(0, _sample.Width - _bounds.Right - CropSafetyPixels), _original.Width, _sample.Width);
            int bottom = ScaleMargin(Math.Max(0, _sample.Height - _bounds.Bottom - CropSafetyPixels), _original.Height, _sample.Height);
            return new CropTransform(left, right, top, bottom, _original.Width, _original.Height);
        }

        private static int ScaleMargin(int value, int original, int sample) => (int)((long)value * original / sample);

        // O(angles × ink pixels) time and O(sample height + width) workspace for row projection.
        internal double DeskewAngle()
        {
            if (IsBlank) return 0;
            int padding = _sample.Width;
            var projection = new int[_sample.Height + 2 * padding];
            long unrotated = Score(projection, padding, slope: 0);
            long best = unrotated;
            double angle = 0;
            for (int step = -SkewSteps; step <= SkewSteps; step++)
            {
                if (step == 0) continue;
                double candidate = step * SkewStepDegrees;
                long score = Score(projection, padding, Math.Tan(candidate * Math.PI / 180));
                if (score <= best) continue;
                best = score;
                angle = candidate;
            }
            return best > unrotated * MinimumSkewImprovement ? -angle : 0;
        }

        private long Score(int[] projection, int padding, double slope)
        {
            Array.Clear(projection);
            foreach (var pixel in _ink) projection[pixel.Y + padding - (int)Math.Round(pixel.X * slope)]++;
            long energy = 0;
            foreach (int count in projection) energy += (long)count * count;
            return energy;
        }
    }
}
