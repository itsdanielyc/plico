using System.Globalization;
using SkiaSharp;

namespace Plico.Core;

/// <summary>Lays out selectable text in a paginated A4 PDF.</summary>
internal static class TextPdfWriter
{
    private const float PageWidth = 595.28f;
    private const float PageHeight = 841.89f;
    private const float Margin = 48f;
    private const float FontSize = 11f;
    private const float LineHeight = 16f;

    // O(text length) layout time and one line of working text in addition to the PDF writer's state.
    internal static void Write(string destination, IEnumerable<string> lines, CancellationToken cancellationToken)
    {
        using var output = File.Create(destination);
        using var document = SKDocument.CreatePdf(output) ?? throw new IOException("Plico could not create a PDF.");
        using var font = new SKFont(SKTypeface.Default, FontSize);
        using var ink = new SKPaint { Color = SKColors.Black, IsAntialias = true };
        SKCanvas? page = null;
        float baseline = 0;
        bool wroteText = false;
        foreach (string sourceLine in lines)
        {
            foreach (string line in Wrap(sourceLine.Replace("\t", "    ", StringComparison.Ordinal), font))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (page is null || baseline > PageHeight - Margin)
                {
                    if (page is not null) document.EndPage();
                    page = document.BeginPage(PageWidth, PageHeight);
                    baseline = Margin + FontSize;
                }
                page.DrawText(line, Margin, baseline, SKTextAlign.Left, font, ink);
                wroteText |= !string.IsNullOrWhiteSpace(line);
                baseline += LineHeight;
            }
        }
        if (!wroteText) throw new InvalidDataException("The document has no readable text to import.");
        document.EndPage();
        document.Close();
    }

    private static IEnumerable<string> Wrap(string source, SKFont font)
    {
        if (font.MeasureText(source) <= PageWidth - 2 * Margin)
        {
            yield return source;
            yield break;
        }

        int lineStart = 0;
        while (lineStart < source.Length)
        {
            int end = lineStart;
            int lastSpace = -1;
            float width = 0;
            while (end < source.Length)
            {
                int elementLength = StringInfo.GetNextTextElementLength(source.AsSpan(end));
                float elementWidth = font.MeasureText(source.AsSpan(end, elementLength));
                if (end != lineStart && width + elementWidth > PageWidth - 2 * Margin) break;
                if (char.IsWhiteSpace(source[end])) lastSpace = end;
                width += elementWidth;
                end += elementLength;
            }
            if (end < source.Length && lastSpace > lineStart) end = lastSpace;
            yield return source[lineStart..end].TrimEnd();
            lineStart = end;
            while (lineStart < source.Length && char.IsWhiteSpace(source[lineStart])) lineStart++;
        }
    }
}
