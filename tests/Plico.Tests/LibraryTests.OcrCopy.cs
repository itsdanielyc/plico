using Plico.Core;
using UglyToad.PdfPig;
using UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor;
using UglyToad.PdfPig.Writer;
using Xunit;

namespace Plico.Tests;

public sealed partial class LibraryTests
{
    [Fact]
    public async Task SearchableCopyReplacesOldOcrAndReusesOnlyTheRequestedLanguage()
    {
        const string spanishWord = "pequeño";
        var token = TestContext.Current.CancellationToken;
        using var temporary = new TemporaryLibrary();
        long originalId, spanishId, englishId;
        using (var library = InboxLibrary.Open(temporary.Path))
        {
            var original = await library.ImportAsync(Fixture("Spanish Receipt.png"), token, OcrLanguage.English);
            originalId = original.Id;
            Assert.True(original.HasText);
            Assert.Contains("Ferreteria", library.ReadIndexedText(originalId), StringComparison.OrdinalIgnoreCase);
            string source = library.ResolvePdf(original);
            byte[] sourceBytes = File.ReadAllBytes(source);
            using var originalPdf = PdfDocument.Open(sourceBytes);
            var originalPage = originalPdf.GetPage(1);
            library.AddTag(originalId, "Workshop");
            library.SetReviewed(originalId, reviewed: true);

            var corrected = await library.CreateSearchableCopyAsync(originalId, token, OcrLanguage.Spanish);

            spanishId = corrected.Document.Id;
            Assert.True(corrected.Added);
            Assert.NotEqual(originalId, spanishId);
            Assert.Equal("Spanish Receipt (Spanish OCR)", corrected.Document.Title);
            Assert.Equal("Workshop", Assert.Single(corrected.Document.Tags));
            Assert.False(corrected.Document.Reviewed);
            using (var pdf = PdfDocument.Open(library.ResolvePdf(corrected.Document)))
            {
                Assert.Equal(original.PageCount, pdf.NumberOfPages);
                var page = pdf.GetPage(1);
                Assert.InRange(Math.Abs((double)(originalPage.Width - page.Width)), 0, 1);
                Assert.InRange(Math.Abs((double)(originalPage.Height - page.Height)), 0, 1);
                string text = ContentOrderTextExtractor.GetText(page);
                Assert.Contains(spanishWord, text, StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain("Ferreteria", text, StringComparison.OrdinalIgnoreCase);
            }
            Assert.Contains(library.Find(new DocumentFilter(Query: spanishWord, Tag: "Workshop"), token).Items,
                item => item.Id == spanishId && item.Match.Contains(spanishWord, StringComparison.OrdinalIgnoreCase));
            Assert.Equal(sourceBytes, File.ReadAllBytes(source));
            Assert.True(library.Find(new DocumentFilter(Tag: "Workshop"), token).Items.Single(item => item.Id == originalId).Reviewed);

            var english = await library.CreateSearchableCopyAsync(originalId, token, OcrLanguage.English);
            englishId = english.Document.Id;
            Assert.True(english.Added);
            Assert.NotEqual(spanishId, englishId);
        }

        using var reopened = InboxLibrary.Open(temporary.Path);
        var repeatedSpanish = await reopened.CreateSearchableCopyAsync(originalId, token, OcrLanguage.Spanish);
        var repeatedEnglish = await reopened.CreateSearchableCopyAsync(originalId, token, OcrLanguage.English);
        Assert.False(repeatedSpanish.Added);
        Assert.False(repeatedEnglish.Added);
        Assert.Equal(spanishId, repeatedSpanish.Document.Id);
        Assert.Equal(englishId, repeatedEnglish.Document.Id);
    }

    [Fact]
    public async Task SearchableCopiesOfIdenticalFilesUseTheirOwnCatalogTags()
    {
        using var temporary = new TemporaryLibrary();
        using var library = InboxLibrary.Open(temporary.Path);
        var token = TestContext.Current.CancellationToken;
        string fixture = Fixture("Scanned Receipt.pdf");
        File.Copy(fixture, Path.Combine(library.PdfDirectory, "April scan.pdf"));
        File.Copy(fixture, Path.Combine(library.PdfDirectory, "June scan.pdf"));
        Assert.Equal(2, library.Refresh(token).Indexed);
        var documents = library.Find(new DocumentFilter(), token).Items;
        var april = documents.Single(document => document.Title == "April scan");
        var june = documents.Single(document => document.Title == "June scan");
        library.AddTag(april.Id, "April");
        library.AddTag(june.Id, "June");

        var first = await library.CreateSearchableCopyAsync(april.Id, token);
        var second = await library.CreateSearchableCopyAsync(june.Id, token);

        Assert.True(first.Added);
        Assert.True(second.Added);
        Assert.NotEqual(first.Document.Id, second.Document.Id);
        Assert.Equal("April", Assert.Single(first.Document.Tags));
        Assert.Equal("June", Assert.Single(second.Document.Tags));

        var ordinaryImport = await library.ImportFileAsync(fixture, token);
        Assert.False(ordinaryImport.Added);
        Assert.Contains(ordinaryImport.Document.Id, new[] { april.Id, june.Id });
    }

    [Fact]
    public async Task SearchableCopyPreservesTheIndexedScanAndReusesTheFirstCopy()
    {
        using var temporary = new TemporaryLibrary();
        using var library = InboxLibrary.Open(temporary.Path);
        var token = TestContext.Current.CancellationToken;
        string folder = Directory.CreateDirectory(Path.Combine(library.PdfDirectory, "Filed")).FullName;
        string path = Path.Combine(folder, "Paper receipt.pdf");
        File.Copy(Fixture("Scanned Receipt.pdf"), path);
        byte[] originalBytes = File.ReadAllBytes(path);
        Assert.Equal(1, library.Refresh(token).Indexed);
        var original = Assert.Single(library.Find(new DocumentFilter(), token).Items);
        Assert.False(original.HasText);
        var legacyRepeat = await library.ImportFileAsync(path, token);
        Assert.False(legacyRepeat.Added);
        Assert.Equal(original.Id, legacyRepeat.Document.Id);
        library.AddTag(original.Id, "Workshop");
        library.SetTitle(original.Id, "October receipt");
        library.SetReviewed(original.Id, reviewed: true);
        var sourceDate = new DateOnly(2024, 2, 29);
        library.SetDocumentDate(original.Id, sourceDate);

        var copy = await library.CreateSearchableCopyAsync(original.Id, token);

        Assert.True(copy.Added);
        Assert.NotEqual(original.Id, copy.Document.Id);
        Assert.Equal("October receipt (searchable)", copy.Document.Title);
        Assert.True(copy.Document.HasText);
        Assert.False(copy.Document.Reviewed);
        Assert.Equal("Workshop", Assert.Single(copy.Document.Tags));
        Assert.Equal(sourceDate, copy.Document.DocumentDate);
        using (var pdf = PdfDocument.Open(library.ResolvePdf(copy.Document)))
        {
            Assert.Equal(original.PageCount, pdf.NumberOfPages);
            Assert.Contains(ReceiptKeyword, ContentOrderTextExtractor.GetText(pdf.GetPage(1)), StringComparison.OrdinalIgnoreCase);
        }
        var found = Assert.Single(library.Find(new DocumentFilter(Query: $"{ReceiptKeyword} dated:2024-02-29", Tag: "Workshop"), token).Items);
        Assert.Equal(copy.Document.Id, found.Id);

        library.SetDocumentDate(copy.Document.Id, null);
        library.SetDocumentDate(original.Id, sourceDate.AddDays(1));
        var repeat = await library.CreateSearchableCopyAsync(original.Id, token);

        Assert.False(repeat.Added);
        Assert.Equal(copy.Document.Id, repeat.Document.Id);
        Assert.Null(repeat.Document.DocumentDate);
        Assert.Equal(2, Directory.EnumerateFiles(library.PdfDirectory, "*.pdf", SearchOption.AllDirectories).Count());
        Assert.Equal(originalBytes, File.ReadAllBytes(path));
        Assert.True(library.Find(new DocumentFilter(Tag: "Workshop"), token).Items.Single(item => item.Id == original.Id).Reviewed);
    }

    [Fact]
    public async Task SearchableCopyPublishesNothingForABlankChangedOrCancelledScan()
    {
        using var temporary = new TemporaryLibrary();
        using var library = InboxLibrary.Open(temporary.Path);
        var token = TestContext.Current.CancellationToken;
        using var blank = new PdfDocumentBuilder();
        blank.AddPage(UglyToad.PdfPig.Content.PageSize.A4);
        string path = Path.Combine(library.PdfDirectory, "Blank scan.pdf");
        byte[] contents = blank.Build();
        File.WriteAllBytes(path, contents);
        Assert.Equal(1, library.Refresh(token).Indexed);
        var scan = Assert.Single(library.Find(new DocumentFilter(), token).Items);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => library.CreateSearchableCopyAsync(scan.Id, new CancellationToken(true)));
        var empty = await Assert.ThrowsAsync<InvalidDataException>(() => library.CreateSearchableCopyAsync(scan.Id, token));
        Assert.Contains("no searchable text", empty.Message, StringComparison.OrdinalIgnoreCase);
        File.SetLastWriteTimeUtc(path, File.GetLastWriteTimeUtc(path).AddMinutes(5));
        var changed = await Assert.ThrowsAsync<IOException>(() => library.CreateSearchableCopyAsync(scan.Id, token));
        Assert.Contains("refresh", changed.Message, StringComparison.OrdinalIgnoreCase);

        Assert.Equal(path, Assert.Single(Directory.EnumerateFiles(library.PdfDirectory)));
        Assert.Equal(contents, File.ReadAllBytes(path));
        Assert.Equal(scan.Id, Assert.Single(library.Find(new DocumentFilter(), token).Items).Id);
        Assert.Empty(Directory.EnumerateDirectories(Path.Combine(temporary.Path, ".plico"), "import-*"));
    }
}
