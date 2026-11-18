using System.Text;
using System.Text.RegularExpressions;

namespace Plico.Core;

internal static partial class SearchTerms
{
    internal const int MaximumQueryLength = 256;
    internal const int MaximumTagLength = 48;
    private const string WordPattern = @"[\p{L}\p{M}\p{N}]+";
    private const string QueryPattern = "\"[^\"]*\"|“[^”]*”|" + WordPattern;

    // O(n) time and space in the bounded query length.
    internal static string FullTextExpression(string query)
    {
        ArgumentOutOfRangeException.ThrowIfGreaterThan(query.Length, MaximumQueryLength);
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
