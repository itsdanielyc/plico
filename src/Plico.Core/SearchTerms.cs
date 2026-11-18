using System.Text;
using System.Text.RegularExpressions;

namespace Plico.Core;

internal static partial class SearchTerms
{
    internal const int MaximumQueryLength = 256;
    internal const int MaximumTagLength = 48;

    // O(n) time and space in the bounded query length.
    internal static string FullTextExpression(string query)
    {
        ArgumentOutOfRangeException.ThrowIfGreaterThan(query.Length, MaximumQueryLength);
        return string.Join(" AND ", WordTokens().Matches(query).Select(word => $"\"{word.Value}\"*"));
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

    [GeneratedRegex(@"[\p{L}\p{M}\p{N}]+", RegexOptions.CultureInvariant | RegexOptions.NonBacktracking)]
    private static partial Regex WordTokens();
}
