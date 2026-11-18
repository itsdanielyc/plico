namespace Plico.Core;

internal sealed record BinderSelection(CatalogFileReference File, string Title);

internal sealed partial class LibraryCatalog
{
    internal IReadOnlyList<BinderSelection> ReadBinderSelection(IReadOnlyList<long> documentIds)
    {
        using var connection = Connect();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT d.id, d.relative_path, d.modified_ticks, d.file_bytes, COALESCE(d.display_title, d.title)
            FROM json_each($ids) selection JOIN documents d ON d.id = selection.value
            WHERE d.is_present = 1 ORDER BY selection.key;
            """;
        int expected = BindSelection(command, documentIds);
        using var reader = command.ExecuteReader();
        var selected = new List<BinderSelection>(expected);
        while (reader.Read())
        {
            var version = new FileVersion(reader.GetInt64(2), reader.GetInt64(3), Present: true);
            selected.Add(new BinderSelection(new CatalogFileReference(reader.GetInt64(0), reader.GetString(1), version),
                reader.GetString(4)));
        }
        RequireSelection(selected.Count, expected);
        return selected;
    }
}
