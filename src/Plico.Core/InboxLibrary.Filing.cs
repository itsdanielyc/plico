using System.Globalization;

namespace Plico.Core;

public sealed partial class InboxLibrary
{
    /// <summary>Lists the tag suggestion rules stored in the library.</summary>
    public IReadOnlyList<FilingRule> GetFilingRules() => _catalog.ReadFilingRules();

    /// <summary>Creates a rule or updates the rule with the supplied ID.</summary>
    public FilingRule SaveFilingRule(FilingRule rule) => _catalog.SaveFilingRule(rule);

    /// <summary>Removes a tag suggestion rule without removing assigned tags.</summary>
    public void DeleteFilingRule(long id) => _catalog.DeleteFilingRule(id);

    /// <summary>Suggests metadata for the selection using indexed text and reviewed examples in this library.</summary>
    public IReadOnlyList<FilingSuggestion> SuggestFiling(IReadOnlyList<long> documentIds, OcrLanguage fallbackLanguage,
        CultureInfo locale, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(documentIds);
        ArgumentNullException.ThrowIfNull(locale);
        cancellationToken.ThrowIfCancellationRequested();
        if (documentIds.Count == 0) return [];

        var rules = _catalog.MatchFilingRules(documentIds, cancellationToken);
        var classifier = new FilingTagClassifier();
        foreach (var (text, tags) in _catalog.ReadFilingTrainingExamples(cancellationToken)) classifier.Train(text, tags);

        var results = new List<FilingSuggestion>(documentIds.Count);
        foreach (var input in _catalog.ReadFilingInputs(documentIds, cancellationToken))
        {
            string excerpt = input.Title + "\n" + input.Text;
            var culture = FilingFieldExtractor.DateCulture(input.Language ?? fallbackLanguage, locale);
            var fields = FilingFieldExtractor.Read(excerpt, culture);
            var keys = input.Tags.Select(SearchTerms.TagKey).ToHashSet(StringComparer.Ordinal);
            var tags = new List<FilingTag>();
            if (rules.TryGetValue(input.Id, out var matchedRules))
                tags.AddRange(matchedRules.Where(rule => keys.Add(SearchTerms.TagKey(rule.Tag))));
            tags.AddRange(classifier.Suggest(excerpt, keys, cancellationToken));
            results.Add(new(input.Id, input.Title, input.Date, fields, tags));
        }
        return results;
    }

    /// <summary>Commits the confirmed tag and document date changes together in one catalog transaction.</summary>
    public void ApplyFilingSuggestions(IReadOnlyList<FilingSelection> selections, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(selections);
        cancellationToken.ThrowIfCancellationRequested();
        _catalog.ApplyFilingSelections(selections, cancellationToken);
    }
}
