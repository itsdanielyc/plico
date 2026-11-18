using Plico.Core;

namespace Plico.Desktop;

internal sealed partial class InboxViewModel
{
    private const int MaximumTagSuggestions = 8;
    private static readonly IComparer<TagSuggestion> TagKeyOrder = Comparer<TagSuggestion>.Create(
        static (left, right) => StringComparer.Ordinal.Compare(left.Key, right.Key));

    private TagSuggestion[]? _tagSuggestionIndex;

    /// <summary>Finds matching labels from the open library in canonical tag-key order.</summary>
    internal IReadOnlyList<string> GetTagSuggestions(string? enteredText, bool excludeAppliedTags)
    {
        if (Tags.Count == 0 || string.IsNullOrWhiteSpace(enteredText)) return [];
        string key;
        try
        {
            key = InboxLibrary.GetTagKey(enteredText);
        }
        catch (ArgumentException)
        {
            return [];
        }

        var index = _tagSuggestionIndex ??= IndexTagLabels(Tags);
        var excluded = excludeAppliedTags && SelectedDocument is { Tags.Count: > 0 } document
            ? document.Tags.ToHashSet(StringComparer.Ordinal)
            : null;
        return FindMatchingLabels(index, key, excluded);
    }

    // O(n log n) time and O(n) space for a changed set of library labels.
    private static TagSuggestion[] IndexTagLabels(IReadOnlyList<string> labels)
    {
        var index = labels.Select(label => new TagSuggestion(InboxLibrary.GetTagKey(label), label)).ToArray();
        Array.Sort(index, TagKeyOrder);
        return index;
    }

    // O(log n + scanned matches) time and O(suggestion limit) space after building the index and exclusions.
    private static IReadOnlyList<string> FindMatchingLabels(TagSuggestion[] index, string prefix, HashSet<string>? excluded)
    {
        int first = Array.BinarySearch(index, new TagSuggestion(prefix, prefix), TagKeyOrder);
        if (first < 0) first = ~first;
        var matches = new List<string>(MaximumTagSuggestions);
        for (int position = first; position < index.Length && matches.Count < MaximumTagSuggestions; position++)
        {
            var candidate = index[position];
            if (!candidate.Key.StartsWith(prefix, StringComparison.Ordinal)) break;
            if (excluded?.Contains(candidate.Label) != true) matches.Add(candidate.Label);
        }
        return matches;
    }

    partial void OnTagsChanged(IReadOnlyList<string>? oldValue, IReadOnlyList<string> newValue)
    {
        RefreshTagNavigation();
        if (_tagSuggestionIndex is not null && oldValue?.SequenceEqual(newValue, StringComparer.Ordinal) != true)
        {
            _tagSuggestionIndex = null;
        }
        OnPropertyChanged(nameof(HasTags));
    }

    private readonly record struct TagSuggestion(string Key, string Label);
}
