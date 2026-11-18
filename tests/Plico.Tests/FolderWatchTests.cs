using Plico.Core;
using Xunit;

namespace Plico.Tests;

public sealed class FolderWatchTests
{
    [Fact]
    public async Task ExistingFileRequestQueuesOnlySupportedTopLevelFiles()
    {
        using var incoming = new TemporaryLibrary();
        using var destination = new TemporaryLibrary();
        string fixture = Path.Combine(AppContext.BaseDirectory, "fixtures", "Lease Renewal.pdf");
        string savedScan = Path.Combine(incoming.Path, "Queued lease.pdf");
        File.Copy(fixture, savedScan);
        File.Copy(fixture, Path.Combine(incoming.Path, "Writing scan.part"));
        string nested = Directory.CreateDirectory(Path.Combine(incoming.Path, "Other files")).FullName;
        File.Copy(fixture, Path.Combine(nested, "Nested lease.pdf"));

        var received = new TaskCompletionSource<IReadOnlyList<string>>(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var watch = FolderWatch.Start(incoming.Path, destination.Path, (paths, _) =>
        {
            received.TrySetResult(paths);
            return Task.CompletedTask;
        });

        Assert.Throws<OperationCanceledException>(() => watch.QueueExistingFiles(new CancellationToken(canceled: true)));
        Assert.Equal(1, watch.QueueExistingFiles(TestContext.Current.CancellationToken));
        var batch = await received.Task.WaitAsync(TimeSpan.FromSeconds(15), TestContext.Current.CancellationToken);

        Assert.Equal(savedScan, Assert.Single(batch));
        Assert.True(File.Exists(savedScan));
    }

    [Fact]
    public async Task RenamedScanImportsAfterArrivalAndKeepsBothSources()
    {
        using var incoming = new TemporaryLibrary();
        using var destination = new TemporaryLibrary();
        using var library = InboxLibrary.Open(destination.Path);
        var fixture = Path.Combine(AppContext.BaseDirectory, "fixtures", "Lease Renewal.pdf");
        var oldFile = Path.Combine(incoming.Path, "Existing letter.pdf");
        File.Copy(fixture, oldFile);

        var importedPaths = new TaskCompletionSource<IReadOnlyList<string>>(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var watch = FolderWatch.Start(incoming.Path, library.PdfDirectory, async (paths, token) =>
        {
            foreach (var file in paths) await library.ImportFileAsync(file, token);
            importedPaths.TrySetResult(paths);
        });
        var partial = Path.Combine(incoming.Path, "scan.part");
        var finished = Path.Combine(incoming.Path, "Scan desk.pdf");
        File.Copy(fixture, partial);
        File.Move(partial, finished);

        var paths = await importedPaths.Task.WaitAsync(TimeSpan.FromSeconds(15), TestContext.Current.CancellationToken);
        Assert.Equal("Scan desk.pdf", Path.GetFileName(Assert.Single(paths)));
        Assert.Equal("Scan desk", Assert.Single(library.Find(new DocumentFilter(Query: "NORTHSTAR")).Items).Title);
        Assert.True(File.Exists(finished));
        Assert.True(File.Exists(oldFile));

        await watch.DisposeAsync();
        Assert.True(watch.Completion.IsCompletedSuccessfully);
    }

    [Fact]
    public async Task RejectsTheDestinationPdfFolderAsIntake()
    {
        using var destination = new TemporaryLibrary();
        using var library = InboxLibrary.Open(destination.Path);
        FolderWatch? unexpectedWatch = null;
        try
        {
            var error = Assert.Throws<ArgumentException>(() =>
                unexpectedWatch = FolderWatch.Start(library.PdfDirectory, library.PdfDirectory, (_, _) => Task.CompletedTask));
            Assert.Contains("scanner", error.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            if (unexpectedWatch is not null) await unexpectedWatch.DisposeAsync();
        }
    }

    [Fact]
    public async Task StoppingCancelsAnInFlightReceiver()
    {
        using var incoming = new TemporaryLibrary();
        using var destination = new TemporaryLibrary();
        var receiverStarted = new TaskCompletionSource<CancellationToken>(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseReceiver = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var watch = FolderWatch.Start(incoming.Path, destination.Path, async (_, token) =>
        {
            receiverStarted.TrySetResult(token);
            await releaseReceiver.Task.WaitAsync(token);
        });
        string fixture = Path.Combine(AppContext.BaseDirectory, "fixtures", "Lease Renewal.pdf");
        File.Copy(fixture, Path.Combine(incoming.Path, "Incoming.pdf"));

        var token = await receiverStarted.Task.WaitAsync(TimeSpan.FromSeconds(15), TestContext.Current.CancellationToken);
        try
        {
            await watch.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(3), TestContext.Current.CancellationToken);
            Assert.True(token.IsCancellationRequested);
            Assert.True(watch.Completion.IsCompletedSuccessfully);
        }
        finally
        {
            releaseReceiver.TrySetResult();
        }
    }
}
