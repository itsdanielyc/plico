using System.IO.Compression;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Plico.Core;
using Xunit;

namespace Plico.Tests;

public sealed class LibraryMigrationTests
{
    private static string Fixture(string path) => Path.Combine(AppContext.BaseDirectory, "fixtures", path);

    [Fact]
    public void MigrationReportsProtectTheLibraryAndCanReplaceAnExternalReport()
    {
        using var directory = new TemporaryLibrary();
        using var external = new TemporaryLibrary();
        using var library = InboxLibrary.Open(directory.Path);
        var report = new LibraryMigrationReport(1, 0, 0, false, [new("Receipt", "Tag", "Review the imported tag.")]);
        string catalog = Path.Combine(directory.Path, ".plico", "catalog.sqlite3");
        byte[] original = File.ReadAllBytes(catalog);

        Assert.Throws<ArgumentException>(() => library.SaveMigrationReport(catalog, report));
        Assert.Equal(original, File.ReadAllBytes(catalog));

        string output = Path.Combine(external.Path, "my migration.json");
        File.WriteAllText(output, "older report");
        library.SaveMigrationReport(output, report);
        using var saved = JsonDocument.Parse(File.ReadAllBytes(output));
        Assert.Equal(report.Added, saved.RootElement.GetProperty("Added").GetInt32());
        Assert.Equal(report.Notices[0].Message, saved.RootElement.GetProperty("Notices")[0].GetProperty("Message").GetString());
        Assert.Equal(output, Assert.Single(Directory.EnumerateFileSystemEntries(external.Path)));
    }

    [Fact]
    public async Task PaperlessZipResolvesForwardLinksAndKeepsRecordsDuringARetry()
    {
        using var source = new TemporaryLibrary();
        using var destination = new TemporaryLibrary();
        var token = TestContext.Current.CancellationToken;
        string input = Path.Combine(source.Path, "paperless.zip");
        using (var archive = ZipFile.Open(input, ZipArchiveMode.Create))
        {
            archive.CreateEntryFromFile(Fixture("Lease Renewal.pdf"), "export/archive/lease.pdf");
            archive.CreateEntryFromFile(Fixture("Match Ledger.pdf"), "export/archive/ledger.pdf");
            await using var manifest = new StreamWriter(archive.CreateEntry("export/manifest.json").Open());
            await manifest.WriteAsync("""
                [
                  {"model":"documents.tag","pk":5,"fields":{"name":"Housing","color":"#264D37","is_inbox_tag":true}},
                  {"model":"documents.correspondent","pk":7,"fields":{"name":"Operating office","aliases":["Office"]}},
                  {"model":"documents.documenttype","pk":8,"fields":{"name":"Lease"}},
                  {"model":"documents.customfield","pk":9,"fields":{"name":"Related record","data_type":"documentlink"}},
                  {"model":"documents.customfield","pk":10,"fields":{"name":"Fee","data_type":"monetary"}},
                  {"model":"documents.note","pk":1,"fields":{"document":1,"note":"Renew by September"}},
                  {"model":"documents.document","pk":1,"__exported_archive_name":"archive/lease.pdf",
                   "fields":{"title":"Migrated lease","created":"2020-05-19","added":"2020-05-25T12:30:00Z","correspondent":7,
                     "document_type":8,"archive_serial_number":23015,"tags":[5],
                     "custom_fields":[{"field":9,"value":[2]},{"field":10,"value":"GBP45.50"}]}},
                  {"model":"documents.document","pk":2,"__exported_archive_name":"archive/ledger.pdf","fields":{"title":"Related ledger"}},
                  {"model":"documents.document","pk":3,"__exported_file_name":"missing.pdf","fields":{"title":"Missing source"}}
                ]
                """.AsMemory(), token);
        }
        using var library = InboxLibrary.Open(destination.Path);
        var migrated = await library.MigrateLibraryAsync(input, LibraryMigrationFormat.Paperless, token);
        Assert.Equal((2, 0, 1), (migrated.Added, migrated.Reused, migrated.Failed));
        var lease = Assert.IsType<DocumentSummary>(library.FindByArchiveSerialNumber(23015));
        Assert.Equal(new DateOnly(2020, 5, 19), lease.DocumentDate);
        Assert.False(lease.Reviewed);
        Assert.Contains("Housing", lease.Tags);
        var ledger = Assert.Single(library.Find(new DocumentFilter(Query: "Related ledger"), token).Items);
        var metadata = library.ReadDocumentMetadata(lease.Id);
        Assert.Equal("Renew by September", metadata.Notes);
        Assert.Contains(metadata.Fields, field => field.Value is CustomFieldValue.DocumentLink link && link.DocumentId == ledger.Id);
        Assert.Contains(metadata.Fields, field => field.Value is CustomFieldValue.Money { Amount: 45.50m, Currency: "GBP" });

        var retry = await library.MigrateLibraryAsync(input, LibraryMigrationFormat.Paperless, token);
        Assert.Equal((0, 2, 1), (retry.Added, retry.Reused, retry.Failed));
        Assert.Equal(lease.Id, library.FindByArchiveSerialNumber(23015)?.Id);
        Assert.True(File.Exists(input));
    }

    [Fact]
    public async Task EvernoteCopiesNoteTextTagsAndResources()
    {
        using var source = new TemporaryLibrary();
        using var destination = new TemporaryLibrary();
        var token = TestContext.Current.CancellationToken;
        string input = Path.Combine(source.Path, "Evernote.enex");
        await File.WriteAllTextAsync(input, """
            <?xml version="1.0" encoding="UTF-8"?>
            <!DOCTYPE en-export SYSTEM "http://xml.evernote.com/pub/evernote-export4.dtd">
            <en-export>
              <note><title>Travel note</title><created>20220908T130000Z</created><tag>Travel</tag>
                <content><![CDATA[<en-note><div>ENEXNOTE470</div></en-note>]]></content>
                <resource><data encoding="base64">UkVTT1VSQ0U0NzA=</data><mime>text/plain</mime>
                  <resource-attributes><file-name>proof.txt</file-name></resource-attributes></resource>
              </note>
            </en-export>
            """, token);
        using var library = InboxLibrary.Open(destination.Path);
        var report = await library.MigrateLibraryAsync(input, LibraryMigrationFormat.Evernote, token);
        Assert.Equal(1, report.Added);
        Assert.Equal(0, report.Failed);
        var document = Assert.Single(library.Find(new DocumentFilter(Query: "ENEXNOTE470"), token).Items);
        Assert.Equal(2, document.PageCount);
        Assert.Equal(new DateOnly(2022, 9, 8), document.DocumentDate);
        Assert.Contains("Travel", document.Tags);
        Assert.Contains("RESOURCE470", library.ReadIndexedText(document.Id));
        var retry = await library.MigrateLibraryAsync(input, LibraryMigrationFormat.Evernote, token);
        Assert.Equal(1, retry.Reused);
    }

    [Fact]
    public async Task MarinerMapsOrganizationAndReadsOriginalWithoutWritingToItsLibrary()
    {
        using var source = new TemporaryLibrary();
        using var destination = new TemporaryLibrary();
        var token = TestContext.Current.CancellationToken;
        string original = Path.Combine(source.Path, "original.pdf");
        File.Copy(Fixture("Lease Renewal.pdf"), original);
        string sourceDatabase = Path.Combine(source.Path, "DocumentWallet.documentwalletsql");
        using (var connection = new SqliteConnection(new SqliteConnectionStringBuilder
               { DataSource = sourceDatabase, Pooling = false }.ToString()))
        {
            await connection.OpenAsync(token);
            await using var command = connection.CreateCommand();
            command.CommandText = """
                CREATE TABLE Z_METADATA (Z_UUID TEXT);
                INSERT INTO Z_METADATA VALUES ('mariner-fixture-library');
                CREATE TABLE ZRECEIPT (Z_PK INTEGER PRIMARY KEY, ZORIGINALUID TEXT, ZPATH TEXT, ZTITLE TEXT,
                    ZORGANIZATION TEXT, ZINBOXVALUE INTEGER, ZDATE REAL, ZNOTES TEXT, ZCUSTOM1 TEXT);
                INSERT INTO ZRECEIPT VALUES (1, 'mariner-1', 'original.pdf', 'Old records',
                    'Archive organization', 0, 86400, 'Keep the notes', 'Unmapped custom value');
                """;
            await command.ExecuteNonQueryAsync(token);
        }
        var originals = await File.ReadAllBytesAsync(sourceDatabase, token);
        using var library = InboxLibrary.Open(destination.Path);
        var report = await library.MigrateLibraryAsync(source.Path, LibraryMigrationFormat.Mariner, token);
        Assert.Equal(1, report.Added);
        Assert.Equal(0, report.Failed);
        var document = Assert.Single(library.Find(new DocumentFilter(), token).Items);
        Assert.True(document.Reviewed);
        Assert.Equal(new DateOnly(2001, 1, 2), document.DocumentDate);
        Assert.Equal("Archive organization", Assert.Single(library.GetMetadataDefinitions().Correspondents).Name);
        Assert.Equal("Keep the notes", library.ReadDocumentMetadata(document.Id).Notes);
        Assert.Contains(report.Notices, notice => notice.OriginalValue == "Unmapped custom value");
        Assert.Equal(originals, await File.ReadAllBytesAsync(sourceDatabase, token));
        Assert.True(File.Exists(original));
    }
}
