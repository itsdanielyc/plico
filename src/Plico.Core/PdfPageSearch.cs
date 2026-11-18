using Microsoft.Data.Sqlite;

namespace Plico.Core;

/// <summary>Matches the full library query against the embedded text of individual PDF pages.</summary>
internal static class PdfPageSearch
{
    private const string PageIndex = $"""
        CREATE VIRTUAL TABLE page_search USING fts5(body, content='', tokenize='{SearchTerms.FtsTokenizer}');
        """;

    private const string MatchPage = """
        INSERT INTO page_search(page_search) VALUES ('delete-all');
        INSERT INTO page_search(rowid, body) VALUES (1, $body);
        SELECT EXISTS (SELECT rowid FROM page_search WHERE page_search MATCH $expression);
        """;

    // After text extraction, expected O(processed text) time and O(largest page text) scratch space.
    internal static int? FindFirstMatch(string path, string expression, CancellationToken cancellationToken) =>
        DocumentProcessor.WithReadablePdf<int?>(path, pdf =>
        {
            using var connection = new SqliteConnection("Data Source=:memory:;Pooling=False");
            connection.Open();
            using (var create = connection.CreateCommand())
            {
                create.CommandText = PageIndex;
                create.ExecuteNonQuery();
            }

            using var match = connection.CreateCommand();
            match.CommandText = MatchPage;
            var body = match.Parameters.Add("$body", SqliteType.Text);
            match.Parameters.AddWithValue("$expression", expression);
            match.Prepare();

            foreach (var page in pdf.GetPages())
            {
                cancellationToken.ThrowIfCancellationRequested();
                string text = DocumentProcessor.ReadPageText(page);
                if (text.Length == 0) continue;
                body.Value = text;
                var found = (long)match.ExecuteScalar()! == 1;
                cancellationToken.ThrowIfCancellationRequested();
                if (found) return page.Number;
            }
            return null;
        });
}
