using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Plico.Core;

internal static partial class SearchTerms
{
    internal const int MaximumQueryLength = 256;
    internal const int MaximumTagLength = 48;
    internal const string FtsTokenizer = "unicode61 remove_diacritics 2";
    private const string TagPrefix = "tag:";
    private const string AddedPrefix = "added:";
    private const int YearLength = 4;
    private const int MonthLength = 7;
    private const int DayLength = 10;
    private const string IsoDayFormat = "yyyy-MM-dd";
    private const string WordPattern = @"[\p{L}\p{M}\p{N}]+";
    private const string QueryPattern = "\"[^\"]*\"|“[^”]*”|" + WordPattern;
    private static readonly CompiledSearch InvalidSearch = new("", [], null, IsValid: false);

    internal readonly record struct CompiledSearch(string FullTextExpression, IReadOnlyCollection<string> TagKeys,
        AddedDatePeriod? AddedPeriod, bool IsValid);

    internal readonly record struct AddedDatePeriod(DateOnly FirstDay, DateOnly LastDay)
    {
        internal AddedDatePeriod Intersect(AddedDatePeriod other) => new(
            FirstDay > other.FirstDay ? FirstDay : other.FirstDay,
            LastDay < other.LastDay ? LastDay : other.LastDay);
    }

    // O(n) time and space in the bounded query length. Quoted text is scanned once.
    internal static CompiledSearch Compile(string query)
    {
        ArgumentOutOfRangeException.ThrowIfGreaterThan(query.Length, MaximumQueryLength);
        if (!query.Contains(TagPrefix, StringComparison.OrdinalIgnoreCase) &&
            !query.Contains(AddedPrefix, StringComparison.OrdinalIgnoreCase)) return CompileParts(query, query, [], null);

        HashSet<string>? tagKeys = null;
        AddedDatePeriod? addedPeriod = null;
        var text = new StringBuilder(query.Length);
        int textStart = 0;
        for (int cursor = 0; cursor < query.Length;)
        {
            int clauseEnd = cursor;
            if (cursor == 0 || char.IsWhiteSpace(query[cursor - 1]))
            {
                var remaining = query.AsSpan(cursor);
                if (remaining.StartsWith(TagPrefix, StringComparison.OrdinalIgnoreCase))
                {
                    if (ReadTagClause(query, cursor + TagPrefix.Length) is not { } tag) return InvalidSearch;
                    (tagKeys ??= new HashSet<string>(StringComparer.Ordinal)).Add(tag.Key);
                    clauseEnd = tag.End;
                }
                else if (remaining.StartsWith(AddedPrefix, StringComparison.OrdinalIgnoreCase))
                {
                    var (value, end) = ReadBareValue(query, cursor + AddedPrefix.Length);
                    if (ParseAddedPeriod(value) is not { } period) return InvalidSearch;
                    if (addedPeriod is { } previous) period = previous.Intersect(period);
                    if (period.FirstDay > period.LastDay) return InvalidSearch;
                    addedPeriod = period;
                    clauseEnd = end;
                }
            }
            if (clauseEnd > cursor)
            {
                text.Append(query.AsSpan(textStart, cursor - textStart)).Append(' ');
                textStart = clauseEnd;
                cursor = clauseEnd;
                continue;
            }

            if (ClosingQuote(query[cursor]) is char closingQuote)
            {
                int end = query.IndexOf(closingQuote, cursor + 1);
                if (end < 0) break;
                cursor = end + 1;
                continue;
            }
            cursor++;
        }

        if (textStart == 0) return CompileParts(query, query, [], null);
        text.Append(query.AsSpan(textStart));
        return CompileParts(query, text.ToString(), tagKeys is not null ? tagKeys : [], addedPeriod);
    }

    private static CompiledSearch CompileParts(string originalQuery, string textQuery, IReadOnlyCollection<string> tagKeys,
        AddedDatePeriod? addedPeriod)
    {
        string expression = FullTextExpression(textQuery);
        bool isValid = expression.Length > 0 || tagKeys.Count > 0 || addedPeriod.HasValue || string.IsNullOrWhiteSpace(originalQuery);
        return new CompiledSearch(expression, tagKeys, addedPeriod, isValid);
    }

    private static AddedDatePeriod? ParseAddedPeriod(string value)
    {
        string? day = value.Length switch
        {
            YearLength => value + "-01-01",
            MonthLength => value + "-01",
            DayLength => value,
            _ => null
        };
        if (!DateOnly.TryParseExact(day, IsoDayFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out var first)) return null;
        var last = value.Length switch
        {
            YearLength => new DateOnly(first.Year, 12, 31),
            MonthLength => new DateOnly(first.Year, first.Month, DateTime.DaysInMonth(first.Year, first.Month)),
            _ => first
        };
        return new AddedDatePeriod(first, last);
    }

    private static (string Key, int End)? ReadTagClause(string query, int start)
    {
        if (start == query.Length || char.IsWhiteSpace(query[start])) return null;
        var value = ClosingQuote(query[start]) is char closingQuote
            ? ReadQuotedTag(query, start + 1, closingQuote)
            : ReadBareValue(query, start);
        if (value is not { } tag) return null;
        try
        {
            return (TagKey(tag.Label), tag.End);
        }
        catch (ArgumentException)
        {
            // The query can contain unfinished input while the search box is being edited.
            return null;
        }
    }

    private static (string Label, int End) ReadBareValue(string query, int start)
    {
        int end = start;
        while (end < query.Length && !char.IsWhiteSpace(query[end])) end++;
        return (query[start..end], end);
    }

    private static (string Label, int End)? ReadQuotedTag(string query, int start, char closingQuote)
    {
        var label = new StringBuilder();
        for (int cursor = start; cursor < query.Length; cursor++)
        {
            char current = query[cursor];
            if (current == closingQuote)
            {
                int end = cursor + 1;
                return end == query.Length || char.IsWhiteSpace(query[end]) ? (label.ToString(), end) : null;
            }
            if (current == '\\' && cursor + 1 < query.Length &&
                (query[cursor + 1] == closingQuote || query[cursor + 1] == '\\'))
            {
                current = query[++cursor];
            }
            label.Append(current);
        }
        return null;
    }

    private static char? ClosingQuote(char openingQuote) => openingQuote switch
    {
        '"' => '"',
        '“' => '”',
        _ => null
    };

    private static string FullTextExpression(string query)
    {
        var expressions = new List<string>();
        foreach (Match part in QueryParts().Matches(query))
        {
            string value = part.Value;
            if (value[0] is not ('"' or '“'))
            {
                expressions.Add($"\"{value}\"*");
                continue;
            }

            // Pass literal tokens to FTS5 even when the phrase contains punctuation or query syntax.
            string phrase = string.Join(' ', WordTokens().Matches(value).Select(word => word.Value));
            if (phrase.Length > 0) expressions.Add($"\"{phrase}\"");
        }
        return string.Join(" AND ", expressions);
    }

    internal static string CleanLabel(string label)
    {
        var trimmed = label.Trim().Normalize(NormalizationForm.FormC);
        if (trimmed.Length is 0 or > MaximumTagLength || trimmed.Any(char.IsControl))
        {
            throw new ArgumentException($"Enter a tag of 1 to {MaximumTagLength} printable characters.", nameof(label));
        }
        return trimmed;
    }

    internal static string TagKey(string label) => CleanLabel(label).ToUpperInvariant();

    [GeneratedRegex(WordPattern, RegexOptions.CultureInvariant | RegexOptions.NonBacktracking)]
    private static partial Regex WordTokens();

    [GeneratedRegex(QueryPattern, RegexOptions.CultureInvariant | RegexOptions.NonBacktracking)]
    private static partial Regex QueryParts();
}
