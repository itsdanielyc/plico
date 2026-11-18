using Microsoft.Data.Sqlite;

namespace Plico.Core;

internal sealed partial class LibraryCatalog
{
    private const int RgbColorLength = 7;

    internal IReadOnlyList<TagAppearance> ReadTagAppearances()
    {
        using var connection = Connect();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT t.label, parent.label, a.color_hex
            FROM tags t LEFT JOIN tag_appearance a ON a.tag_key = t.tag_key
            LEFT JOIN tags parent ON parent.tag_key = a.parent_key ORDER BY t.tag_key;
            """;
        using var rows = command.ExecuteReader();
        var tags = new List<TagAppearance>();
        while (rows.Read())
            tags.Add(new(rows.GetString(0), rows.IsDBNull(1) ? null : rows.GetString(1), rows.IsDBNull(2) ? null : rows.GetString(2)));
        return tags;
    }

    internal void SaveTagAppearance(string tag, string? parent, string? colorHex)
    {
        using var connection = Connect();
        using var transaction = connection.BeginTransaction();
        WriteTagAppearance(connection, transaction, tag, parent, colorHex);
        transaction.Commit();
    }

    private static void WriteTagAppearance(SqliteConnection connection, SqliteTransaction transaction, string tag, string? parent, string? colorHex)
    {
        string tagKey = EnsureMetadataTag(connection, transaction, tag);
        string? parentKey = parent is null ? null : EnsureMetadataTag(connection, transaction, parent);
        string? color = NormalizeTagColor(colorHex);
        if (parentKey is not null && TagIsAncestor(connection, transaction, tagKey, parentKey))
            throw new ArgumentException("Choose a parent outside this tag’s branch.");
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO tag_appearance (tag_key, parent_key, color_hex) VALUES ($tag, $parent, $color)
            ON CONFLICT(tag_key) DO UPDATE SET parent_key = excluded.parent_key, color_hex = excluded.color_hex;
            """;
        command.Parameters.AddWithValue("$tag", tagKey);
        command.Parameters.AddWithValue("$parent", DbValue(parentKey));
        command.Parameters.AddWithValue("$color", DbValue(color));
        command.ExecuteNonQuery();
    }

    private static string EnsureMetadataTag(SqliteConnection connection, SqliteTransaction transaction, string tag)
    {
        string label = SearchTerms.CleanLabel(tag);
        string key = SearchTerms.TagKey(label);
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "INSERT INTO tags (tag_key, label) VALUES ($key, $label) ON CONFLICT(tag_key) DO NOTHING;";
        command.Parameters.AddWithValue("$key", key);
        command.Parameters.AddWithValue("$label", label);
        command.ExecuteNonQuery();
        return key;
    }

    // O(h log n) catalog work for a tag branch of height h, with O(h) visited keys.
    private static bool TagIsAncestor(SqliteConnection connection, SqliteTransaction transaction, string ancestorKey, string candidateKey)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            WITH RECURSIVE ancestors(key) AS (
                SELECT $candidate UNION
                SELECT style.parent_key FROM ancestors a JOIN tag_appearance style ON style.tag_key = a.key
                WHERE style.parent_key IS NOT NULL
            )
            SELECT EXISTS (SELECT 1 FROM ancestors WHERE key = $ancestor);
            """;
        command.Parameters.AddWithValue("$candidate", candidateKey);
        command.Parameters.AddWithValue("$ancestor", ancestorKey);
        return (long)command.ExecuteScalar()! != 0;
    }

    private static void MoveTagAppearance(SqliteCommand command, bool destinationExists)
    {
        command.CommandText = """
            INSERT INTO tag_appearance (tag_key, parent_key, color_hex)
                SELECT $destinationKey, CASE WHEN $preserveParent THEN NULL ELSE parent_key END, color_hex
                FROM tag_appearance WHERE tag_key = $sourceKey
            ON CONFLICT(tag_key) DO UPDATE SET color_hex = COALESCE(tag_appearance.color_hex, excluded.color_hex);
            UPDATE tag_appearance SET parent_key = $destinationKey WHERE parent_key = $sourceKey;
            """;
        command.Parameters.AddWithValue("$preserveParent", destinationExists);
        command.ExecuteNonQuery();
        command.Parameters.RemoveAt("$preserveParent");
    }

    private static string? NormalizeTagColor(string? color)
    {
        if (color is null) return null;
        color = color.Trim();
        if (color.Length != RgbColorLength || color[0] != '#' || !color[1..].All(char.IsAsciiHexDigit))
            throw new ArgumentException("Enter a colour as #RRGGBB.");
        return color.ToUpperInvariant();
    }
}
