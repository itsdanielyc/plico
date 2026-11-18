using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace Plico.Core;

internal sealed record FilingInput(long Id, string Title, string Text, IReadOnlyList<string> Tags, DateOnly? Date,
    OcrLanguage? Language);

internal sealed partial class LibraryCatalog
{
    private const string FilingMigration = """
        CREATE TABLE IF NOT EXISTS tag_rules (
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            tag TEXT NOT NULL,
            tag_key TEXT NOT NULL,
            pattern TEXT NOT NULL,
            match_mode INTEGER NOT NULL
        );
        CREATE INDEX IF NOT EXISTS tag_rules_by_tag ON tag_rules(tag_key);
        CREATE TABLE IF NOT EXISTS document_ocr_languages (
            document_id INTEGER PRIMARY KEY REFERENCES documents(id) ON DELETE CASCADE,
            language TEXT NOT NULL
        );
        """;

    private const string FilingTagsSql = """
        (SELECT json_group_array(t.label ORDER BY t.tag_key)
         FROM document_tags dt JOIN tags t USING (tag_key) WHERE dt.document_id = d.id)
        """;

    internal IReadOnlyList<FilingRule> ReadFilingRules()
    {
        using var connection = Connect();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT id, tag, pattern, match_mode FROM tag_rules ORDER BY id;";
        using var rows = command.ExecuteReader();
        var rules = new List<FilingRule>();
        while (rows.Read()) rules.Add(new(rows.GetInt64(0), rows.GetString(1), rows.GetString(2), (FilingRuleMatch)rows.GetInt32(3)));
        return rules;
    }

    internal FilingRule SaveFilingRule(FilingRule rule)
    {
        string tag = SearchTerms.CleanLabel(rule.Tag);
        string pattern = rule.Pattern.Trim();
        if (!Enum.IsDefined(rule.Match) || pattern.Length is 0 or > SearchTerms.MaximumQueryLength ||
            pattern.Any(char.IsControl) || !SearchTerms.WordTokens().IsMatch(pattern))
            throw new ArgumentException($"Enter matching words or a phrase of up to {SearchTerms.MaximumQueryLength} characters.");
        using var connection = Connect();
        using var command = connection.CreateCommand();
        command.CommandText = rule.Id is null
            ? "INSERT INTO tag_rules (tag, tag_key, pattern, match_mode) VALUES ($tag, $key, $pattern, $mode) RETURNING id;"
            : "UPDATE tag_rules SET tag = $tag, tag_key = $key, pattern = $pattern, match_mode = $mode WHERE id = $id RETURNING id;";
        command.Parameters.AddWithValue("$tag", tag);
        command.Parameters.AddWithValue("$key", SearchTerms.TagKey(tag));
        command.Parameters.AddWithValue("$pattern", pattern);
        command.Parameters.AddWithValue("$mode", (int)rule.Match);
        if (rule.Id is long id) command.Parameters.AddWithValue("$id", id);
        if (command.ExecuteScalar() is not long saved) throw new InvalidOperationException("This tag rule no longer exists.");
        return new(saved, tag, pattern, rule.Match);
    }

    internal void DeleteFilingRule(long id)
    {
        using var connection = Connect();
        using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM tag_rules WHERE id = $id;";
        command.Parameters.AddWithValue("$id", id);
        command.ExecuteNonQuery();
    }

    internal Dictionary<long, List<FilingTag>> MatchFilingRules(IReadOnlyList<long> ids, CancellationToken token)
    {
        var matches = new Dictionary<long, List<FilingTag>>();
        var rules = ReadFilingRules();
        if (rules.Count == 0) return matches;
        using var connection = Connect();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT rowid FROM documents_search
            WHERE documents_search MATCH $pattern AND rowid IN (SELECT value FROM json_each($ids));
            """;
        BindSelection(command, ids);
        var pattern = command.Parameters.Add("$pattern", SqliteType.Text);
        foreach (var rule in rules)
        {
            token.ThrowIfCancellationRequested();
            var words = SearchTerms.WordTokens().Matches(rule.Pattern).Select(match => match.Value).ToArray();
            pattern.Value = rule.Match switch
            {
                FilingRuleMatch.Phrase => $"\"{string.Join(' ', words)}\"",
                FilingRuleMatch.AnyWord => string.Join(" OR ", words.Select(word => $"\"{word}\"")),
                FilingRuleMatch.AllWords => string.Join(" AND ", words.Select(word => $"\"{word}\"")),
                _ => throw new InvalidDataException("A tag rule uses an unknown matching mode.")
            };
            using var rows = command.ExecuteReader();
            while (rows.Read())
            {
                token.ThrowIfCancellationRequested();
                long id = rows.GetInt64(0);
                if (!matches.TryGetValue(id, out var list)) matches.Add(id, list = []);
                list.Add(new(rule.Tag, FilingTagSource.Rule));
            }
        }
        return matches;
    }

    internal IEnumerable<(string Text, string[] Tags)> ReadFilingTrainingExamples(CancellationToken token)
    {
        using var connection = Connect();
        using var command = connection.CreateCommand();
        command.CommandText = $"""
            SELECT d.title || char(10) || substr(d.body, 1, $length), {FilingTagsSql}
            FROM documents d WHERE d.is_present = 1 AND d.reviewed = 1 AND d.has_text = 1;
            """;
        command.Parameters.AddWithValue("$length", FilingTagClassifier.MaximumTrainingCharacters);
        using var rows = command.ExecuteReader();
        while (rows.Read())
        {
            token.ThrowIfCancellationRequested();
            yield return (rows.GetString(0), JsonSerializer.Deserialize<string[]>(rows.GetString(1)) ?? []);
        }
    }

    internal IEnumerable<FilingInput> ReadFilingInputs(IReadOnlyList<long> ids, CancellationToken token,
        int maximumCharacters = FilingFieldExtractor.MaximumTextCharacters)
    {
        using var connection = Connect();
        using var command = connection.CreateCommand();
        command.CommandText = $"""
            SELECT d.id, COALESCE(d.display_title, d.title), substr(d.body, 1, $length),
                   {FilingTagsSql}, d.document_day, COALESCE(language.language, d.ocr_copy_language)
            FROM json_each($ids) selection JOIN documents d ON d.id = selection.value
            LEFT JOIN document_ocr_languages language ON language.document_id = d.id
            WHERE d.is_present = 1 ORDER BY selection.key;
            """;
        int count = BindSelection(command, ids);
        command.Parameters.AddWithValue("$length", maximumCharacters);
        using var rows = command.ExecuteReader();
        int available = 0;
        while (rows.Read())
        {
            token.ThrowIfCancellationRequested();
            available++;
            OcrLanguage? language = !rows.IsDBNull(5) && Enum.TryParse<OcrLanguage>(rows.GetString(5), out var parsed) ? parsed : null;
            yield return new(rows.GetInt64(0), rows.GetString(1), rows.GetString(2),
                JsonSerializer.Deserialize<string[]>(rows.GetString(3)) ?? [],
                rows.IsDBNull(4) ? null : DateOnly.FromDayNumber(rows.GetInt32(4)), language);
        }
        RequireSelection(available, count);
    }

    internal void ApplyFilingSelections(IReadOnlyList<FilingSelection> selections, CancellationToken token)
    {
        if (selections.Count == 0) return;
        using var connection = Connect();
        using var transaction = connection.BeginTransaction();
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        BindAndRequirePresentSelection(command, selections.Select(selection => selection.DocumentId).ToArray());
        foreach (var selection in selections)
        {
            token.ThrowIfCancellationRequested();
            command.Parameters.Clear();
            command.Parameters.AddWithValue("$id", selection.DocumentId);
            if (selection.DocumentDate is { } date)
            {
                command.CommandText = "UPDATE documents SET document_day = $day WHERE id = $id;";
                command.Parameters.AddWithValue("$day", date.DayNumber);
                command.ExecuteNonQuery();
                command.Parameters.RemoveAt("$day");
            }
            command.CommandText = """
                INSERT OR IGNORE INTO tags (tag_key, label) VALUES ($key, $label);
                INSERT OR IGNORE INTO document_tags (tag_key, document_id) VALUES ($key, $id);
                """;
            var key = command.Parameters.Add("$key", SqliteType.Text);
            var label = command.Parameters.Add("$label", SqliteType.Text);
            foreach (string tag in selection.Tags)
            {
                string clean = SearchTerms.CleanLabel(tag);
                key.Value = SearchTerms.TagKey(clean);
                label.Value = clean;
                command.ExecuteNonQuery();
            }
        }
        token.ThrowIfCancellationRequested();
        transaction.Commit();
    }
}
