using System.Text;

namespace Plico.Core;

/// <summary>A ranked match supported by indexed text or a locally generated embedding.</summary>
public sealed record DocumentSimilarityMatch(DocumentSummary Document, double Score);

internal sealed record SimilaritySource(long Id, string Title, string Text);

internal static class SimilarityWords
{
    private const int MinimumWordLength = 3;
    private const int MaximumWordLength = 48;
    private static readonly HashSet<string> CommonWords = new(
        ("the and for with from this that your have are was were will has more than any about " +
         "les des dans une pour avec est sur par pas que vous nous sont cette avoir " +
         "der die das den dem des und mit für auf von ein eine ist nur sie zur " +
         "los las del por con para una que este esta como desde sus hola").Split(' '), StringComparer.Ordinal);

    // O(n) time and O(u) space for n text characters and u distinct searchable terms.
    internal static Dictionary<string, int> Count(string text)
    {
        var words = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var match in SearchTerms.WordTokens().EnumerateMatches(text))
        {
            if (match.Length is < MinimumWordLength or > MaximumWordLength) continue;
            string word = text.AsSpan(match.Index, match.Length).ToString().ToLowerInvariant().Normalize(NormalizationForm.FormC);
            if (CommonWords.Contains(word) || !word.Any(char.IsLetter)) continue;
            words[word] = words.GetValueOrDefault(word) + 1;
        }
        return words;
    }

    internal static string[] DistinctTerms(string text, int maximum) => Count(text)
        .OrderByDescending(term => term.Value).ThenByDescending(term => term.Key.Length).ThenBy(term => term.Key, StringComparer.Ordinal)
        .Take(maximum).Select(term => term.Key).ToArray();

    internal static double Cosine(Dictionary<string, int> target, Dictionary<string, int> candidate,
        IReadOnlyDictionary<string, double> weights, double targetNorm)
    {
        if (targetNorm == 0 || candidate.Count == 0) return 0;
        double square = 0;
        double dot = 0;
        foreach (var (term, usage) in candidate)
        {
            double termWeight = weights.GetValueOrDefault(term, 1);
            double value = Math.Sqrt(usage) * termWeight;
            square += value * value;
            if (target.TryGetValue(term, out int targetUse)) dot += Math.Sqrt(targetUse) * termWeight * value;
        }
        return square == 0 ? 0 : dot / (targetNorm * Math.Sqrt(square));
    }
}
