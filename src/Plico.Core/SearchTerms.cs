using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Plico.Core;

internal static partial class SearchTerms
{
    internal const int MaximumQueryLength = 256;
    internal const int MaximumTagLength = 48;
    internal const string FtsTokenizer = "unicode61 remove_diacritics 2";
    internal const string RootFolderScope = ".";
    internal const char FolderSeparator = '/';
    private const string TagPrefix = "tag:";
    private const string AddedPrefix = "added:";
    private const string DatedPrefix = "dated:";
    private const string FolderPrefix = "folder:";
    private const int YearLength = 4;
    private const int MonthLength = 7;
    private const int DayLength = 10;
    private const string IsoDayFormat = "yyyy-MM-dd";
    private const string WordPattern = @"[\p{L}\p{M}\p{N}]+";
    private const string QueryPattern = "\"[^\"]*\"|“[^”]*”|" + WordPattern;
    private static readonly CompiledSearch InvalidSearch = new("", [], null, null, null, IsValid: false);

    internal readonly record struct CompiledSearch(string FullTextExpression, IReadOnlyCollection<string> TagKeys,
        DatePeriod? AddedPeriod, DatePeriod? DocumentPeriod, string? FolderPath, bool IsValid);

    internal readonly record struct DatePeriod(DateOnly FirstDay, DateOnly LastDay)
    {
        internal DatePeriod Intersect(DatePeriod other) => new(
            FirstDay > other.FirstDay ? FirstDay : other.FirstDay,
            LastDay < other.LastDay ? LastDay : other.LastDay);
    }

    // O(n) time and space in the bounded query length. Quoted text is scanned once.
    internal static CompiledSearch Compile(string query)
    {
        ArgumentOutOfRangeException.ThrowIfGreaterThan(query.Length, MaximumQueryLength);
        if (!query.Contains(TagPrefix, StringComparison.OrdinalIgnoreCase) &&
            !query.Contains(AddedPrefix, StringComparison.OrdinalIgnoreCase) &&
            !query.Contains(DatedPrefix, StringComparison.OrdinalIgnoreCase) &&
            !query.Contains(FolderPrefix, StringComparison.OrdinalIgnoreCase)) return CompileParts(query, query, [], null, null, null);

        HashSet<string>? tagKeys = null;
        DatePeriod? addedPeriod = null;
        DatePeriod? documentPeriod = null;
        string? folderPath = null;
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
                    if (ReadDateClause(query, cursor + AddedPrefix.Length, addedPeriod) is not { } added) return InvalidSearch;
                    (addedPeriod, clauseEnd) = added;
                }
                else if (remaining.StartsWith(DatedPrefix, StringComparison.OrdinalIgnoreCase))
                {
                    if (ReadDateClause(query, cursor + DatedPrefix.Length, documentPeriod) is not { } dated) return InvalidSearch;
                    (documentPeriod, clauseEnd) = dated;
                }
                else if (remaining.StartsWith(FolderPrefix, StringComparison.OrdinalIgnoreCase))
                {
                    if (ReadFolderClause(query, cursor + FolderPrefix.Length) is not { } folder) return InvalidSearch;
                    folderPath = folderPath is null ? folder.Path : IntersectFolderPaths(folderPath, folder.Path);
                    if (folderPath is null) return InvalidSearch;
                    clauseEnd = folder.End;
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

        if (textStart == 0) return CompileParts(query, query, [], null, null, null);
        text.Append(query.AsSpan(textStart));
        return CompileParts(query, text.ToString(), tagKeys is not null ? tagKeys : [], addedPeriod, documentPeriod, folderPath);
    }

    private static CompiledSearch CompileParts(string originalQuery, string textQuery, IReadOnlyCollection<string> tagKeys,
        DatePeriod? addedPeriod, DatePeriod? documentPeriod, string? folderPath)
    {
        string expression = FullTextExpression(textQuery);
        bool isValid = expression.Length > 0 || tagKeys.Count > 0 || addedPeriod.HasValue || documentPeriod.HasValue || folderPath is not null ||
            string.IsNullOrWhiteSpace(originalQuery);
        return new CompiledSearch(expression, tagKeys, addedPeriod, documentPeriod, folderPath, isValid);
    }

    private static (DatePeriod Period, int End)? ReadDateClause(string query, int start, DatePeriod? previous)
    {
        var (value, end) = ReadBareValue(query, start);
        if (ParseDatePeriod(value) is not { } parsed) return null;
        var period = previous is { } current ? current.Intersect(parsed) : parsed;
        return period.FirstDay <= period.LastDay ? (period, end) : null;
    }

    private static DatePeriod? ParseDatePeriod(string value)
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
        return new DatePeriod(first, last);
    }

    private static (string Key, int End)? ReadTagClause(string query, int start)
    {
        if (ReadFilterValue(query, start) is not { } tag) return null;
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

    private static (string Path, int End)? ReadFolderClause(string query, int start)
    {
        if (ReadFilterValue(query, start) is not { } value) return null;
        if (value.Label == RootFolderScope) return (RootFolderScope, value.End);
        if (value.Label.Any(char.IsControl) ||
            value.Label.Split(FolderSeparator).Any(part => part is "" or "." or "..")) return null;
        return (value.Label, value.End);
    }

    private static string? IntersectFolderPaths(string first, string second)
    {
        if (first == second) return first;
        if (first == RootFolderScope || second == RootFolderScope) return null;
        if (IsChildFolder(second, first)) return second;
        return IsChildFolder(first, second) ? first : null;
    }

    private static bool IsChildFolder(string path, string parent) => path.Length > parent.Length &&
        path[parent.Length] == FolderSeparator && path.StartsWith(parent, StringComparison.Ordinal);

    private static (string Label, int End)? ReadFilterValue(string query, int start)
    {
        if (start == query.Length || char.IsWhiteSpace(query[start])) return null;
        return ClosingQuote(query[start]) is char closingQuote
            ? ReadQuotedValue(query, start + 1, closingQuote)
            : ReadBareValue(query, start);
    }

    private static (string Label, int End) ReadBareValue(string query, int start)
    {
        int end = start;
        while (end < query.Length && !char.IsWhiteSpace(query[end])) end++;
        return (query[start..end], end);
    }

    private static (string Label, int End)? ReadQuotedValue(string query, int start, char closingQuote)
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
