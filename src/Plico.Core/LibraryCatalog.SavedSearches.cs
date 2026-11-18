using Microsoft.Data.Sqlite;
using SQLitePCL;

namespace Plico.Core;

internal sealed partial class LibraryCatalog
{
    private const string SavedSearchMigration = """
        CREATE TABLE saved_searches(
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            name TEXT NOT NULL,
            name_key TEXT NOT NULL UNIQUE,
            query TEXT NOT NULL,
            scope_tag_key TEXT REFERENCES tags(tag_key),
            sort INTEGER NOT NULL
        );
        CREATE INDEX saved_searches_by_tag ON saved_searches(scope_tag_key) WHERE scope_tag_key IS NOT NULL;
        """;

    private const string SavedSearchProjection = """
        id, name, query, (SELECT label FROM tags WHERE tag_key = saved_searches.scope_tag_key), sort
        """;

    // O(n) result rows in name index order, with one tag key lookup per scoped search.
    internal IReadOnlyList<SavedSearch> ReadSavedSearches()
    {
        using var connection = Connect();
        using var command = connection.CreateCommand();
        command.CommandText = $"SELECT {SavedSearchProjection} FROM saved_searches ORDER BY name_key;";
        using var reader = command.ExecuteReader();
        var searches = new List<SavedSearch>();
        while (reader.Read()) searches.Add(ReadSavedSearch(reader));
        return searches;
    }

    internal SavedSearch WriteSavedSearch(long? id, string name, string query, DocumentSort sort, string? scopeTagKey)
    {
        using var connection = Connect();
        using var command = connection.CreateCommand();
        command.Parameters.AddWithValue("$name", name);
        command.Parameters.AddWithValue("$key", name.ToUpperInvariant());
        command.Parameters.AddWithValue("$query", query);
        command.Parameters.AddWithValue("$sort", (int)sort);
        if (id is { } savedId)
        {
            command.Parameters.AddWithValue("$id", savedId);
            command.CommandText = $"""
                UPDATE saved_searches SET name=$name, name_key=$key, query=$query, sort=$sort
                WHERE id=$id RETURNING {SavedSearchProjection};
                """;
        }
        else
        {
            command.Parameters.AddWithValue("$scopeTagKey", (object?)scopeTagKey ?? DBNull.Value);
            command.CommandText = $"""
                INSERT INTO saved_searches(name, name_key, query, sort, scope_tag_key)
                VALUES($name, $key, $query, $sort, $scopeTagKey) RETURNING {SavedSearchProjection};
                """;
        }
        try
        {
            using var reader = command.ExecuteReader();
            if (!reader.Read()) throw new InvalidOperationException("This saved search is no longer available.");
            return ReadSavedSearch(reader);
        }
        catch (SqliteException error) when (error.SqliteExtendedErrorCode == raw.SQLITE_CONSTRAINT_UNIQUE)
        {
            throw new InvalidOperationException("A saved search with that name already exists.", error);
        }
        catch (SqliteException error) when (error.SqliteExtendedErrorCode == raw.SQLITE_CONSTRAINT_FOREIGNKEY)
        {
            throw new InvalidOperationException("The search’s sidebar tag is no longer available.", error);
        }
    }

    internal void DeleteSavedSearch(long id)
    {
        using var connection = Connect();
        using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM saved_searches WHERE id = $id;";
        command.Parameters.AddWithValue("$id", id);
        command.ExecuteNonQuery();
    }

    private static SavedSearch ReadSavedSearch(SqliteDataReader reader) => new(reader.GetInt64(0), reader.GetString(1),
        reader.GetString(2), reader.IsDBNull(3) ? null : reader.GetString(3), (DocumentSort)reader.GetInt32(4));
}
