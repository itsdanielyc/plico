using Plico.Core;
using System.Runtime.CompilerServices;
using NAPS2.Images;
using NAPS2.ImportExport;
using NAPS2.Scan;
using Microsoft.Data.Sqlite;
using UglyToad.PdfPig;
using UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor;
using Xunit;

namespace Plico.Tests;

public sealed partial class LibraryTests
{
    private const string Receipt = "Hardware Store Receipt.png";
    private const string Letter = "Lease Renewal.pdf";
    private const string Mixed = "Mixed Pages.pdf";
    private const string ReceiptKeyword = "WRENCH470";
    private const string LetterKeyword = "NORTHSTAR";

    [Fact]
    public async Task CancelledCatalogReadLeavesTheLibraryAvailableForTheNextSearch()
    {
        using var temporary = new TemporaryLibrary();
        using var library = InboxLibrary.Open(temporary.Path);
        var document = await library.ImportAsync(Fixture(Letter), TestContext.Current.CancellationToken);
        var filter = new DocumentFilter(Query: LetterKeyword);
        var cancelled = new CancellationToken(canceled: true);

        Assert.Throws<OperationCanceledException>(() => library.Find(filter, cancelled));

        Assert.Equal(document.Id, Assert.Single(library.Find(filter, TestContext.Current.CancellationToken).Items).Id);
    }

    [Fact]
    public async Task ReadingIndexedTextRejectsAnEntryMarkedMissing()
    {
        using var temporary = new TemporaryLibrary();
        using var library = InboxLibrary.Open(temporary.Path);
        var document = await library.ImportAsync(Fixture(Letter), TestContext.Current.CancellationToken);
        Assert.Contains(LetterKeyword, library.ReadIndexedText(document.Id), StringComparison.OrdinalIgnoreCase);

        File.Delete(library.ResolvePdf(document));
        library.Refresh(TestContext.Current.CancellationToken);

        Assert.Throws<InvalidOperationException>(() => library.ReadIndexedText(document.Id));
    }

    [Fact]
    public async Task DigitalPdfStaysIntactAndSearchable()
    {
        using var temporary = new TemporaryLibrary();
        using var library = InboxLibrary.Open(temporary.Path);
        var imported = await library.ImportAsync(Fixture(Letter), TestContext.Current.CancellationToken);

        Assert.Equal(File.ReadAllBytes(Fixture(Letter)), File.ReadAllBytes(library.ResolvePdf(imported)));
        Assert.Equal(imported.Id, Assert.Single(library.Find(new DocumentFilter(Query: LetterKeyword), TestContext.Current.CancellationToken).Items).Id);
    }

    [Fact]
    public async Task ImageImportEmbedsTextInAnOrdinaryPdf()
    {
        using var temporary = new TemporaryLibrary();
        using var library = InboxLibrary.Open(temporary.Path);
        var imported = await library.ImportAsync(Fixture(Receipt), TestContext.Current.CancellationToken);
        using var pdf = PdfDocument.Open(library.ResolvePdf(imported));

        Assert.Contains(ReceiptKeyword, ContentOrderTextExtractor.GetText(pdf.GetPage(1)), StringComparison.OrdinalIgnoreCase);
        var found = Assert.Single(library.Find(new DocumentFilter(Query: ReceiptKeyword), TestContext.Current.CancellationToken).Items);
        Assert.Equal(imported.Id, found.Id);
        Assert.InRange(found.Match.IndexOf(ReceiptKeyword, StringComparison.OrdinalIgnoreCase), 0, 22);
        Assert.True(File.Exists(Fixture(Receipt)));
    }

    [Fact]
    public async Task MixedPdfRetainsDigitalTextAndAddsOcrOnImagePage()
    {
        using var temporary = new TemporaryLibrary();
        using var library = InboxLibrary.Open(temporary.Path);
        var imported = await library.ImportAsync(Fixture(Mixed), TestContext.Current.CancellationToken);
        using var pdf = PdfDocument.Open(library.ResolvePdf(imported));

        Assert.Equal(2, pdf.NumberOfPages);
        Assert.Contains(LetterKeyword, ContentOrderTextExtractor.GetText(pdf.GetPage(1)), StringComparison.OrdinalIgnoreCase);
        Assert.Contains(ReceiptKeyword, ContentOrderTextExtractor.GetText(pdf.GetPage(2)), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task MatchingPageUsesEmbeddedTextAndKeepsQueryTermsOnTheSamePage()
    {
        using var temporary = new TemporaryLibrary();
        using var library = InboxLibrary.Open(temporary.Path);
        var token = TestContext.Current.CancellationToken;
        var document = await library.ImportAsync(Fixture(Mixed), token);
        library.SetTitle(document.Id, "December archive");

        Assert.Equal(2, library.FindFirstMatchingPage(document, "\"wrench470\"", token));
        Assert.Equal(1, library.FindFirstMatchingPage(document, "\"new term\" norths", token));
        Assert.Single(library.Find(new DocumentFilter(Query: "norths wrench"), token).Items);
        Assert.Null(library.FindFirstMatchingPage(document, "norths wrench", token));
        Assert.Null(library.FindFirstMatchingPage(document, "December archive", token));
        Assert.Throws<OperationCanceledException>(() => library.FindFirstMatchingPage(document, "wrench", new CancellationToken(true)));
    }

    [Fact]
    public async Task AdjacentMatchingPagesExcludeTheAnchorAndStopAtTheDocumentEnds()
    {
        const string phrase = "\"ledger renewal\"";
        using var temporary = new TemporaryLibrary();
        using var library = InboxLibrary.Open(temporary.Path);
        var token = TestContext.Current.CancellationToken;
        var document = await library.ImportAsync(Fixture("Match Ledger.pdf"), token);

        Assert.Equal(2, library.FindFirstMatchingPage(document, phrase, token));
        Assert.Equal(4, library.FindAdjacentMatchingPage(document, phrase, 2, PageMatchDirection.Forward, token));
        Assert.Equal(2, library.FindAdjacentMatchingPage(document, phrase, 4, PageMatchDirection.Backward, token));
        Assert.Null(library.FindAdjacentMatchingPage(document, phrase, 4, PageMatchDirection.Forward, token));
        Assert.Null(library.FindAdjacentMatchingPage(document, phrase, 2, PageMatchDirection.Backward, token));
        Assert.Null(library.FindAdjacentMatchingPage(document, "tag:ledger", 2, PageMatchDirection.Forward, token));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            library.FindAdjacentMatchingPage(document, phrase, 0, PageMatchDirection.Forward, token));
        Assert.Throws<OperationCanceledException>(() =>
            library.FindAdjacentMatchingPage(document, phrase, 2, PageMatchDirection.Forward, new CancellationToken(true)));
    }

    [Fact]
    public async Task MalformedPdfDoesNotPublishAFileOrRow()
    {
        using var temporary = new TemporaryLibrary();
        string source = System.IO.Path.Combine(temporary.Path, "Corrupt.pdf");
        await File.WriteAllTextAsync(source, "A synthetic invalid PDF.", TestContext.Current.CancellationToken);
        using var library = InboxLibrary.Open(temporary.Path);

        await Assert.ThrowsAsync<InvalidDataException>(() => library.ImportAsync(source, TestContext.Current.CancellationToken));
        Assert.Empty(library.Find(new DocumentFilter(), TestContext.Current.CancellationToken).Items);
        Assert.Empty(Directory.EnumerateFiles(library.PdfDirectory));
        Assert.True(File.Exists(source));
    }

    [Fact]
    public async Task TagsAndReviewStateSurviveMovingTheLibrary()
    {
        using var temporary = new TemporaryLibrary();
        string original = Directory.CreateDirectory(Path.Combine(temporary.Path, "Original")).FullName;
        long documentId;
        using (var library = InboxLibrary.Open(original))
        {
            var document = await library.ImportAsync(Fixture(Letter), TestContext.Current.CancellationToken);
            documentId = document.Id;
            library.AddTag(documentId, "  Réntal  ");
            library.AddTag(documentId, "RÉNTAL");
            library.SetReviewed(documentId, true);
        }

        string moved = Path.Combine(temporary.Path, "Moved");
        Directory.Move(original, moved);
        using var reopened = InboxLibrary.Open(moved);
        Assert.Empty(reopened.Find(new DocumentFilter(InboxOnly: true), TestContext.Current.CancellationToken).Items);
        var matching = Assert.Single(reopened.Find(new DocumentFilter(Query: "norths*", Tag: "réntal"), TestContext.Current.CancellationToken).Items);
        Assert.Equal(documentId, matching.Id);
        Assert.Equal("Réntal", Assert.Single(matching.Tags));
        Assert.True(File.Exists(reopened.ResolvePdf(matching)));
    }

    [Fact]
    public void RefreshRecoversAnUnindexedFile()
    {
        using var temporary = new TemporaryLibrary();
        using var library = InboxLibrary.Open(temporary.Path);
        string nested = Directory.CreateDirectory(Path.Combine(library.PdfDirectory, "Property")).FullName;
        File.Copy(Fixture(Letter), Path.Combine(nested, "A copy.pdf"));

        var refreshed = library.Refresh(TestContext.Current.CancellationToken);

        Assert.Equal(1, refreshed.Indexed);
        Assert.Empty(refreshed.Warnings);
        Assert.Single(library.Find(new DocumentFilter(Query: LetterKeyword), TestContext.Current.CancellationToken).Items);
        Assert.Equal(0, library.Refresh(TestContext.Current.CancellationToken).Indexed);
    }

    [Fact]
    public async Task MultipageScanUsesTheSameSearchableOutputAsImport()
    {
        using var temporary = new TemporaryLibrary();
        using var library = InboxLibrary.Open(temporary.Path);
        var document = await library.StoreScanAsync("Feeder job", ReadCapturedPages, TestContext.Current.CancellationToken);
        using var pdf = PdfDocument.Open(library.ResolvePdf(document));

        Assert.Equal(2, document.PageCount);
        Assert.All(pdf.GetPages(), page =>
            Assert.Contains(ReceiptKeyword, ContentOrderTextExtractor.GetText(page), StringComparison.OrdinalIgnoreCase));
        Assert.Equal(document.Id, Assert.Single(library.Find(new DocumentFilter(Query: ReceiptKeyword), TestContext.Current.CancellationToken).Items).Id);
    }

    private static async IAsyncEnumerable<ProcessedImage> ReadCapturedPages(
        ScanningContext context, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var capturedFiles = new[] { Fixture(Receipt), Fixture(Receipt) };
        var importer = new ImageImporter(context);
        foreach (var capturedFile in capturedFiles)
        {
            await foreach (var image in importer.Import(capturedFile, progress: cancellationToken))
            {
                yield return image;
            }
        }
    }

    [Fact]
    public async Task ExactRepeatWithDifferentNameKeepsExistingPdfAndReviewState()
    {
        using var temporary = new TemporaryLibrary();
        string renamedCopy = Path.Combine(temporary.Path, "Renamed receipt.png");
        File.Copy(Fixture(Receipt), renamedCopy);
        long originalId;
        using (var library = InboxLibrary.Open(temporary.Path))
        {
            var first = await library.ImportFileAsync(Fixture(Receipt), TestContext.Current.CancellationToken);
            Assert.True(first.Added);
            originalId = first.Document.Id;
            library.AddTag(originalId, "Workshop");
            library.SetReviewed(originalId, true);
        }

        using var reopened = InboxLibrary.Open(temporary.Path);
        var duplicate = await reopened.ImportFileAsync(renamedCopy, TestContext.Current.CancellationToken);

        Assert.False(duplicate.Added);
        Assert.Equal(originalId, duplicate.Document.Id);
        Assert.True(duplicate.Document.Reviewed);
        Assert.Equal("Workshop", Assert.Single(duplicate.Document.Tags));
        Assert.Single(Directory.EnumerateFiles(reopened.PdfDirectory));
        Assert.True(File.Exists(renamedCopy));
    }

    [Fact]
    public async Task UpgradeFromV1FindsIdenticalDigitalPdfAndRetainsTags()
    {
        using var temporary = new TemporaryLibrary();
        long originalId;
        using (var library = InboxLibrary.Open(temporary.Path))
        {
            var original = await library.ImportAsync(Fixture(Letter), TestContext.Current.CancellationToken);
            originalId = original.Id;
            library.AddTag(originalId, "Housing");
            library.SetReviewed(originalId, true);
        }

        var builder = new SqliteConnectionStringBuilder
        {
            DataSource = Path.Combine(temporary.Path, ".plico", "catalog.sqlite3"),
            Pooling = false
        };
        using (var connection = new SqliteConnection(builder.ToString()))
        {
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = """
                DROP TABLE pending_pdf_move;
                DROP INDEX documents_added_at;
                DROP INDEX documents_ocr_copies;
                DROP INDEX documents_source_sha256;
                DROP INDEX documents_legacy_file_bytes;
                ALTER TABLE documents DROP COLUMN ocr_copy_language;
                ALTER TABLE documents DROP COLUMN ocr_copy_source_id;
                ALTER TABLE documents DROP COLUMN source_sha256;
                ALTER TABLE documents DROP COLUMN display_title;
                ALTER TABLE documents DROP COLUMN pdf_sha256;
                PRAGMA user_version = 1;
                """;
            command.ExecuteNonQuery();
        }

        using var upgraded = InboxLibrary.Open(temporary.Path);
        var duplicate = await upgraded.ImportFileAsync(Fixture(Letter), TestContext.Current.CancellationToken);
        var searchResult = Assert.Single(upgraded.Find(new DocumentFilter(Query: LetterKeyword, Tag: "housing"), TestContext.Current.CancellationToken).Items);

        Assert.False(duplicate.Added);
        Assert.Equal(originalId, searchResult.Id);
        Assert.True(searchResult.Reviewed);
        Assert.Single(Directory.EnumerateFiles(upgraded.PdfDirectory));
    }

    [Fact]
    public async Task DifferentFileCannotInheritMetadataFromMissingPdf()
    {
        using var temporary = new TemporaryLibrary();
        using var library = InboxLibrary.Open(temporary.Path);
        var original = await library.ImportAsync(Fixture(Letter), TestContext.Current.CancellationToken);
        library.AddTag(original.Id, "Housing");
        library.SetReviewed(original.Id, true);
        File.Delete(library.ResolvePdf(original));
        library.Refresh(TestContext.Current.CancellationToken);
        string unrelated = Path.Combine(temporary.Path, Letter);
        File.Copy(Fixture(Mixed), unrelated);

        var imported = await library.ImportFileAsync(unrelated, TestContext.Current.CancellationToken);

        Assert.True(imported.Added);
        Assert.NotEqual(original.Id, imported.Document.Id);
        Assert.False(imported.Document.Reviewed);
        Assert.Empty(imported.Document.Tags);
    }

    [Fact]
    public async Task EditedTitleRemainsSearchableByBothNamesAfterARefresh()
    {
        var token = TestContext.Current.CancellationToken;
        using var temporary = new TemporaryLibrary();
        string source = Path.Combine(temporary.Path, "Binder88.pdf");
        File.Copy(Fixture(Letter), source);
        long id;
        using (var library = InboxLibrary.Open(temporary.Path))
        {
            var document = await library.ImportAsync(source, token);
            id = document.Id;
            library.AddTag(id, "Rent");
            library.SetReviewed(id, true);
            library.SetTitle(id, "  October housing  ");
            var named = Assert.Single(library.Find(new DocumentFilter(Query: "October"), TestContext.Current.CancellationToken).Items);
            Assert.Equal("October housing", named.Title);
            Assert.Equal(document.RelativePath, named.RelativePath);
            Assert.Equal(File.ReadAllBytes(source), File.ReadAllBytes(library.ResolvePdf(named)));

            File.Copy(Fixture(Mixed), library.ResolvePdf(named), overwrite: true);
            Assert.Equal(1, library.Refresh(token).Indexed);
        }

        using var reopened = InboxLibrary.Open(temporary.Path);
        var found = Assert.Single(reopened.Find(new DocumentFilter(Query: "Binder88", Tag: "rent"), TestContext.Current.CancellationToken).Items);
        Assert.Equal("October housing", found.Title);
        Assert.Equal(id, found.Id);
        Assert.True(found.Reviewed);
        Assert.Equal(2, found.PageCount);
        Assert.Single(reopened.Find(new DocumentFilter(Query: "October NORTHSTAR"), TestContext.Current.CancellationToken).Items);
    }

    [Fact]
    public async Task InvalidTitleLeavesTheDocumentSearchable()
    {
        using var temporary = new TemporaryLibrary();
        using var library = InboxLibrary.Open(temporary.Path);
        var document = await library.ImportAsync(Fixture(Letter), TestContext.Current.CancellationToken);

        Assert.Throws<ArgumentException>(() => library.SetTitle(document.Id, "    "));
        Assert.Throws<ArgumentException>(() => library.SetTitle(document.Id, "Broken\nTitle"));
        Assert.Equal(document.Title, Assert.Single(library.Find(new DocumentFilter(Query: LetterKeyword), TestContext.Current.CancellationToken).Items).Title);
    }

    [Fact]
    public async Task QuotedSearchRequiresAdjacentWholeWords()
    {
        using var temporary = new TemporaryLibrary();
        using var library = InboxLibrary.Open(temporary.Path);
        var document = await library.ImportAsync(Fixture(Letter), TestContext.Current.CancellationToken);

        Assert.Equal(document.Id, Assert.Single(library.Find(new DocumentFilter(Query: "\"new term\" norths"), TestContext.Current.CancellationToken).Items).Id);
        Assert.Single(library.Find(new DocumentFilter(Query: "northstar lease"), TestContext.Current.CancellationToken).Items);
        Assert.Empty(library.Find(new DocumentFilter(Query: "\"northstar lease\""), TestContext.Current.CancellationToken).Items);
        Assert.Empty(library.Find(new DocumentFilter(Query: "\"new ter\""), TestContext.Current.CancellationToken).Items);
        Assert.Single(library.Find(new DocumentFilter(Query: "\"new ter"), TestContext.Current.CancellationToken).Items);
        Assert.Single(library.Find(new DocumentFilter(Query: "“new term”"), TestContext.Current.CancellationToken).Items);
    }

    private static string Fixture(string name) => Path.Combine(AppContext.BaseDirectory, "fixtures", name);
}

internal sealed class TemporaryLibrary : IDisposable
{
    private readonly DirectoryInfo _directory = Directory.CreateTempSubdirectory("plico-test-");
    public string Path => _directory.FullName;
    public void Dispose() => _directory.Delete(recursive: true);
}
