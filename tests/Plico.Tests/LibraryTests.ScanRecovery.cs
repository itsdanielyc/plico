using System.Runtime.CompilerServices;
using NAPS2.Images;
using NAPS2.Scan;
using NAPS2.Scan.Exceptions;
using Plico.Core;
using Xunit;

namespace Plico.Tests;

public sealed partial class LibraryTests
{
    [Fact]
    public async Task FeederErrorBeforeTheFirstPageDoesNotOfferRecovery()
    {
        using var temporary = new TemporaryLibrary();
        using var library = InboxLibrary.Open(temporary.Path);
        var token = TestContext.Current.CancellationToken;

        await Assert.ThrowsAsync<DevicePaperJamException>(() => library.StoreScanWithRecoveryAsync("Empty feeder job",
            (context, cancellation) => CaptureUntilJamAsync(context, capturePages: false, cancellation),
            (_, _) => throw new InvalidOperationException("A scan with zero pages must skip confirmation."), token));

        AssertNoScanPublished(library, temporary.Path, token);
    }

    [Fact]
    public async Task CancellingAtScanRecoveryDiscardsStagedPagesEvenWhenSaveWasChosen()
    {
        using var temporary = new TemporaryLibrary();
        using var library = InboxLibrary.Open(temporary.Path);
        var token = TestContext.Current.CancellationToken;
        using var scanCancellation = CancellationTokenSource.CreateLinkedTokenSource(token);
        bool prompted = false;

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => library.StoreScanWithRecoveryAsync("Interrupted feeder job",
            (context, cancellation) => CaptureUntilJamAsync(context, capturePages: true, cancellation),
            (interruption, cancellation) =>
            {
                prompted = true;
                Assert.Equal(2, interruption.CapturedPages);
                Assert.Equal(scanCancellation.Token, cancellation);
                scanCancellation.Cancel();
                return Task.FromResult(true);
            }, scanCancellation.Token));

        Assert.True(prompted);
        AssertNoScanPublished(library, temporary.Path, token);
    }

    private static async IAsyncEnumerable<ProcessedImage> CaptureUntilJamAsync(ScanningContext context, bool capturePages,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        if (capturePages)
        {
            await foreach (var image in ReadCapturedPages(context, cancellationToken)) yield return image;
        }
        throw new DevicePaperJamException();
    }

    private static void AssertNoScanPublished(InboxLibrary library, string libraryPath, CancellationToken cancellationToken)
    {
        Assert.Empty(Directory.EnumerateFiles(library.PdfDirectory, "*.pdf"));
        Assert.Empty(library.Find(new DocumentFilter(), cancellationToken).Items);
        Assert.Empty(Directory.EnumerateDirectories(Path.Combine(libraryPath, ".plico"), "import-*"));
    }
}
