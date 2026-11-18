using Microsoft.Data.Sqlite;
using System.Globalization;
using System.Text;

namespace Plico.Core;

internal sealed partial class LibraryCatalog
{
    private const int CorrespondentMatchingTextLimit = 65_536;
    private const int CorrespondentSuggestionLimit = 12;

    // O(a + n + candidate comparisons) time and O(a + n) space for alias tokens a and bounded OCR tokens n.
    internal IReadOnlyList<Correspondent> SuggestCorrespondents(long documentId, IReadOnlyList<Correspondent> contacts,
        CancellationToken token)
    {
        if (contacts.Count == 0) return [];
        using var connection = Connect();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT title || char(10) || substr(body, 1, $limit) FROM documents WHERE id = $id AND is_present = 1;
            """;
        command.Parameters.AddWithValue("$limit", CorrespondentMatchingTextLimit);
        command.Parameters.AddWithValue("$id", documentId);
        if (command.ExecuteScalar() is not string text) throw new InvalidOperationException(DocumentUnavailableMessage);
        var names = new Dictionary<string, List<(long Id, string[] Words)>>(StringComparer.Ordinal);
        foreach (var contact in contacts)
        {
            token.ThrowIfCancellationRequested();
            foreach (var alias in contact.Aliases.Prepend(contact.Name))
            {
                var phrase = MatchWords(alias);
                if (phrase.Length == 0) continue;
                if (!names.TryGetValue(phrase[0], out var candidates)) names.Add(phrase[0], candidates = []);
                candidates.Add((contact.Id, phrase));
            }
        }

        var hits = new HashSet<long>();
        var words = MatchWords(text);
        for (int index = 0; index < words.Length && hits.Count < CorrespondentSuggestionLimit; index++)
        {
            token.ThrowIfCancellationRequested();
            if (!names.TryGetValue(words[index], out var candidates)) continue;
            foreach (var (id, phrase) in candidates)
            {
                if (words.Length - index >= phrase.Length && words.AsSpan(index, phrase.Length).SequenceEqual(phrase))
                    hits.Add(id);
            }
        }
        return contacts.Where(contact => hits.Contains(contact.Id)).Take(CorrespondentSuggestionLimit).ToArray();
    }

    private static string[] MatchWords(string text)
    {
        string upper = text.ToUpperInvariant().Normalize(NormalizationForm.FormD);
        return SearchTerms.WordTokens().Matches(upper)
            .Select(match => string.Concat(match.Value.Where(character => CharUnicodeInfo.GetUnicodeCategory(character) != UnicodeCategory.NonSpacingMark)))
            .Where(word => word.Length > 0).ToArray();
    }
}
