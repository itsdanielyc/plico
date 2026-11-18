using System.Text.Json;
using Plico.Core;
using Xunit;

namespace Plico.Tests;

public sealed partial class LibraryTests
{
    [Fact]
    public async Task IntegrityCheckReportsChangesWithoutReindexingAndReportsExportMetadata()
    {
        var token = TestContext.Current.CancellationToken;
        using var temporary = new TemporaryLibrary();
        using var target = new TemporaryLibrary();
        using var library = InboxLibrary.Open(temporary.Path);
        var letter = await library.ImportAsync(Fixture(Letter), token);
        var ledger = await library.ImportAsync(Fixture("Match Ledger.pdf"), token);
        library.AddTag(letter.Id, "Property, taxes");
        library.SetTitle(letter.Id, "=formula");
        library.SetDocumentDate(letter.Id, new DateOnly(2026, 4, 3));
        File.AppendAllText(library.ResolvePdf(letter), "\n");
        File.Delete(library.ResolvePdf(ledger));
        const string newFilename = "Unindexed.pdf";
        File.Copy(Fixture(Letter), Path.Combine(library.PdfDirectory, newFilename));

        var result = library.CheckIntegrity(token);
        Assert.Contains(new IntegrityIssue(letter.RelativePath, IntegrityIssueKind.ChecksumMismatch), result.Issues);
        Assert.Contains(new IntegrityIssue(ledger.RelativePath, IntegrityIssueKind.Missing), result.Issues);
        Assert.Contains(new IntegrityIssue(newFilename, IntegrityIssueKind.Unindexed), result.Issues);
        Assert.Equal(2, library.Find(new DocumentFilter(), token).Items.Count);

        var jsonPath = await library.ExportMetadataAsync(target.Path, token);
        using var json = JsonDocument.Parse(await File.ReadAllTextAsync(jsonPath, token));
        var entries = json.RootElement.GetProperty("documents");
        Assert.Equal("2026-04-03", entries[0].GetProperty("documentDate").GetString());
        Assert.Equal("Property, taxes", entries[0].GetProperty("tags")[0].GetString());
        var csvPath = await library.ExportSearchCsvAsync(new DocumentFilter(Query: "tag:\"Property, taxes\""), target.Path, token);
        string csv = await File.ReadAllTextAsync(csvPath, token);
        Assert.Contains("\"'=formula\"", csv);
        Assert.Contains("\"Property, taxes\"", csv);
        Assert.DoesNotContain(ledger.RelativePath, csv);
    }
}
