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

    // O(visited pages + their processed text) time and O(largest page text) scratch space, plus PDF parser state.
    internal static int? FindMatch(string path, string expression, int anchorPage, PageMatchDirection direction,
        CancellationToken cancellationToken) =>
        DocumentProcessor.WithReadablePdf<int?>(path, pdf =>
        {
            bool forward = direction == PageMatchDirection.Forward;
            int pageCount = pdf.NumberOfPages;
            if (forward ? anchorPage >= pageCount : anchorPage <= 1) return null;
            int step = forward ? 1 : -1;
            int firstPage = forward ? anchorPage + step : Math.Min(anchorPage + step, pageCount);

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

            for (int page = firstPage; page >= 1 && page <= pageCount; page += step)
            {
                cancellationToken.ThrowIfCancellationRequested();
                string text = DocumentProcessor.ReadPageText(pdf.GetPage(page));
                if (text.Length == 0) continue;
                body.Value = text;
                var found = (long)match.ExecuteScalar()! == 1;
                cancellationToken.ThrowIfCancellationRequested();
                if (found) return page;
            }
            return null;
        });
}
