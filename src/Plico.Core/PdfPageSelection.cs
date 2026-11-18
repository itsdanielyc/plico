using System.Globalization;

namespace Plico.Core;

/// <summary>Validated PDF page ranges in source order, with each page represented once.</summary>
public sealed class PdfPageSelection
{
    private PdfPageSelection(int sourcePageCount, IReadOnlyList<PdfPageRange> ranges)
    {
        SourcePageCount = sourcePageCount;
        Ranges = ranges;
        SelectedPageCount = ranges.Sum(range => range.Count);
    }

    /// <summary>The number of distinct selected pages.</summary>
    public int SelectedPageCount { get; }

    internal int SourcePageCount { get; }
    internal IReadOnlyList<PdfPageRange> Ranges { get; }

    /// <summary>Reads comma-separated, one-based pages and ascending ranges for a PDF.</summary>
    // O(input length + ranges log ranges) time and O(ranges) space, including for large ranges.
    public static PdfPageSelection Parse(string expression, int sourcePageCount)
    {
        ArgumentNullException.ThrowIfNull(expression);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sourcePageCount);
        var ranges = new List<PdfPageRange>();
        ReadOnlySpan<char> input = expression;
        foreach (var item in input.Split(','))
        {
            var token = input[item].Trim();
            int hyphen = token.IndexOf('-');
            ReadOnlySpan<char> start = hyphen < 0 ? token : token[..hyphen].Trim();
            ReadOnlySpan<char> end = hyphen < 0 ? token : token[(hyphen + 1)..].Trim();
            if (!TryReadPage(start, out int first) || !TryReadPage(end, out int last) ||
                first < 1 || last < first || last > sourcePageCount)
            {
                throw InvalidSelection(sourcePageCount, nameof(expression));
            }
            ranges.Add(new PdfPageRange(first, last));
        }
        if (ranges.Count == 0) throw InvalidSelection(sourcePageCount, nameof(expression));

        ranges.Sort(static (left, right) => left.FirstPage.CompareTo(right.FirstPage));
        int mergedIndex = 0;
        for (int index = 1; index < ranges.Count; index++)
        {
            var previous = ranges[mergedIndex];
            var next = ranges[index];
            if (next.FirstPage - 1 > previous.LastPage) ranges[++mergedIndex] = next;
            else ranges[mergedIndex] = new PdfPageRange(previous.FirstPage, Math.Max(previous.LastPage, next.LastPage));
        }
        ranges.RemoveRange(mergedIndex + 1, ranges.Count - mergedIndex - 1);
        return new PdfPageSelection(sourcePageCount, ranges.AsReadOnly());
    }

    private static bool TryReadPage(ReadOnlySpan<char> text, out int page) =>
        int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out page) &&
        text.IndexOfAnyExceptInRange('0', '9') < 0;

    private static ArgumentException InvalidSelection(int sourcePageCount, string parameterName) => new(
        $"Enter pages or ascending ranges from 1 to {sourcePageCount}, separated by commas.", parameterName);
}

internal readonly record struct PdfPageRange(int FirstPage, int LastPage)
{
    internal int Count => LastPage - FirstPage + 1;
}
