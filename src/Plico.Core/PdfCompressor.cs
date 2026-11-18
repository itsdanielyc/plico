using System.IO.Compression;
using PdfSharpCore.Pdf;
using PdfSharpCore.Pdf.IO;
using SkiaSharp;

namespace Plico.Core;

/// <summary>Optimizes safe PDF streams while keeping the existing text and vector objects.</summary>
internal static class PdfCompressor
{
    private const int MinimumStreamBytes = 256;
    private const int MinimumFilterSavings = 32;
    private const int SmallerScanMaxSide = 1800;
    private const int SmallerScanQuality = 65;
    private const long MaximumDecodedImagePixels = 32_000_000;
    private const string Filter = "/Filter";
    private const string Width = "/Width";
    private const string Height = "/Height";

    // O(PDF bytes + decoded pixels) time and one decoded image plus the loaded PDF object graph.
    internal static void Write(string source, string output, PdfCompressionMode mode, CancellationToken token)
    {
        using var pdf = PdfReader.Open(source, PdfDocumentOpenMode.Modify);
        foreach (var item in pdf.Internals.GetAllObjects())
        {
            token.ThrowIfCancellationRequested();
            if (item is not PdfDictionary { Stream.Length: >= MinimumStreamBytes } dictionary ||
                dictionary.Elements.ContainsKey("/F") || dictionary.Elements.ContainsKey("/DecodeParms")) continue;

            if (!dictionary.Elements.ContainsKey(Filter))
                CompressUnfilteredStream(dictionary);
            else if (mode == PdfCompressionMode.SmallerScans)
                OptimizeJpeg(dictionary);
        }
        token.ThrowIfCancellationRequested();
        pdf.Save(output);
    }

    private static void CompressUnfilteredStream(PdfDictionary dictionary)
    {
        using var compressed = new MemoryStream();
        using (var deflate = new ZLibStream(compressed, CompressionLevel.SmallestSize, leaveOpen: true))
            deflate.Write(dictionary.Stream!.Value);
        if (compressed.Length + MinimumFilterSavings >= dictionary.Stream!.Length) return;
        dictionary.Stream.Value = compressed.ToArray();
        dictionary.Elements.SetName(Filter, "/FlateDecode");
    }

    private static void OptimizeJpeg(PdfDictionary dictionary)
    {
        var elements = dictionary.Elements;
        if (elements.GetName("/Subtype") != "/Image" || elements.GetName(Filter) != "/DCTDecode" ||
            elements.GetName("/ColorSpace") != "/DeviceRGB" || elements.GetInteger("/BitsPerComponent") != 8 ||
            elements.ContainsKey("/SMask") || elements.ContainsKey("/Mask") ||
            elements.ContainsKey("/Decode") || elements.ContainsKey("/ImageMask")) return;

        using var encoded = SKData.CreateCopy(dictionary.Stream!.Value);
        using var codec = SKCodec.Create(encoded);
        if (codec is null) return;
        int width = codec.Info.Width;
        int height = codec.Info.Height;
        if ((long)width * height > MaximumDecodedImagePixels || width != elements.GetInteger(Width) ||
            height != elements.GetInteger(Height)) return;

        using var original = SKBitmap.Decode(codec);
        if (original is null) return;
        float scale = Math.Min(1, (float)SmallerScanMaxSide / Math.Max(width, height));
        using var resized = scale == 1 ? null : original.Resize(
            new SKImageInfo(Math.Max(1, (int)(width * scale)), Math.Max(1, (int)(height * scale))),
            new SKSamplingOptions(SKFilterMode.Linear));
        var image = resized ?? original;
        using var replacement = image.Encode(SKEncodedImageFormat.Jpeg, SmallerScanQuality);
        if (replacement is null || replacement.Size >= dictionary.Stream.Length) return;
        dictionary.Stream.Value = replacement.ToArray();
        elements.SetInteger(Width, image.Width);
        elements.SetInteger(Height, image.Height);
    }
}
