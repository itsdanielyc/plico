namespace Plico.Core;

internal abstract record SearchPredicate
{
    internal sealed record Clause(string Source, SearchTerms.CompiledSearch Value) : SearchPredicate;
    internal sealed record Exclude(SearchPredicate Operand) : SearchPredicate;
    internal sealed record All(SearchPredicate Left, SearchPredicate Right) : SearchPredicate;
    internal sealed record Either(SearchPredicate Left, SearchPredicate Right) : SearchPredicate;
}

internal sealed record BooleanSearch(SearchPredicate Root, string? NativeText, string PositiveText)
{
    // O(n²) worst-case string construction for a bounded query of n characters.
    internal string WithFolder(string clause)
    {
        var components = new List<string>();
        KeepOuterClauses(Root, components);
        components.Add(clause);
        return string.Join(' ', components);
    }

    private static void KeepOuterClauses(SearchPredicate node, List<string> components)
    {
        switch (node)
        {
            case SearchPredicate.All all:
                KeepOuterClauses(all.Left, components);
                KeepOuterClauses(all.Right, components);
                break;
            case SearchPredicate.Clause { Value.FolderPath: not null }:
                break;
            default:
                components.Add(Write(node));
                break;
        }
    }

    private static string Write(SearchPredicate node) => node switch
    {
        SearchPredicate.Clause clause => clause.Source,
        SearchPredicate.Exclude exclude => "-" + Write(exclude.Operand),
        SearchPredicate.All all => $"({Write(all.Left)} {Write(all.Right)})",
        SearchPredicate.Either either => $"({Write(either.Left)} OR {Write(either.Right)})",
        _ => throw new InvalidOperationException("Unknown search predicate.")
    };
}

internal static partial class SearchTerms
{
    private enum TokenKind { Clause, Open, Close, And, Or, Not }
    private readonly record struct Token(TokenKind Kind, string Text);

    // O(n²) time for bounded FTS string construction, and O(n) space for tokens and predicates.
    private static CompiledSearch? CompileBoolean(string query)
    {
        if (query.AsSpan().IndexOfAny('(', ')', '-') < 0 && !query.Contains("OR", StringComparison.Ordinal) &&
            !query.Contains("AND", StringComparison.Ordinal) && !query.Contains("NOT", StringComparison.Ordinal)) return null;

        var tokens = ReadBooleanTokens(query);
        if (!tokens.Any(token => token.Kind != TokenKind.Clause)) return null;
        var parser = new BooleanParser(tokens);
        var root = parser.Parse();
        if (root is null) return InvalidSearch(parser.Issue ?? SearchQueryIssue.BooleanSyntax);
        string? native = NativeExpression(root);
        var positive = new HashSet<string>(StringComparer.Ordinal);
        CollectPositiveText(root, positive, excluded: false);
        string projected = string.Join(" OR ", positive);
        return new CompiledSearch(native ?? projected, [], null, null, null, null, null, null)
        {
            Advanced = new(root, native, projected)
        };
    }

    private static List<Token> ReadBooleanTokens(string query)
    {
        var tokens = new List<Token>();
        for (int cursor = 0; cursor < query.Length;)
        {
            char current = query[cursor];
            if (char.IsWhiteSpace(current))
            {
                cursor++;
                continue;
            }
            TokenKind kind = current switch
            {
                '(' => TokenKind.Open,
                ')' => TokenKind.Close,
                '-' => TokenKind.Not,
                _ => TokenKind.Clause
            };
            if (kind != TokenKind.Clause)
            {
                tokens.Add(new(kind, ""));
                cursor++;
                continue;
            }

            int start = cursor;
            do
            {
                if (ClosingQuote(query[cursor++]) is not char closing) continue;
                while (cursor < query.Length)
                {
                    char quoted = query[cursor++];
                    if (quoted == closing) break;
                    if (quoted == '\\' && cursor < query.Length && (query[cursor] == closing || query[cursor] == '\\'))
                        cursor++;
                }
            } while (cursor < query.Length && !char.IsWhiteSpace(query[cursor]) && query[cursor] is not ('(' or ')'));

            string atom = query[start..cursor];
            tokens.Add(new(atom switch
            {
                "AND" => TokenKind.And,
                "OR" => TokenKind.Or,
                "NOT" => TokenKind.Not,
                _ => TokenKind.Clause
            }, atom));
        }
        return tokens;
    }

    // FTS5 uses binary NOT. A conjunction with one excluded operand can stay entirely in FTS.
    private static string? NativeExpression(SearchPredicate node) => node switch
    {
        SearchPredicate.Clause { Value.FullTextExpression.Length: > 0 } clause => clause.Value.FullTextExpression,
        SearchPredicate.All { Right: SearchPredicate.Exclude excluded } all =>
            CombineNative(all.Left, excluded.Operand, "NOT"),
        SearchPredicate.All { Left: SearchPredicate.Exclude excluded } all =>
            CombineNative(all.Right, excluded.Operand, "NOT"),
        SearchPredicate.All all => CombineNative(all.Left, all.Right, "AND"),
        SearchPredicate.Either either => CombineNative(either.Left, either.Right, "OR"),
        _ => null
    };

    private static string? CombineNative(SearchPredicate left, SearchPredicate right, string operation)
    {
        if (NativeExpression(left) is not { } first || NativeExpression(right) is not { } second) return null;
        return $"({first} {operation} {second})";
    }

    private static void CollectPositiveText(SearchPredicate node, HashSet<string> positive, bool excluded)
    {
        switch (node)
        {
            case SearchPredicate.Clause clause when !excluded:
                foreach (System.Text.RegularExpressions.Match match in FtsPhrases().Matches(clause.Value.FullTextExpression))
                    positive.Add(match.Value);
                break;
            case SearchPredicate.Exclude exclude:
                CollectPositiveText(exclude.Operand, positive, !excluded);
                break;
            case SearchPredicate.All all:
                CollectPositiveText(all.Left, positive, excluded);
                CollectPositiveText(all.Right, positive, excluded);
                break;
            case SearchPredicate.Either either:
                CollectPositiveText(either.Left, positive, excluded);
                CollectPositiveText(either.Right, positive, excluded);
                break;
        }
    }

    private sealed class BooleanParser(List<Token> tokens)
    {
        private int _cursor;
        private TokenKind? Next => _cursor < tokens.Count ? tokens[_cursor].Kind : null;
        internal SearchQueryIssue? Issue { get; private set; }

        internal SearchPredicate? Parse()
        {
            var root = ParseAlternatives();
            return _cursor == tokens.Count ? root : null;
        }

        private SearchPredicate? ParseAlternatives()
        {
            var result = ParseConjunction();
            while (result is not null && Take(TokenKind.Or))
            {
                if (ParseConjunction() is not { } right) return null;
                result = new SearchPredicate.Either(result, right);
            }
            return result;
        }

        private SearchPredicate? ParseConjunction()
        {
            var result = ParseOperand();
            while (result is not null && Next is not (null or TokenKind.Or or TokenKind.Close))
            {
                Take(TokenKind.And);
                if (ParseOperand() is not { } right) return null;
                result = new SearchPredicate.All(result, right);
            }
            return result;
        }

        private SearchPredicate? ParseOperand()
        {
            if (Take(TokenKind.Not))
                return ParseOperand() is { } operand ? new SearchPredicate.Exclude(operand) : null;
            if (Take(TokenKind.Open))
            {
                var group = ParseAlternatives();
                return Take(TokenKind.Close) ? group : null;
            }
            if (Next != TokenKind.Clause) return null;

            string atom = tokens[_cursor++].Text;
            var clause = CompileLegacy(atom);
            if (clause.IsValid) return new SearchPredicate.Clause(atom, clause);
            Issue = clause.QueryIssue == SearchQueryIssue.NoSearchTerms ? SearchQueryIssue.BooleanSyntax : clause.QueryIssue;
            return null;
        }

        private bool Take(TokenKind kind)
        {
            if (Next != kind) return false;
            _cursor++;
            return true;
        }
    }
}
