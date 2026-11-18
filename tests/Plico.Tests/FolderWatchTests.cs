using System.Collections.Concurrent;
using System.Threading.Channels;
using Plico.Core;
using Xunit;

namespace Plico.Tests;

public sealed class FolderWatchTests
{
    private static readonly TimeSpan WatchTestDeadline = TimeSpan.FromSeconds(15);

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task RecoversMissedScansAndKeepsWatchingAfterLostNotifications(bool bufferOverflow)
    {
        using var incoming = new TemporaryLibrary();
        using var destination = new TemporaryLibrary();
        string fixture = Path.Combine(AppContext.BaseDirectory, "fixtures", "Lease Renewal.pdf");
        string unchanged = Path.Combine(incoming.Path, "Preexisting scan.pdf");
        string overwritten = Path.Combine(incoming.Path, "Overwritten scan.pdf");
        File.Copy(fixture, unchanged);
        File.WriteAllText(overwritten, "This existing file will be replaced with a scan.");

        var delivered = Channel.CreateUnbounded<IReadOnlyList<string>>();
        var source = new InterruptibleWatcher(incoming.Path);
        var notifications = new ConcurrentQueue<(WatcherChangeTypes Change, string Path)>();
        void RecordNotification(object _, FileSystemEventArgs change) =>
            notifications.Enqueue((change.ChangeType, change.FullPath));
        source.Created += RecordNotification;
        source.Changed += RecordNotification;
        source.Renamed += RecordNotification;
        await using var watch = FolderWatch.Start(incoming.Path, destination.Path,
            (paths, token) => delivered.Writer.WriteAsync(paths, token).AsTask(), _ => source, TestContext.Current.CancellationToken);
        string beforeInterruption = Path.Combine(incoming.Path, "Before interruption.pdf");
        File.Copy(fixture, beforeInterruption);
        var firstBatch = await NextBatchAsync(delivered.Reader, watch);
        Assert.True(firstBatch.Count == 1 && firstBatch[0] == beforeInterruption,
            $"Expected only {beforeInterruption}. Delivered [{string.Join(", ", firstBatch)}] after notifications [{string.Join(", ", notifications)}].");

        source.EnableRaisingEvents = false;
        string missed = Path.Combine(incoming.Path, "Missed scan.pdf");
        File.Copy(fixture, missed);
        File.Copy(fixture, overwritten, overwrite: true);
        File.Copy(fixture, Path.Combine(incoming.Path, "Scanner in progress.part"));
        source.ReportLoss(bufferOverflow ? new InternalBufferOverflowException() : new IOException("File notifications dropped."));

        var recovered = await NextBatchAsync(delivered.Reader, watch);
        Assert.Equal(new[] { missed, overwritten }, recovered.Order(StringComparer.Ordinal));

        string afterRecovery = Path.Combine(incoming.Path, "After recovery.pdf");
        File.Copy(fixture, afterRecovery);
        Assert.Equal(afterRecovery, Assert.Single(await NextBatchAsync(delivered.Reader, watch)));
        Assert.True(File.Exists(unchanged));
    }

    [Fact]
    public async Task QueuesAScanThatArrivesBeforeNotificationsAreEnabled()
    {
        using var incoming = new TemporaryLibrary();
        using var destination = new TemporaryLibrary();
        string fixture = Path.Combine(AppContext.BaseDirectory, "fixtures", "Lease Renewal.pdf");
        string preexisting = Path.Combine(incoming.Path, "Preexisting scan.pdf");
        string arrivedDuringStart = Path.Combine(incoming.Path, "Arrived during start.pdf");
        File.Copy(fixture, preexisting);
        var delivered = Channel.CreateUnbounded<IReadOnlyList<string>>();

        await using var watch = FolderWatch.Start(incoming.Path, destination.Path,
            (paths, token) => delivered.Writer.WriteAsync(paths, token).AsTask(), folder =>
            {
                File.Copy(fixture, arrivedDuringStart);
                return new FileSystemWatcher(folder);
            }, TestContext.Current.CancellationToken);

        Assert.Equal(arrivedDuringStart, Assert.Single(await NextBatchAsync(delivered.Reader, watch)));
        Assert.True(File.Exists(preexisting));
    }

    [Fact]
    public async Task ReportsTheFolderWhenTheWatchCannotRecover()
    {
        using var incoming = new TemporaryLibrary();
        using var destination = new TemporaryLibrary();
        string scannerFolder = Directory.CreateDirectory(Path.Combine(incoming.Path, "Scanner output")).FullName;
        string fixture = Path.Combine(AppContext.BaseDirectory, "fixtures", "Lease Renewal.pdf");
        string scan = Path.Combine(scannerFolder, "Scan.pdf");
        var delivered = Channel.CreateUnbounded<IReadOnlyList<string>>();
        var source = new InterruptibleWatcher(scannerFolder);
        var token = TestContext.Current.CancellationToken;
        var watch = FolderWatch.Start(scannerFolder, destination.Path,
            (paths, cancellation) => delivered.Writer.WriteAsync(paths, cancellation).AsTask(), _ => source, token);
        try
        {
            File.Copy(fixture, scan);
            Assert.Equal(scan, Assert.Single(await NextBatchAsync(delivered.Reader, watch)));
            source.EnableRaisingEvents = false;
            File.Delete(scan);
            Directory.Delete(scannerFolder);
            source.ReportLoss(new IOException("File notifications dropped."));

            var error = await Assert.ThrowsAsync<IOException>(() => watch.Completion.WaitAsync(WatchTestDeadline, token));
            Assert.Contains(scannerFolder, error.Message, StringComparison.Ordinal);
            Assert.True(watch.Completion.IsFaulted);
        }
        finally
        {
            await Assert.ThrowsAsync<IOException>(() => watch.DisposeAsync().AsTask());
        }
    }

    private static async Task<IReadOnlyList<string>> NextBatchAsync(ChannelReader<IReadOnlyList<string>> delivered, FolderWatch watch)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(WatchTestDeadline);
        var next = delivered.ReadAsync(timeout.Token).AsTask();
        if (await Task.WhenAny(next, watch.Completion) == watch.Completion) await watch.Completion;
        return await next;
    }

    private sealed class InterruptibleWatcher(string directory) : FileSystemWatcher(directory)
    {
        internal void ReportLoss(Exception failure) => OnError(new ErrorEventArgs(failure));
    }

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
        }, TestContext.Current.CancellationToken);

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
        }, TestContext.Current.CancellationToken);
        var partial = Path.Combine(incoming.Path, "scan.part");
        var finished = Path.Combine(incoming.Path, "Scan desk.pdf");
        File.Copy(fixture, partial);
        File.Move(partial, finished);

        var paths = await importedPaths.Task.WaitAsync(TimeSpan.FromSeconds(15), TestContext.Current.CancellationToken);
        Assert.Equal("Scan desk.pdf", Path.GetFileName(Assert.Single(paths)));
        Assert.Equal("Scan desk", Assert.Single(library.Find(new DocumentFilter(Query: "NORTHSTAR"), TestContext.Current.CancellationToken).Items).Title);
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
                unexpectedWatch = FolderWatch.Start(library.PdfDirectory, library.PdfDirectory, (_, _) => Task.CompletedTask,
                    TestContext.Current.CancellationToken));
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
        }, TestContext.Current.CancellationToken);
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
