using System.Text;
using Microsoft.Data.Sqlite;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.Core;

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

    private static readonly string FindHits = $"""
        CREATE VIRTUAL TABLE page_hits USING fts5(body, tokenize='{SearchTerms.FtsTokenizer}');
        INSERT INTO page_hits(body) VALUES ($body);
        SELECT highlight(page_hits, 0, char({LibraryCatalog.SearchMatchStart}), char({LibraryCatalog.SearchMatchEnd}))
        FROM page_hits WHERE page_hits MATCH $expression;
        """;

    // O(words + page text) time and space, plus the PDF parser and its spatial word extraction.
    internal static IReadOnlyList<PdfPageHighlight> HighlightPage(string path, int number, string expression, CancellationToken token) =>
        DocumentProcessor.WithReadablePdf<IReadOnlyList<PdfPageHighlight>>(path, pdf =>
        {
            var page = pdf.GetPage(number);
            if (page.Width <= 0 || page.Height <= 0) return [];
            var body = new StringBuilder();
            var positions = new List<(int Start, int End, PdfRectangle Bounds)>();
            foreach (Word word in page.GetWords())
            {
                token.ThrowIfCancellationRequested();
                if (body.Length > 0) body.Append(' ');
                int offset = body.Length;
                body.Append(word.Text);
                positions.Add((offset, body.Length, word.BoundingBox));
            }
            if (positions.Count == 0) return [];

            using var connection = new SqliteConnection("Data Source=:memory:;Pooling=False");
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = FindHits;
            command.Parameters.AddWithValue("$body", body.ToString());
            command.Parameters.AddWithValue("$expression", expression);
            if (command.ExecuteScalar() is not string marked) return [];
            var (_, hits) = LibraryCatalog.ReadSnippet(marked, trimContext: false);
            return MapHits(page, positions, hits, token);
        });

    private static List<PdfPageHighlight> MapHits(Page page, List<(int Start, int End, PdfRectangle Bounds)> words,
        IReadOnlyList<SearchMatchSpan> hits, CancellationToken token)
    {
        var matches = new List<PdfPageHighlight>();
        int wordIndex = 0;
        foreach (var hit in hits)
        {
            token.ThrowIfCancellationRequested();
            while (wordIndex < words.Count && words[wordIndex].End <= hit.Start) wordIndex++;
            for (int index = wordIndex; index < words.Count && words[index].Start < hit.Start + hit.Length; index++)
            {
                if (Normalize(words[index].Bounds, page.Width, page.Height) is { } rectangle)
                    matches.Add(rectangle);
            }
        }
        return matches;
    }

    private static PdfPageHighlight? Normalize(PdfRectangle box, double width, double height)
    {
        double left = Math.Clamp(Math.Min(Math.Min(box.BottomLeft.X, box.TopLeft.X), Math.Min(box.BottomRight.X, box.TopRight.X)) / width, 0, 1);
        double right = Math.Clamp(Math.Max(Math.Max(box.BottomLeft.X, box.TopLeft.X), Math.Max(box.BottomRight.X, box.TopRight.X)) / width, 0, 1);
        double top = Math.Clamp(1 - Math.Max(Math.Max(box.BottomLeft.Y, box.TopLeft.Y), Math.Max(box.BottomRight.Y, box.TopRight.Y)) / height, 0, 1);
        double bottom = Math.Clamp(1 - Math.Min(Math.Min(box.BottomLeft.Y, box.TopLeft.Y), Math.Min(box.BottomRight.Y, box.TopRight.Y)) / height, 0, 1);
        return right > left && bottom > top ? new PdfPageHighlight(left, top, right - left, bottom - top) : null;
    }

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
