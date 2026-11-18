using System.Runtime.CompilerServices;
using NAPS2.Images;
using NAPS2.ImportExport;
using NAPS2.Pdf;
using NAPS2.Scan;
using Plico.Core;
using Xunit;

namespace Plico.Tests;

public sealed partial class LibraryTests
{
    private const string LeaseFixture = "Lease Renewal.pdf";
    private const string LedgerFixture = "Match Ledger.pdf";

    [Fact]
    public async Task ScanBatchDropsLeadingTrailingAndConsecutiveSeparators()
    {
        using var temporary = new TemporaryLibrary();
        using var library = InboxLibrary.Open(temporary.Path);
        var token = TestContext.Current.CancellationToken;
        var pages = new CapturedPage[]
        {
            new(LeaseFixture, "PATCHT"), new(LeaseFixture), new(LeaseFixture, "NEXT"),
            new(LeaseFixture, "PATCHT"), new(LedgerFixture), new(LeaseFixture, "NEXT")
        };
        var result = await library.StoreScanBatchAsync("Split job",
            (context, cancellation) => CapturedFixturePages(context, pages, cancellation), (_, _) => Task.FromResult(false),
            null, new ScanBatchOptions(true, "NEXT"), false, token);

        Assert.Collection(result.Documents, document => Assert.Equal("Split job - 1", document.Title),
            document => Assert.Equal("Split job - 2", document.Title));
        Assert.All(result.Documents, document => Assert.Equal(1, document.PageCount));
        Assert.Contains("NORTHSTAR", DocumentProcessor.ReadPageText(library.ResolvePdf(result.Documents[0]), 1, token));
        Assert.Contains("LEDGER", DocumentProcessor.ReadPageText(library.ResolvePdf(result.Documents[1]), 1, token),
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ManualDuplexResumesAfterClosingAndKeepsFrontsWhenBackCountDiffers()
    {
        using var temporary = new TemporaryLibrary();
        var token = TestContext.Current.CancellationToken;
        using (var firstSession = InboxLibrary.Open(temporary.Path))
        {
            var saved = await firstSession.StoreScanBatchAsync("Manual duplex fixture",
                (context, cancellation) => CapturedFixturePages(context, [new(LeaseFixture), new(LedgerFixture)], cancellation),
                (_, _) => Task.FromResult(false), (_, _) => Task.FromResult(ManualDuplexDecision.SaveForLater), null, false, token);
            Assert.True(saved.HasPendingDuplex);
            Assert.Empty(firstSession.Find(new DocumentFilter(), token).Items);
        }

        using var reopened = InboxLibrary.Open(temporary.Path);
        await Assert.ThrowsAsync<InvalidDataException>(() => reopened.StoreScanBatchAsync("New session",
            (context, cancellation) => CapturedFixturePages(context, [new(LedgerFixture)], cancellation),
            (_, _) => Task.FromResult(false), (_, _) => Task.FromResult(ManualDuplexDecision.ScanBacks), null, false, token));
        Assert.Empty(reopened.Find(new DocumentFilter(), token).Items);

        var resumed = await reopened.StoreScanBatchAsync("New session",
            (context, cancellation) => CapturedFixturePages(context, [new(LedgerFixture), new(LeaseFixture)], cancellation),
            (_, _) => Task.FromResult(false), (prompt, _) =>
            {
                Assert.Equal(2, prompt.FrontPageCount);
                return Task.FromResult(ManualDuplexDecision.ScanBacks);
            }, null, false, token);
        var document = Assert.Single(resumed.Documents);
        Assert.Equal("Manual duplex fixture", document.Title);
        Assert.Equal(4, document.PageCount);
        string pdf = reopened.ResolvePdf(document);
        Assert.Contains("NORTHSTAR", DocumentProcessor.ReadPageText(pdf, 1, token));
        Assert.Contains("NORTHSTAR", DocumentProcessor.ReadPageText(pdf, 2, token));
        Assert.Contains("LEDGER", DocumentProcessor.ReadPageText(pdf, 3, token), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("LEDGER", DocumentProcessor.ReadPageText(pdf, 4, token), StringComparison.OrdinalIgnoreCase);
        Assert.False(Directory.Exists(Path.Combine(temporary.Path, ".plico", "pending-manual-duplex")));
    }

    private sealed record CapturedPage(string File, string? Barcode = null);

    [Fact]
    public async Task ASerialLabelKeepsItsFirstPageAndAConflictKeepsTheCapture()
    {
        using var temporary = new TemporaryLibrary();
        using var library = InboxLibrary.Open(temporary.Path);
        var token = TestContext.Current.CancellationToken;
        var reserved = await library.ImportAsync(Fixture(LeaseFixture), token);
        library.SetArchiveSerialNumber(reserved.Id, 890);
        var pages = new CapturedPage[] { new(LedgerFixture, "ASN00890"), new(LeaseFixture, "ASN891"), new(LedgerFixture) };
        var batch = await library.StoreScanBatchAsync("Archive fixture",
            (context, cancellation) => CapturedFixturePages(context, pages, cancellation), (_, _) => Task.FromResult(false),
            null, new ScanBatchOptions(false, ReadArchiveSerialNumbers: true), false, token);

        Assert.Equal(2, batch.Documents.Count);
        Assert.Equal(new ArchiveSerialConflict(1, 890), Assert.Single(batch.SerialConflicts));
        Assert.Null(library.ReadArchiveSerialNumber(batch.Documents[0].Id));
        Assert.Equal(1, batch.Documents[0].PageCount);
        Assert.Equal(2, batch.Documents[1].PageCount);
        Assert.Equal(reserved.Id, library.FindByArchiveSerialNumber(890)?.Id);
        Assert.Equal(batch.Documents[1].Id, library.FindByArchiveSerialNumber(891)?.Id);
    }

    [Fact]
    public async Task PrintedSeparatorAndArchiveLabelsDecodeUsingTheScannerBarcodeOptions()
    {
        using var temporary = new TemporaryLibrary();
        var token = TestContext.Current.CancellationToken;
        string separator = Path.Combine(temporary.Path, "separator.pdf");
        string labels = Path.Combine(temporary.Path, "labels.pdf");
        ArchiveLabels.WriteSeparatorSheet(separator, ScanPaper.A4, token);
        ArchiveLabels.WriteSerialSheet(labels, ScanPaper.Letter, 470, quantity: 1, token);

        using var context = DocumentProcessor.CreateCaptureContext(temporary.Path);
        var options = new ImportParams { BarcodeDetectionOptions = ScanBatchPages.BarcodeOptions(new(true, ReadArchiveSerialNumbers: true)) };
        var importer = new PdfImporter(context);
        var printed = new List<ProcessedImage>();
        await DocumentProcessor.CollectPagesAsync(importer.Import(separator, options, token), printed, token);
        await DocumentProcessor.CollectPagesAsync(importer.Import(labels, options, token), printed, token);
        Assert.Equal(2, printed.Count);
        Assert.True(printed[0].PostProcessingData.Barcode.IsPatchT);
        Assert.Equal(470, ArchiveLabels.ReadBarcode(printed[1].PostProcessingData.Barcode.DetectedText));
    }

    private static async IAsyncEnumerable<ProcessedImage> CapturedFixturePages(ScanningContext context,
        IReadOnlyList<CapturedPage> pages, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        foreach (var fixture in pages)
        {
            await foreach (var page in new PdfImporter(context).Import(Fixture(fixture.File), progress: cancellationToken))
            {
                yield return fixture.Barcode is { } barcode
                    ? page.WithPostProcessingData(page.PostProcessingData with { Barcode = new Barcode(true, true, barcode) }, disposeSelf: true)
                    : page;
                break;
            }
        }
    }
}
