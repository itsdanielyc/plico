using System.Collections.Immutable;
using NAPS2.Ocr;
using NAPS2.Scan;
using RapidOcrNet;
using SkiaSharp;
using OcrResult = NAPS2.Ocr.OcrResult;

namespace Plico.Core;

/// <summary>Adapts offline PP-OCRv5 word and line bounding boxes to the established PDF text-layer exporter.</summary>
internal sealed class RapidOcrEngine : IOcrEngine
{
    private static readonly Lazy<RapidOcr> Recognizer = new(CreateRecognizer);
    private static readonly SemaphoreSlim Inference = new(1);
    private static readonly RapidOcrOptions Recognition = RapidOcrOptions.Default with { ReturnWordBox = true };

    public async Task<OcrResult?> ProcessImage(ScanningContext scanningContext, string imagePath, OcrParams ocrParams,
        CancellationToken cancelToken)
    {
        cancelToken.ThrowIfCancellationRequested();
        using var image = SKBitmap.Decode(imagePath) ?? throw new InvalidDataException("RapidOCR could not open a staged page.");
        await Inference.WaitAsync(cancelToken).ConfigureAwait(false);
        try
        {
            var recognition = await Recognizer.Value.DetectAsync(image, Recognition, cancelToken).ConfigureAwait(false);
            return BuildResult(image, recognition.TextBlocks, ocrParams.LanguageCode ?? "eng");
        }
        finally
        {
            Inference.Release();
        }
    }

    private static RapidOcr CreateRecognizer()
    {
        var recognizer = new RapidOcr();
        try
        {
            recognizer.InitModels();
            return recognizer;
        }
        catch
        {
            recognizer.Dispose();
            throw;
        }
    }

    // O(blocks + words) time and space for the recognized bounding boxes.
    private static OcrResult BuildResult(SKBitmap image, IReadOnlyList<TextBlock> blocks, string language)
    {
        var lines = ImmutableList.CreateBuilder<OcrResultElement>();
        var words = ImmutableList.CreateBuilder<OcrResultElement>();
        foreach (var block in blocks)
        {
            if (string.IsNullOrWhiteSpace(block.Text) || block.BoxPoints.Length == 0) continue;
            var children = ImmutableList.CreateBuilder<OcrResultElement>();
            foreach (var word in block.WordResults ?? [])
            {
                if (!string.IsNullOrWhiteSpace(word.Text) && word.BoxPoints.Length != 0)
                    children.Add(Element(word.Text, word.BoxPoints, language, []));
            }
            if (children.Count == 0)
                children.Add(Element(block.Text, block.BoxPoints, language, []));
            var lineWords = children.ToImmutable();
            lines.Add(Element(block.Text, block.BoxPoints, language, lineWords));
            words.AddRange(lineWords);
        }
        return new OcrResult((0, 0, image.Width, image.Height), words.ToImmutable(), lines.ToImmutable());
    }

    private static OcrResultElement Element(string text, SKPointI[] points, string language, ImmutableList<OcrResultElement> children)
    {
        int left = points.Min(point => point.X);
        int top = points.Min(point => point.Y);
        int right = points.Max(point => point.X);
        int bottom = points.Max(point => point.Y);
        int width = Math.Max(1, right - left);
        int height = Math.Max(1, bottom - top);
        return new(text, language, false, (left, top, width, height), height, height, children);
    }
}
