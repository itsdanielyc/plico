using Plico.Core;
using UglyToad.PdfPig;
using UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor;
using Xunit;

namespace Plico.Tests;

public sealed partial class LibraryTests
{
    [Fact]
    public async Task CombinedPdfPreservesRequestedOrderSearchTextAndSourceTags()
    {
        var token = TestContext.Current.CancellationToken;
        using var temporary = new TemporaryLibrary();
        using var library = InboxLibrary.Open(temporary.Path);
        var mixed = await library.ImportAsync(Fixture(Mixed), token);
        var spanish = await library.ImportAsync(Fixture("Spanish Receipt.png"), token, OcrLanguage.Spanish);
        library.AddTag(mixed.Id, "Housing");
        library.AddTag(mixed.Id, "Workshop");
        library.AddTag(spanish.Id, "Repairs");
        library.AddTag(spanish.Id, "Workshop");
        library.SetReviewed(mixed.Id, reviewed: true);
        var sourceDate = new DateOnly(2024, 2, 29);
        library.SetDocumentDate(mixed.Id, sourceDate);
        library.SetDocumentDate(spanish.Id, sourceDate);
        string mixedPath = library.ResolvePdf(mixed);
        string spanishPath = library.ResolvePdf(spanish);
        byte[] mixedSource = File.ReadAllBytes(mixedPath);
        byte[] spanishSource = File.ReadAllBytes(spanishPath);

        var combined = await library.CombinePdfsAsync([spanish.Id, mixed.Id, spanish.Id], "  Records packet  ", cancellationToken: token);

        Assert.Equal("Records packet", combined.Title);
        Assert.False(combined.Reviewed);
        Assert.Equal(new[] { "Housing", "Repairs", "Workshop" }, combined.Tags);
        Assert.Equal(sourceDate, combined.DocumentDate);
        Assert.True(combined.HasText);
        Assert.Equal(3, combined.PageCount);
        using (var pdf = PdfDocument.Open(library.ResolvePdf(combined)))
        {
            Assert.Equal(3, pdf.NumberOfPages);
            Assert.Contains("pequeño", ContentOrderTextExtractor.GetText(pdf.GetPage(1)), StringComparison.OrdinalIgnoreCase);
            Assert.Contains(LetterKeyword, ContentOrderTextExtractor.GetText(pdf.GetPage(2)), StringComparison.OrdinalIgnoreCase);
            Assert.Contains(ReceiptKeyword, ContentOrderTextExtractor.GetText(pdf.GetPage(3)), StringComparison.OrdinalIgnoreCase);
        }
        Assert.Equal(3, library.FindFirstMatchingPage(combined, ReceiptKeyword, token));
        var results = library.Find(new DocumentFilter(Query: "pequeño norths tag:Housing dated:2024-02"), token).Items;
        Assert.Equal(combined.Id, Assert.Single(results).Id);
        Assert.Equal(mixedSource, File.ReadAllBytes(mixedPath));
        Assert.Equal(spanishSource, File.ReadAllBytes(spanishPath));
        Assert.True(library.Find(new DocumentFilter(Tag: "Housing"), token).Items.Single(document => document.Id == mixed.Id).Reviewed);
    }

    [Fact]
    public async Task CombinedPdfUsesTheSharedDateOnlyWhenEverySourceMatches()
    {
        var token = TestContext.Current.CancellationToken;
        using var temporary = new TemporaryLibrary();
        using var library = InboxLibrary.Open(temporary.Path);
        BatchFixtures.AddTwoDocuments(library, token);
        var sources = library.Find(new DocumentFilter(), token).Items.Select(document => document.Id).ToArray();
        var leapDay = new DateOnly(2024, 2, 29);
        library.SetDocumentDate(sources[0], leapDay);

        var withUndatedSource = await library.CombinePdfsAsync(sources, "Packet with an undated PDF", cancellationToken: token);
        Assert.Null(withUndatedSource.DocumentDate);

        library.SetDocumentDate(sources[1], leapDay.AddDays(1));
        var withDifferentDates = await library.CombinePdfsAsync(sources, "Packet from different days", cancellationToken: token);
        Assert.Null(withDifferentDates.DocumentDate);
    }

    [Fact]
    public async Task CancelledOrChangedSourcesPublishNoCombinedPdf()
    {
        var token = TestContext.Current.CancellationToken;
        using var temporary = new TemporaryLibrary();
        using var library = InboxLibrary.Open(temporary.Path);
        BatchFixtures.AddTwoDocuments(library, token);
        var documents = library.Find(new DocumentFilter(), token).Items;
        var ids = documents.Select(document => document.Id).ToArray();
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(token);
        var cancelAfterFirst = new InlinePdfProgress(count =>
        {
            if (count == 1) cancellation.Cancel();
        });

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            library.CombinePdfsAsync(ids, "Cancelled packet", cancelAfterFirst, cancellation.Token));
        await Assert.ThrowsAsync<ArgumentException>(() =>
            library.CombinePdfsAsync([ids[0], ids[0]], "Repeated source", cancellationToken: token));
        string path = library.ResolvePdf(documents[0]);
        DateTime stamp = File.GetLastWriteTimeUtc(path);
        var editAfterCopy = new InlinePdfProgress(count =>
        {
            if (count == ids.Length) File.SetLastWriteTimeUtc(path, stamp.AddMinutes(5));
        });
        await Assert.ThrowsAsync<IOException>(() => library.CombinePdfsAsync(ids, "Changed packet", editAfterCopy, token));

        Assert.Equal(ids.Length, Directory.EnumerateFiles(library.PdfDirectory, "*.pdf").Count());
        Assert.Equal(ids.Length, library.Find(new DocumentFilter(), token).Items.Count);
        Assert.Empty(Directory.EnumerateDirectories(Path.Combine(temporary.Path, ".plico"), "import-*"));
    }
}
