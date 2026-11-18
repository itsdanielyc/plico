using System.Globalization;
using System.Text;
using Microsoft.Data.Sqlite;

namespace Plico.Core;

/// <summary>Reads the Core Data catalog and originals in a Mariner Paperless library.</summary>
internal static class MarinerMigration
{
    private const string FormatName = "mariner-paperless";
    private const string DatabaseFile = "DocumentWallet.documentwalletsql";
    private static readonly DateTimeOffset AppleEpoch = new(2001, 1, 1, 0, 0, 0, TimeSpan.Zero);

    internal static IEnumerable<MigrationEntry> Read(string source, CancellationToken cancellationToken)
    {
        string root = LocateLibrary(source);
        string database = MigrationFiles.LocateFile(root, DatabaseFile)
            ?? throw new InvalidDataException("Choose a Mariner .paperless package containing DocumentWallet.documentwalletsql.");
        var builder = new SqliteConnectionStringBuilder { DataSource = database, Mode = SqliteOpenMode.ReadOnly, Pooling = false };
        using var connection = new SqliteConnection(builder.ToString());
        connection.Open();
        var tables = ListTables(connection);
        string libraryKey = ReadLibraryKey(connection, tables, database);
        var types = ReadLabels(connection, tables, "ZDATATYPE");
        var categories = ReadLabels(connection, tables, "ZCATEGORY");
        var subcategories = ReadLabels(connection, tables, "ZSUBCATEGORY");
        var organizations = ReadLabels(connection, tables, "ZORGANIZATION");
        var tagNames = ReadLabels(connection, tables, "ZTAG");
        var tags = ReadReceiptTags(connection, tables, tagNames);

        using var command = connection.CreateCommand();
        command.CommandText = "SELECT * FROM ZRECEIPT ORDER BY Z_PK;";
        using var records = command.ExecuteReader();
        var columns = Enumerable.Range(0, records.FieldCount).ToDictionary(records.GetName, StringComparer.OrdinalIgnoreCase);
        while (records.Read())
        {
            cancellationToken.ThrowIfCancellationRequested();
            string id = Column(records, columns, "Z_PK") ?? throw new InvalidDataException("A Mariner record has no identifier.");
            string key = Column(records, columns, "ZORIGINALUID") ?? id;
            string? originalPath = Column(records, columns, "ZPATH");
            string title = Column(records, columns, "ZTITLE") ?? Column(records, columns, "ZMERCHANT") ??
                Column(records, columns, "ZORIGINALFILENAME") ?? $"Mariner record {id}";
            string? organization = Column(records, columns, "ZORGANIZATION");
            if (organization is not null && organizations.TryGetValue(organization, out var organizationName)) organization = organizationName;
            organization ??= Column(records, columns, "ZMERCHANT");
            string? inbox = Column(records, columns, "ZINBOXVALUE");
            bool? reviewed = inbox is null ? null : inbox == "0";
            var documentTags = tags.TryGetValue(id, out var assigned) ? assigned.ToList() : [];
            if (Label(categories, Column(records, columns, "ZCATEGORY")) is { } category)
            {
                string? sub = Label(subcategories, Column(records, columns, "ZSUBCATEGORY"));
                documentTags.Add(new ImportedTag(sub is null ? [category] : [category, sub]));
            }
            var notices = new List<LibraryMigrationNotice>();
            if (AppleDate(Column(records, columns, "ZIMPORTDATE")) is { } imported)
                notices.Add(new(title, "Source added", "Original Mariner intake timestamp", imported.ToString("O", CultureInfo.InvariantCulture)));
            var fields = ReadFields(records, columns, title, notices);
            var metadata = new ImportedDocumentMetadata(title, AppleDate(Column(records, columns, "ZDATE")) is { } date
                    ? DateOnly.FromDateTime(date.UtcDateTime) : null, reviewed, documentTags,
                organization is null ? [] : [new ImportedCorrespondent(organization, [])],
                Label(types, Column(records, columns, "ZDATATYPE")), Column(records, columns, "ZNOTES"), fields, null);
            yield return new MigrationEntry(new MigrationSourceKey(FormatName, libraryKey, key), metadata,
                (stage, token) => StageFileAsync(root, originalPath, stage, token), notices);
        }
    }

    private static string LocateLibrary(string source)
    {
        string root = Path.GetFullPath(source);
        if (!Directory.Exists(root)) throw new DirectoryNotFoundException("The Mariner library folder is unavailable.");
        if (File.Exists(Path.Combine(root, DatabaseFile))) return root;
        var children = Directory.EnumerateDirectories(root, "*.paperless",
            new EnumerationOptions { AttributesToSkip = FileAttributes.ReparsePoint }).Take(2).ToArray();
        return children.Length == 1 ? children[0] : root;
    }

    private static async Task<string> StageFileAsync(string root, string? relative, string stage, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(relative)) throw new FileNotFoundException("The Mariner record has no source file path.");
        string? file = MigrationFiles.LocateFile(root, relative) ?? MigrationFiles.LocateFile(root, GermanFilename(relative));
        if (file is null) throw new FileNotFoundException($"The Mariner original is missing: {relative}");
        await using var input = File.OpenRead(file);
        return await MigrationFiles.StageAsync(input, stage, file, cancellationToken).ConfigureAwait(false);
    }

    private static string GermanFilename(string value) => value.Normalize(NormalizationForm.FormC)
        .Replace("ä", "ae", StringComparison.Ordinal).Replace("ö", "oe", StringComparison.Ordinal)
        .Replace("ü", "ue", StringComparison.Ordinal).Replace("ß", "ss", StringComparison.Ordinal)
        .Replace("Ä", "Ae", StringComparison.Ordinal).Replace("Ö", "Oe", StringComparison.Ordinal)
        .Replace("Ü", "Ue", StringComparison.Ordinal);

    private static HashSet<string> ListTables(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT name FROM sqlite_schema WHERE type = 'table';";
        using var result = command.ExecuteReader();
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        while (result.Read()) names.Add(result.GetString(0));
        if (!names.Contains("ZRECEIPT")) throw new InvalidDataException("The Mariner catalog has no receipts table.");
        return names;
    }

    private static string ReadLibraryKey(SqliteConnection connection, HashSet<string> tables, string database)
    {
        if (tables.Contains("Z_METADATA"))
        {
            using var id = connection.CreateCommand();
            id.CommandText = "SELECT Z_UUID FROM Z_METADATA LIMIT 1;";
            if (id.ExecuteScalar() is string value && !string.IsNullOrWhiteSpace(value)) return value;
        }
        using var file = File.OpenRead(database);
        return MigrationFiles.StableKey(file);
    }

    private static Dictionary<string, string> ReadLabels(SqliteConnection connection, HashSet<string> tables, string table)
    {
        var labels = new Dictionary<string, string>(StringComparer.Ordinal);
        if (!tables.Contains(table)) return labels;
        using var command = connection.CreateCommand();
        command.CommandText = $"SELECT Z_PK, ZNAME FROM {table} WHERE ZNAME IS NOT NULL;";
        using var records = command.ExecuteReader();
        while (records.Read()) labels.Add(records.GetInt64(0).ToString(CultureInfo.InvariantCulture), records.GetString(1));
        return labels;
    }

    private static Dictionary<string, List<ImportedTag>> ReadReceiptTags(SqliteConnection connection, HashSet<string> tables,
        Dictionary<string, string> definitions)
    {
        var tags = new Dictionary<string, List<ImportedTag>>(StringComparer.Ordinal);
        if (!tables.Contains("Z_14TAGS")) return tags;
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT Z_14RECEIPTS1, Z_18TAGS FROM Z_14TAGS;";
        using var records = command.ExecuteReader();
        while (records.Read())
        {
            if (records.IsDBNull(0) || records.IsDBNull(1)) continue;
            string id = records.GetInt64(0).ToString(CultureInfo.InvariantCulture);
            if (Label(definitions, records.GetInt64(1).ToString(CultureInfo.InvariantCulture)) is not { } label) continue;
            if (!tags.TryGetValue(id, out var receiptTags)) tags.Add(id, receiptTags = []);
            receiptTags.Add(new ImportedTag([label]));
        }
        return tags;
    }

    private static List<ImportedCustomField> ReadFields(SqliteDataReader row, Dictionary<string, int> columns,
        string title, List<LibraryMigrationNotice> notices)
    {
        var fields = new List<ImportedCustomField>();
        if (decimal.TryParse(Column(row, columns, "ZAMOUNT"), NumberStyles.Float, CultureInfo.InvariantCulture, out decimal amount))
            fields.Add(new("Mariner amount", new ImportedFieldValue.Value(new CustomFieldValue.Money(amount, null))));
        foreach (string rawField in new[] { "ZAMOUNTASSTRING", "ZINTRASHVALUE", "ZPAYMENTMETHOD", "ZSHIPPINGAMOUNT",
                     "ZTAXAMOUNT", "ZTAXAMOUNT2", "ZTIPAMOUNT", "ZREIMBURSABLE", "ZCUSTOM1", "ZCUSTOM2", "ZCUSTOM3",
                     "ZCUSTOM4", "ZCUSTOM5", "ZCUSTOM6" })
        {
            if (Column(row, columns, rawField) is { } value)
                notices.Add(new(title, rawField, "Original Mariner value", value));
        }
        return fields;
    }

    private static string? Label(Dictionary<string, string> labels, string? id) =>
        id is not null && labels.TryGetValue(id, out var label) ? label : null;

    private static string? Column(SqliteDataReader row, Dictionary<string, int> columns, string name) =>
        columns.TryGetValue(name, out int index) && !row.IsDBNull(index) ? Convert.ToString(row.GetValue(index), CultureInfo.InvariantCulture) : null;

    private static DateTimeOffset? AppleDate(string? value)
    {
        if (!double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double seconds) || !double.IsFinite(seconds))
            return null;
        try { return AppleEpoch.AddSeconds(seconds); }
        catch (ArgumentOutOfRangeException) { return null; }
    }
}
