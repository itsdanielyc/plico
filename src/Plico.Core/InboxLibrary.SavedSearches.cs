namespace Plico.Core;

public sealed partial class InboxLibrary
{
    /// <summary>The maximum number of characters accepted in a search expression.</summary>
    public const int MaximumSearchQueryLength = SearchTerms.MaximumQueryLength;

    /// <summary>Lists this library’s saved searches in name order.</summary>
    public IReadOnlyList<SavedSearch> GetSavedSearches() => _catalog.ReadSavedSearches();

    /// <summary>Saves a search recipe with a unique name and an optional existing sidebar tag.</summary>
    public SavedSearch SaveSearch(string name, string query, DocumentSort sort, string? scopeTag = null) =>
        _catalog.WriteSavedSearch(null, NormalizeTitle(name), NormalizeSavedSearchQuery(query), ValidateSearchSort(sort),
            scopeTag is null ? null : SearchTerms.TagKey(scopeTag));

    /// <summary>Updates a shortcut’s name, query, and order while retaining its sidebar tag.</summary>
    public SavedSearch UpdateSavedSearch(long id, string name, string query, DocumentSort sort) =>
        _catalog.WriteSavedSearch(id, NormalizeTitle(name), NormalizeSavedSearchQuery(query), ValidateSearchSort(sort), null);

    /// <summary>Removes a saved search by its stable identifier.</summary>
    public void RemoveSavedSearch(long id) => _catalog.DeleteSavedSearch(id);

    /// <summary>Trims a saved query and validates it with the document search parser.</summary>
    public static string NormalizeSavedSearchQuery(string query)
    {
        ArgumentNullException.ThrowIfNull(query);
        string cleaned = query.Trim();
        if (cleaned.Length is 0 or > MaximumSearchQueryLength)
        {
            throw new ArgumentException($"Enter a search of 1 to {MaximumSearchQueryLength} characters.", nameof(query));
        }
        if (!SearchTerms.Compile(cleaned).IsValid) throw new ArgumentException("Complete the search expression.", nameof(query));
        return cleaned;
    }

    private static DocumentSort ValidateSearchSort(DocumentSort sort) => Enum.IsDefined(sort)
        ? sort
        : throw new ArgumentOutOfRangeException(nameof(sort));
}
