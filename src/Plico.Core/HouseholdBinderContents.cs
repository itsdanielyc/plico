using System.Globalization;
using SkiaSharp;

namespace Plico.Core;

internal readonly record struct BinderContentsEntry(string Title, int FirstPage);

/// <summary>Lays out searchable contents pages before the selected document pages.</summary>
internal static class HouseholdBinderContents
{
    private const float PageWidth = 612;
    private const float PageHeight = 792;
    private const float Margin = 54;
    private const float HeadingFontSize = 24;
    private const float HeadingLineHeight = 30;
    private const float BodyFontSize = 12;
    private const float BodyLineHeight = 17;
    private const float SectionGap = 12;
    private const float RowGap = 7;
    private const float PageNumberWidth = 70;
    private const float ContentWidth = PageWidth - 2 * Margin;
    private const float ContentBottom = PageHeight - Margin - BodyLineHeight;
    private const string Contents = "Contents";

    private sealed record ContentsRow(IReadOnlyList<string> Lines, int FirstPage)
    {
        internal float Height => Lines.Count * BodyLineHeight + RowGap;
    }

    // O(selected documents) time and space for the catalog's bounded titles.
    internal static int Write(Stream output, string title, IReadOnlyList<BinderContentsEntry> entries, CancellationToken token)
    {
        using var heading = new SKFont(SKTypeface.Default, HeadingFontSize);
        using var body = new SKFont(SKTypeface.Default, BodyFontSize);
        using var paint = new SKPaint { IsAntialias = true, Color = SKColors.Black };
        var headingLines = Wrap(title, heading, ContentWidth);
        float firstBodyRow = Margin + headingLines.Count * HeadingLineHeight + SectionGap + 2 * BodyLineHeight;
        var sheets = Paginate(entries, body, firstBodyRow, token);
        using var pdf = SKDocument.CreatePdf(output) ?? throw new IOException("Could not create the binder contents.");
        for (int index = 0; index < sheets.Count; index++)
        {
            token.ThrowIfCancellationRequested();
            var canvas = pdf.BeginPage(PageWidth, PageHeight);
            float baseline = Margin;
            if (index == 0)
            {
                foreach (string line in headingLines)
                {
                    canvas.DrawText(line, Margin, baseline + HeadingFontSize, SKTextAlign.Left, heading, paint);
                    baseline += HeadingLineHeight;
                }
                baseline += SectionGap;
            }
            canvas.DrawText(Contents, Margin, baseline + BodyLineHeight, SKTextAlign.Left, body, paint);
            canvas.DrawText("Page", PageWidth - Margin, baseline + BodyLineHeight, SKTextAlign.Right, body, paint);
            baseline += 2 * BodyLineHeight;
            foreach (var row in sheets[index])
            {
                for (int lineIndex = 0; lineIndex < row.Lines.Count; lineIndex++)
                    canvas.DrawText(row.Lines[lineIndex], Margin, baseline + lineIndex * BodyLineHeight, SKTextAlign.Left, body, paint);
                string pageNumber = checked(row.FirstPage + sheets.Count).ToString(CultureInfo.InvariantCulture);
                canvas.DrawText(pageNumber, PageWidth - Margin, baseline, SKTextAlign.Right, body, paint);
                baseline += row.Height;
            }
            canvas.DrawText((index + 1).ToString(CultureInfo.InvariantCulture), PageWidth - Margin, PageHeight - Margin,
                SKTextAlign.Right, body, paint);
            pdf.EndPage();
        }
        pdf.Close();
        return sheets.Count;
    }

    private static List<List<ContentsRow>> Paginate(IReadOnlyList<BinderContentsEntry> entries, SKFont font, float baseline,
        CancellationToken token)
    {
        var rows = new List<ContentsRow>();
        var sheets = new List<List<ContentsRow>> { rows };
        foreach (var entry in entries)
        {
            token.ThrowIfCancellationRequested();
            var row = new ContentsRow(Wrap(entry.Title, font, ContentWidth - PageNumberWidth), entry.FirstPage);
            if (baseline + row.Height > ContentBottom)
            {
                rows = [];
                sheets.Add(rows);
                baseline = Margin + 2 * BodyLineHeight;
            }
            rows.Add(row);
            baseline += row.Height;
        }
        return sheets;
    }

    private static List<string> Wrap(string text, SKFont font, float width)
    {
        var lines = new List<string>();
        var remaining = text.AsSpan().Trim();
        while (!remaining.IsEmpty)
        {
            int length = font.BreakText(remaining, width);
            if (length == remaining.Length)
            {
                lines.Add(remaining.ToString());
                break;
            }
            if (length == 0) length = 1;
            if (length < remaining.Length && char.IsSurrogatePair(remaining[length - 1], remaining[length])) length++;
            int wordEnd = remaining[..length].LastIndexOf(' ');
            if (wordEnd > 0) length = wordEnd;
            lines.Add(remaining[..length].ToString().TrimEnd());
            remaining = remaining[length..].TrimStart();
        }
        return lines;
    }
}
