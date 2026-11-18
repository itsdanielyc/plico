using System.Runtime.CompilerServices;
using NAPS2.Images;
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
