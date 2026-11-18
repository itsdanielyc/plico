using System.Text;
using Microsoft.Data.Sqlite;

namespace Plico.Core;

internal sealed partial class LibraryCatalog
{
    private const string SearchRank = "bm25(5.0, 1.0)";
    private const int SnippetLeadingCharacters = 20;
    internal const int SearchMatchStart = 30;
    internal const int SearchMatchEnd = 31;
    private static readonly string SearchSnippet = $"""
        replace(replace(snippet(documents_search, 1, char({SearchMatchStart}), char({SearchMatchEnd}), ' … ', 18), char(13), ''), char(10), ' ')
        """;

    // O(n²) worst-case construction for n bounded query characters. Each value is bound as a SQLite parameter.
    private string BooleanCondition(SqliteConnection connection, SqliteCommand command, SearchPredicate root)
    {
        RegisterLocalAddedDay(connection);
        return Build(root);

        string Build(SearchPredicate node) => node switch
        {
            SearchPredicate.Clause clause => BooleanClause(command, clause.Value),
            SearchPredicate.Exclude exclusion => $"NOT ({Build(exclusion.Operand)})",
            SearchPredicate.All all => $"({Build(all.Left)} AND {Build(all.Right)})",
            SearchPredicate.Either either => $"({Build(either.Left)} OR {Build(either.Right)})",
            _ => throw new InvalidOperationException("Unknown search predicate.")
        };
    }

    private static string BooleanClause(SqliteCommand command, SearchTerms.CompiledSearch clause)
    {
        if (clause.FullTextExpression.Length > 0)
            return $"d.id IN (SELECT rowid FROM documents_search WHERE documents_search MATCH {Bind(clause.FullTextExpression)})";
        if (clause.TagKeys.Count > 0)
            return $"d.id IN (SELECT document_id FROM document_tags WHERE tag_key = {Bind(clause.TagKeys.Single())})";
        if (clause.Reviewed is bool reviewed) return $"d.reviewed = {Bind(reviewed)}";
        if (clause.HasText is bool hasText) return $"d.has_text = {Bind(hasText)}";
        if (clause.FolderPath is { } folder)
        {
            if (folder == SearchTerms.RootFolderScope) return "instr(d.relative_path, '/') = 0";
            const char exclusiveEnd = (char)(SearchTerms.FolderSeparator + 1);
            return $"d.relative_path >= {Bind(folder + SearchTerms.FolderSeparator)} AND d.relative_path < {Bind(folder + exclusiveEnd)}";
        }
        if (clause.AddedPeriod is { } added)
        {
            long start = (added.FirstDay.DayNumber - UnixEpochDay) * SecondsPerDay - MaximumLocalOffsetSeconds;
            long end = (added.LastDay.DayNumber + 1 - UnixEpochDay) * SecondsPerDay + MaximumLocalOffsetSeconds;
            return $"d.added_at >= {Bind(start)} AND d.added_at < {Bind(end)} AND " +
                $"plico_local_added_day(d.added_at) BETWEEN {Bind(added.FirstDay.DayNumber)} AND {Bind(added.LastDay.DayNumber)}";
        }
        if (clause.DocumentPeriod is { } dated)
            return $"coalesce(d.document_day BETWEEN {Bind(dated.FirstDay.DayNumber)} AND {Bind(dated.LastDay.DayNumber)}, 0)";
        throw new InvalidOperationException("A Boolean clause has no search term.");

        string Bind(object value)
        {
            string name = $"$boolean{command.Parameters.Count}";
            command.Parameters.AddWithValue(name, value);
            return name;
        }
    }

    // O(n) time and space for n excerpt characters.
    internal static (string Text, IReadOnlyList<SearchMatchSpan> Highlights) ReadSnippet(string excerpt, bool trimContext = true)
    {
        int firstHit = excerpt.IndexOf((char)SearchMatchStart);
        if (firstHit < 0) return ("", []);
        int beginning = 0;
        var text = new StringBuilder(excerpt.Length);
        if (trimContext && firstHit > SnippetLeadingCharacters)
        {
            int wordBreak = excerpt.IndexOf(' ', firstHit - SnippetLeadingCharacters);
            beginning = wordBreak >= 0 && wordBreak < firstHit ? wordBreak + 1 : firstHit;
            text.Append("… ");
        }
        var highlights = new List<SearchMatchSpan>();
        int start = -1;
        var characters = excerpt.AsSpan(beginning);
        if (trimContext) characters = characters.Trim();
        foreach (char character in characters)
        {
            switch (character)
            {
                case (char)SearchMatchStart:
                    start = text.Length;
                    break;
                case (char)SearchMatchEnd:
                    if (start >= 0 && start < text.Length) highlights.Add(new(start, text.Length - start));
                    start = -1;
                    break;
                default:
                    text.Append(character);
                    break;
            }
        }
        return (text.ToString(), highlights);
    }
}
