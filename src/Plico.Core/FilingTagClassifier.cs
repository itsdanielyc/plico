using System.Globalization;
using System.Text;

namespace Plico.Core;

internal sealed class FilingTagClassifier
{
    private const int FeatureBuckets = 1 << 15;
    private const int MinimumSamplesPerClass = 3;
    private const int MinimumSupportingFeatures = 2;
    private const int MaximumTokenLength = 32;
    private const int MinimumTokenLength = 3;
    private const int MaximumLearnedTags = 3;
    private const int MaximumTermWeight = 3;
    private const double MinimumLogOdds = 1.3862943611198906; // Four-to-one odds.
    internal const int MaximumTrainingCharacters = 32_768;

    private sealed class ClassCounts
    {
        internal Dictionary<int, long> Frequencies { get; } = [];
        internal long Tokens { get; set; }
        internal int Examples { get; set; }
    }

    private readonly Dictionary<string, ClassCounts> _labels = new(StringComparer.OrdinalIgnoreCase);
    private readonly long[] _totalFrequencies = new long[FeatureBuckets];
    private long _totalTokens;
    private int _examples;

    // O(tokens × (1 + tags)) time per example. Global memory is fixed and each tag stores only observed buckets.
    internal void Train(string text, IReadOnlyList<string> tags)
    {
        var features = Features(text);
        if (features.Count == 0) return;
        long tokens = 0;
        foreach (var (feature, frequency) in features)
        {
            _totalFrequencies[feature] += frequency;
            tokens += frequency;
        }
        _examples++;
        _totalTokens += tokens;
        foreach (string tag in tags)
        {
            if (!_labels.TryGetValue(tag, out var label)) _labels.Add(tag, label = new ClassCounts());
            label.Tokens += tokens;
            label.Examples++;
            foreach (var (feature, frequency) in features)
                label.Frequencies[feature] = label.Frequencies.GetValueOrDefault(feature) + frequency;
        }
    }

    // O(text length + tags × distinct features) time and O(distinct features + tags) scratch.
    internal IReadOnlyList<FilingTag> Suggest(string text, HashSet<string> existingKeys, CancellationToken token)
    {
        if (_examples < 2 * MinimumSamplesPerClass) return [];
        var features = Features(text);
        var suggestions = new List<(string Label, double Score)>();
        foreach (var (name, label) in _labels)
        {
            token.ThrowIfCancellationRequested();
            int negativeExamples = _examples - label.Examples;
            if (label.Examples < MinimumSamplesPerClass || negativeExamples < MinimumSamplesPerClass ||
                existingKeys.Contains(SearchTerms.TagKey(name))) continue;

            double score = Math.Log((label.Examples + 1.0) / (negativeExamples + 1.0));
            double scoringOffset = Math.Log((_totalTokens - label.Tokens + FeatureBuckets) /
                (double)(label.Tokens + FeatureBuckets));
            int supported = 0;
            foreach (var (feature, count) in features)
            {
                long positive = label.Frequencies.GetValueOrDefault(feature);
                long negative = _totalFrequencies[feature] - positive;
                if (positive == 0 && negative == 0) continue;
                if (positive > negative) supported++;
                score += Math.Min(count, MaximumTermWeight) *
                    (Math.Log((positive + 1.0) / (negative + 1.0)) + scoringOffset);
            }
            if (supported >= MinimumSupportingFeatures && score >= MinimumLogOdds) suggestions.Add((name, score));
        }
        return suggestions.OrderByDescending(item => item.Score).ThenBy(item => item.Label, StringComparer.OrdinalIgnoreCase)
            .Take(MaximumLearnedTags).Select(item => new FilingTag(item.Label, FilingTagSource.ReviewedDocuments)).ToArray();
    }

    // O(n) time and space for a bounded training excerpt. Stable buckets keep accented variants together.
    private static Dictionary<int, int> Features(string source)
    {
        string text = (source.Length > MaximumTrainingCharacters ? source[..MaximumTrainingCharacters] : source).Normalize(NormalizationForm.FormD);
        var features = new Dictionary<int, int>();
        int? previous = null;
        foreach (var match in SearchTerms.WordTokens().EnumerateMatches(text))
        {
            if (match.Length is < MinimumTokenLength or > MaximumTokenLength)
            {
                previous = null;
                continue;
            }
            bool hasLetter = false;
            int hash = 17;
            foreach (char character in text.AsSpan(match.Index, match.Length))
            {
                if (CharUnicodeInfo.GetUnicodeCategory(character) == UnicodeCategory.NonSpacingMark) continue;
                hasLetter |= char.IsLetter(character);
                hash = unchecked(hash * 31 + char.ToUpperInvariant(character));
            }
            if (!hasLetter)
            {
                previous = null;
                continue;
            }
            Count(hash);
            if (previous is int prefix) Count(unchecked(prefix * 397 ^ hash));
            previous = hash;
        }
        return features;

        void Count(int hash)
        {
            int bucket = hash & (FeatureBuckets - 1);
            features[bucket] = features.GetValueOrDefault(bucket) + 1;
        }
    }
}
